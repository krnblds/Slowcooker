namespace Slowcooker.Storage.Redis;

/// <summary>
/// Every mutation is one Lua script so it is atomic on the server. Keys are derived from the prefix in ARGV[1].
/// Layout (p = prefix):
///   p:job:{id}            HASH   the job
///   p:queue:{queue}       ZSET   scheduled jobs, score = run_at (ms)
///   p:processing          ZSET   leased jobs, score = lease_expires_at (ms)
///   p:idx:all             ZSET   all jobs, score = created_at (ms)        (listing)
///   p:idx:state:{n}       ZSET   jobs by state, score = created_at (ms)   (listing, filtering)
///   p:idx:queue:{queue}   ZSET   jobs by queue, score = created_at (ms)   (listing, filtering)
///   p:idx:name:{name}     ZSET   jobs by job name, score = created_at (ms) (listing, filtering)
///   p:idx:completed:{n}   ZSET   succeeded/failed jobs, score = completed_at (ms) (retention)
///   p:stats               HASH   "{queue}|{state}" -> count
///   p:idem:{key}          STRING id of the job that owns an idempotency key
///   p:recurring:{id}      HASH   a recurring job
///   p:recurring:ids       SET    all recurring job ids
///   p:recurring:due       ZSET   recurring jobs with a next run, score = next_run_at (ms)
///   p:signal              PUBSUB channel for wake-ups
/// </summary>
internal static class RedisScripts
{
    private const string Prelude = """
        local p = ARGV[1]
        local function jobKey(id) return p .. ':job:' .. id end
        local function queueKey(q) return p .. ':queue:' .. q end
        local function stateIdx(s) return p .. ':idx:state:' .. s end
        local function queueIdx(q) return p .. ':idx:queue:' .. q end
        local function nameIdx(n) return p .. ':idx:name:' .. n end
        local function completedIdx(s) return p .. ':idx:completed:' .. s end
        local function stat(q, s, d) redis.call('HINCRBY', p .. ':stats', q .. '|' .. s, d) end
        local function moveState(id, q, from, to, created)
          if from ~= to then
            redis.call('ZREM', stateIdx(from), id)
            redis.call('ZADD', stateIdx(to), created, id)
            stat(q, from, -1)
            stat(q, to, 1)
          end
        end
        local function signal() redis.call('PUBLISH', p .. ':signal', '1') end
        local function insertJob(id, q, name, payload, created, runAt, idem)
          redis.call('HSET', jobKey(id), 'id', id, 'queue', q, 'state', '0', 'name', name, 'payload', payload,
            'created_at', created, 'run_at', runAt, 'attempt', '0', 'idempotency_key', idem)
          redis.call('ZADD', queueKey(q), runAt, id)
          redis.call('ZADD', stateIdx(0), created, id)
          redis.call('ZADD', queueIdx(q), created, id)
          redis.call('ZADD', nameIdx(name), created, id)
          redis.call('ZADD', p .. ':idx:all', created, id)
          stat(q, 0, 1)
          signal()
        end
        local function owned(id, worker)
          local h = redis.call('HMGET', jobKey(id), 'state', 'lease_owner', 'queue', 'created_at', 'run_at', 'attempt')
          if h[1] ~= '1' or h[2] ~= worker then return nil end
          return h
        end
        """;

    /// <summary>ARGV: p, id, queue, name, payload, created, run_at, idem ('' = none). Returns the id (existing one on idempotency hit).</summary>
    public const string Create = Prelude + "\n" + """
        local id, q, name, payload, created, runAt, idem = ARGV[2], ARGV[3], ARGV[4], ARGV[5], ARGV[6], ARGV[7], ARGV[8]
        if idem ~= '' then
          local existing = redis.call('GET', p .. ':idem:' .. idem)
          if existing and redis.call('EXISTS', jobKey(existing)) == 1 then return existing end
          redis.call('SET', p .. ':idem:' .. idem, id)
        end
        insertJob(id, q, name, payload, created, runAt, idem)
        return id
        """;

    /// <summary>ARGV: p, worker, now, leaseUntil, max, queue1, queue2, ... Returns an array of HGETALL results for the fetched jobs.</summary>
    public const string Fetch = Prelude + "\n" + """
        local worker, now, leaseUntil, max = ARGV[2], ARGV[3], ARGV[4], tonumber(ARGV[5])
        local wanted = {}
        for i = 6, #ARGV do wanted[ARGV[i]] = true end
        local candidates = {}
        -- expired leases (crash recovery)
        local expired = redis.call('ZRANGEBYSCORE', p .. ':processing', '-inf', now, 'LIMIT', 0, max)
        for _, id in ipairs(expired) do
          local h = redis.call('HMGET', jobKey(id), 'queue', 'run_at')
          if h[1] == false then
            redis.call('ZREM', p .. ':processing', id)
          elseif wanted[h[1]] then
            candidates[#candidates + 1] = { id = id, q = h[1], runAt = tonumber(h[2]), from = 1 }
          end
        end
        -- due scheduled jobs
        for i = 6, #ARGV do
          local q = ARGV[i]
          local due = redis.call('ZRANGEBYSCORE', queueKey(q), '-inf', now, 'WITHSCORES', 'LIMIT', 0, max)
          for j = 1, #due, 2 do
            candidates[#candidates + 1] = { id = due[j], q = q, runAt = tonumber(due[j + 1]), from = 0 }
          end
        end
        table.sort(candidates, function(a, b)
          if a.runAt ~= b.runAt then return a.runAt < b.runAt end
          return a.id < b.id
        end)
        local result = {}
        for i = 1, math.min(max, #candidates) do
          local c = candidates[i]
          local created = redis.call('HGET', jobKey(c.id), 'created_at')
          if c.from == 0 then redis.call('ZREM', queueKey(c.q), c.id) end
          redis.call('HINCRBY', jobKey(c.id), 'attempt', 1)
          redis.call('HSET', jobKey(c.id), 'state', '1', 'lease_owner', worker, 'lease_expires_at', leaseUntil)
          redis.call('ZADD', p .. ':processing', leaseUntil, c.id)
          moveState(c.id, c.q, c.from, 1, created)
          result[#result + 1] = redis.call('HGETALL', jobKey(c.id))
        end
        return result
        """;

    /// <summary>ARGV: p, id, worker, now. Returns 1 or 0.</summary>
    public const string Complete = Prelude + "\n" + """
        local id, worker, now = ARGV[2], ARGV[3], ARGV[4]
        local h = owned(id, worker)
        if not h then return 0 end
        redis.call('HSET', jobKey(id), 'state', '2', 'completed_at', now)
        redis.call('HDEL', jobKey(id), 'lease_owner', 'lease_expires_at')
        redis.call('ZREM', p .. ':processing', id)
        redis.call('ZADD', completedIdx(2), now, id)
        moveState(id, h[3], 1, 2, h[4])
        return 1
        """;

    /// <summary>ARGV: p, id, worker, now, error. Returns 1 or 0.</summary>
    public const string Fail = Prelude + "\n" + """
        local id, worker, now, err = ARGV[2], ARGV[3], ARGV[4], ARGV[5]
        local h = owned(id, worker)
        if not h then return 0 end
        redis.call('HSET', jobKey(id), 'state', '3', 'completed_at', now, 'last_error', err)
        redis.call('HDEL', jobKey(id), 'lease_owner', 'lease_expires_at')
        redis.call('ZREM', p .. ':processing', id)
        redis.call('ZADD', completedIdx(3), now, id)
        moveState(id, h[3], 1, 3, h[4])
        return 1
        """;

    /// <summary>ARGV: p, id, worker, run_at, error. Returns 1 or 0.</summary>
    public const string Retry = Prelude + "\n" + """
        local id, worker, runAt, err = ARGV[2], ARGV[3], ARGV[4], ARGV[5]
        local h = owned(id, worker)
        if not h then return 0 end
        redis.call('HSET', jobKey(id), 'state', '0', 'run_at', runAt, 'last_error', err)
        redis.call('HDEL', jobKey(id), 'lease_owner', 'lease_expires_at')
        redis.call('ZREM', p .. ':processing', id)
        redis.call('ZADD', queueKey(h[3]), runAt, id)
        moveState(id, h[3], 1, 0, h[4])
        signal()
        return 1
        """;

    /// <summary>ARGV: p, id, worker. Returns 1 or 0.</summary>
    public const string Requeue = Prelude + "\n" + """
        local id, worker = ARGV[2], ARGV[3]
        local h = owned(id, worker)
        if not h then return 0 end
        local attempt = math.max(0, tonumber(h[6]) - 1)
        redis.call('HSET', jobKey(id), 'state', '0', 'attempt', attempt)
        redis.call('HDEL', jobKey(id), 'lease_owner', 'lease_expires_at')
        redis.call('ZREM', p .. ':processing', id)
        redis.call('ZADD', queueKey(h[3]), h[5], id)
        moveState(id, h[3], 1, 0, h[4])
        signal()
        return 1
        """;

    /// <summary>ARGV: p, id, worker, leaseUntil. Returns 1 or 0.</summary>
    public const string ExtendLease = Prelude + "\n" + """
        local id, worker, leaseUntil = ARGV[2], ARGV[3], ARGV[4]
        if not owned(id, worker) then return 0 end
        redis.call('HSET', jobKey(id), 'lease_expires_at', leaseUntil)
        redis.call('ZADD', p .. ':processing', leaseUntil, id)
        return 1
        """;

    /// <summary>Operator requeue. ARGV: p, id, now. Returns 1 or 0.</summary>
    public const string OperatorRequeue = Prelude + "\n" + """
        local id, now = ARGV[2], ARGV[3]
        local h = redis.call('HMGET', jobKey(id), 'state', 'queue', 'created_at')
        if h[1] == false or h[1] == '1' then return 0 end
        local from = tonumber(h[1])
        redis.call('HSET', jobKey(id), 'state', '0', 'run_at', now, 'attempt', '0')
        redis.call('HDEL', jobKey(id), 'lease_owner', 'lease_expires_at', 'completed_at')
        redis.call('ZREM', completedIdx(2), id)
        redis.call('ZREM', completedIdx(3), id)
        redis.call('ZADD', queueKey(h[2]), now, id)
        moveState(id, h[2], from, 0, h[3])
        signal()
        return 1
        """;

    /// <summary>ARGV: p, id. Returns 1 or 0.</summary>
    public const string Delete = Prelude + "\n" + """
        local id = ARGV[2]
        local h = redis.call('HMGET', jobKey(id), 'state', 'queue', 'idempotency_key', 'name')
        if h[1] == false then return 0 end
        local state, q, idem, name = tonumber(h[1]), h[2], h[3], h[4]
        redis.call('DEL', jobKey(id))
        redis.call('ZREM', queueKey(q), id)
        redis.call('ZREM', p .. ':processing', id)
        redis.call('ZREM', stateIdx(state), id)
        redis.call('ZREM', queueIdx(q), id)
        redis.call('ZREM', nameIdx(name), id)
        redis.call('ZREM', p .. ':idx:all', id)
        redis.call('ZREM', completedIdx(2), id)
        redis.call('ZREM', completedIdx(3), id)
        stat(q, state, -1)
        if idem and idem ~= '' and redis.call('GET', p .. ':idem:' .. idem) == id then
          redis.call('DEL', p .. ':idem:' .. idem)
        end
        return 1
        """;

    /// <summary>ARGV: p, state, before, max. Returns the number of deleted jobs.</summary>
    public const string Purge = Prelude + "\n" + """
        local state, before, max = ARGV[2], ARGV[3], tonumber(ARGV[4])
        local ids = redis.call('ZRANGEBYSCORE', completedIdx(state), '-inf', '(' .. before, 'LIMIT', 0, max)
        local n = 0
        for _, id in ipairs(ids) do
          local h = redis.call('HMGET', jobKey(id), 'queue', 'idempotency_key', 'name')
          if h[1] then
            redis.call('DEL', jobKey(id))
            redis.call('ZREM', stateIdx(state), id)
            redis.call('ZREM', queueIdx(h[1]), id)
            redis.call('ZREM', nameIdx(h[3]), id)
            redis.call('ZREM', p .. ':idx:all', id)
            stat(h[1], state, -1)
            if h[2] and h[2] ~= '' and redis.call('GET', p .. ':idem:' .. h[2]) == id then
              redis.call('DEL', p .. ':idem:' .. h[2])
            end
            n = n + 1
          end
          redis.call('ZREM', completedIdx(state), id)
        end
        return n
        """;

    /// <summary>ARGV: p, state ('' = any), queue ('' = any), name ('' = any), skip, take, temp key suffix. Returns [total, [HGETALL, ...]].</summary>
    public const string Query = Prelude + "\n" + """
        local state, q, name, skip, take = ARGV[2], ARGV[3], ARGV[4], tonumber(ARGV[5]), tonumber(ARGV[6])
        local keys = {}
        if state ~= '' then keys[#keys + 1] = stateIdx(state) end
        if q ~= '' then keys[#keys + 1] = queueIdx(q) end
        if name ~= '' then keys[#keys + 1] = nameIdx(name) end
        local key
        local temp = nil
        if #keys == 0 then key = p .. ':idx:all'
        elseif #keys == 1 then key = keys[1]
        else
          temp = p .. ':tmp:' .. ARGV[7]
          local args = { 'ZINTERSTORE', temp, #keys }
          for _, k in ipairs(keys) do args[#args + 1] = k end
          args[#args + 1] = 'AGGREGATE'
          args[#args + 1] = 'MAX'
          redis.call(unpack(args))
          key = temp
        end
        local total = redis.call('ZCARD', key)
        local ids = redis.call('ZREVRANGE', key, skip, skip + take - 1)
        if temp then redis.call('DEL', temp) end
        local items = {}
        for _, id in ipairs(ids) do items[#items + 1] = redis.call('HGETALL', jobKey(id)) end
        return { total, items }
        """;

    /// <summary>ARGV: p, id, name, payload, cron, tz, queue, next ('' = none), last_run (''), last_job (''), created, updated.</summary>
    public const string UpsertRecurring = Prelude + "\n" + """
        local id = ARGV[2]
        local key = p .. ':recurring:' .. id
        redis.call('HSET', key, 'id', id, 'name', ARGV[3], 'payload', ARGV[4], 'cron', ARGV[5], 'time_zone_id', ARGV[6], 'queue', ARGV[7],
          'next_run_at', ARGV[8], 'last_run_at', ARGV[9], 'last_job_id', ARGV[10], 'created_at', ARGV[11], 'updated_at', ARGV[12])
        redis.call('SADD', p .. ':recurring:ids', id)
        if ARGV[8] ~= '' then redis.call('ZADD', p .. ':recurring:due', ARGV[8], id) else redis.call('ZREM', p .. ':recurring:due', id) end
        return 1
        """;

    /// <summary>ARGV: p, id. Returns 1 or 0.</summary>
    public const string DeleteRecurring = Prelude + "\n" + """
        local id = ARGV[2]
        local n = redis.call('DEL', p .. ':recurring:' .. id)
        redis.call('SREM', p .. ':recurring:ids', id)
        redis.call('ZREM', p .. ':recurring:due', id)
        return n
        """;

    /// <summary>ARGV: p, now, max. Returns [HGETALL, ...] soonest first.</summary>
    public const string DueRecurring = Prelude + "\n" + """
        local ids = redis.call('ZRANGEBYSCORE', p .. ':recurring:due', '-inf', ARGV[2], 'LIMIT', 0, tonumber(ARGV[3]))
        local items = {}
        for _, id in ipairs(ids) do items[#items + 1] = redis.call('HGETALL', p .. ':recurring:' .. id) end
        return items
        """;

    /// <summary>ARGV: p, id, expected, next ('' = none), job id, queue, name, payload, created, run_at. Returns 1 or 0.</summary>
    public const string TriggerRecurring = Prelude + "\n" + """
        local id, expected, nextRun = ARGV[2], ARGV[3], ARGV[4]
        local key = p .. ':recurring:' .. id
        local current = redis.call('HGET', key, 'next_run_at')
        if current == false or current ~= expected then return 0 end
        local jobId, q, name, payload, created, runAt = ARGV[5], ARGV[6], ARGV[7], ARGV[8], ARGV[9], ARGV[10]
        redis.call('HSET', key, 'next_run_at', nextRun, 'last_run_at', created, 'last_job_id', jobId, 'updated_at', created)
        if nextRun ~= '' then redis.call('ZADD', p .. ':recurring:due', nextRun, id) else redis.call('ZREM', p .. ':recurring:due', id) end
        insertJob(jobId, q, name, payload, created, runAt, '')
        return 1
        """;
}
