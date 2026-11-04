<script setup lang="ts">
import { computed } from "vue";
import { RouterLink, useRouter } from "vue-router";
import { ErrorBanner, RelativeTime, describeCron, errorHeadline, formatDateTime, useJobs, useRecurring, type JobState } from "@slowcooker/vue";
import Icon from "../components/Icon.vue";
import PageHeader from "../components/PageHeader.vue";
import QueueBar from "../components/QueueBar.vue";
import Sparkline from "../components/Sparkline.vue";
import { followLive } from "../live";
import { useSharedStats } from "../stats";

const router = useRouter();
const stats = useSharedStats();
// The 200 most recent failures are enough to rank what is breaking; the count of all failures comes from /stats.
const failed = useJobs({ state: "failed", pageSize: 200, intervalMs: 10_000 });
const succeeded = useJobs({ state: "succeeded", pageSize: 6, intervalMs: 5000 });
const recurring = useRecurring({ intervalMs: 15_000 });
followLive(failed);
followLive(succeeded);
followLive(recurring);

const STATES: { state: JobState; label: string; hint: string }[] = [
  { state: "scheduled", label: "Scheduled", hint: "Due now, later, or waiting to retry" },
  { state: "processing", label: "Processing", hint: "Leased by a worker right now" },
  { state: "succeeded", label: "Succeeded", hint: "Retained until the retention window passes" },
  { state: "failed", label: "Failed", hint: "Out of retries; needs a person" },
];

const total = computed(() => {
  const s = stats.data.value;
  return s ? s.scheduled + s.processing + s.succeeded + s.failed : null;
});
const series = (state: JobState) => stats.history.value.map((h) => h[state]);

const topErrors = computed(() => {
  const groups = new Map<string, { headline: string; name: string; count: number; latest: string; jobId: string }>();
  for (const job of failed.items.value) {
    const headline = errorHeadline(job.lastError) || "Failed without an error message";
    const key = `${job.name}\u0000${headline}`;
    const at = job.completedAt ?? job.runAt;
    const g = groups.get(key);
    if (!g) groups.set(key, { headline, name: job.name, count: 1, latest: at, jobId: job.id });
    else {
      g.count++;
      if (at > g.latest) {
        g.latest = at;
        g.jobId = job.id;
      }
    }
  }
  return [...groups.values()].sort((a, b) => b.count - a.count || b.latest.localeCompare(a.latest)).slice(0, 6);
});

const upcoming = computed(() =>
  (recurring.data.value ?? [])
    .filter((r) => r.nextRunAt)
    .sort((a, b) => a.nextRunAt!.localeCompare(b.nextRunAt!))
    .slice(0, 5),
);
const parked = computed(() => (recurring.data.value ?? []).filter((r) => !r.nextRunAt).length);

function show(state: JobState) {
  void router.push({ name: "jobs", query: { state } });
}
function refreshAll() {
  void stats.refresh();
  void failed.refresh();
  void succeeded.refresh();
  void recurring.refresh();
}
</script>

<template>
  <PageHeader title="Overview">
    <template #subtitle>
      <template v-if="stats.serverTime.value">Server time {{ formatDateTime(stats.serverTime.value) }}<span v-if="total !== null"> · {{ total.toLocaleString() }} jobs retained</span></template>
      <span v-else class="sc-skeleton" />
    </template>
    <button type="button" class="sc-btn" :disabled="stats.loading.value" @click="refreshAll"><Icon name="refresh" /> Refresh</button>
  </PageHeader>

  <section v-if="stats.data.value" class="health" :class="stats.data.value.failed ? 'health--bad' : 'health--ok'" aria-live="polite">
    <span class="health__icon"><Icon :name="stats.data.value.failed ? 'alert' : 'check'" :size="18" /></span>
    <div class="health__text">
      <template v-if="stats.data.value.failed">
        <strong>{{ stats.data.value.failed.toLocaleString() }} {{ stats.data.value.failed === 1 ? "job has" : "jobs have" }} failed and will not be retried.</strong>
        <span>Review the errors, fix the cause, then requeue them.</span>
      </template>
      <template v-else>
        <strong>No failed jobs.</strong>
        <span>{{ stats.data.value.processing }} processing, {{ stats.data.value.scheduled }} scheduled across {{ stats.data.value.queues.length }} {{ stats.data.value.queues.length === 1 ? "queue" : "queues" }}.</span>
      </template>
    </div>
    <RouterLink v-if="stats.data.value.failed" :to="{ name: 'jobs', query: { state: 'failed' } }" class="sc-btn sc-btn--primary">Review failures <Icon name="arrow" :size="14" /></RouterLink>
  </section>

  <ErrorBanner :error="failed.error.value" />

  <div class="tiles">
    <button v-for="s in STATES" :key="s.state" type="button" class="tile" :class="`tile--${s.state}`" :title="s.hint" @click="show(s.state)">
      <span class="tile__label">{{ s.label }}</span>
      <span class="tile__value">{{ stats.data.value ? stats.data.value[s.state].toLocaleString() : "–" }}</span>
      <Sparkline class="tile__spark" :values="series(s.state)" :label="s.label" />
    </button>
  </div>
  <p class="caption">Trends cover the time since this page was opened; the server keeps counts, not history.</p>

  <div class="grid grid--wide">
    <section class="sc-card">
      <header class="sc-card__header">
        <h2 class="sc-card__title">Queues <span class="sc-card__subtitle">{{ stats.data.value?.queues.length ?? 0 }}</span></h2>
        <div class="legend" aria-hidden="true">
          <span class="legend__item legend__item--scheduled">Scheduled</span>
          <span class="legend__item legend__item--processing">Processing</span>
          <span class="legend__item legend__item--succeeded">Succeeded</span>
          <span class="legend__item legend__item--failed">Failed</span>
        </div>
      </header>
      <div class="sc-table-wrap">
        <table class="sc-table queues">
          <thead>
            <tr><th>Queue</th><th class="queues__bar-col">Distribution</th><th class="sc-num">Pending</th><th class="sc-num">Failed</th></tr>
          </thead>
          <tbody>
            <tr v-for="q in stats.data.value?.queues ?? []" :key="q.name" class="sc-row--clickable" @click="router.push({ name: 'jobs', query: { queue: q.name } })">
              <td><RouterLink :to="{ name: 'jobs', query: { queue: q.name } }" class="queues__name" @click.stop><span class="sc-chip">{{ q.name }}</span></RouterLink></td>
              <td class="queues__bar-col"><QueueBar :queue="q" /></td>
              <td class="sc-num">{{ (q.scheduled + q.processing).toLocaleString() }}</td>
              <td class="sc-num" :class="{ 'sc-text--failed': q.failed > 0 }">{{ q.failed.toLocaleString() }}</td>
            </tr>
            <tr v-if="!stats.data.value?.queues.length"><td colspan="4"><div class="sc-empty"><Icon name="layers" /><span class="sc-empty__title">No queues yet</span><span>Queues appear as soon as a job is enqueued.</span></div></td></tr>
          </tbody>
        </table>
      </div>
    </section>

    <section class="sc-card">
      <header class="sc-card__header">
        <h2 class="sc-card__title">Up next <span v-if="parked" class="sc-badge sc-badge--parked" title="Schedules with no future run">{{ parked }} parked</span></h2>
        <RouterLink to="/recurring" class="sc-link">Schedules <Icon name="arrow" :size="12" /></RouterLink>
      </header>
      <ul v-if="upcoming.length" class="feed">
        <li v-for="r in upcoming" :key="r.id">
          <RouterLink :to="{ name: 'recurring', params: { id: r.id } }" class="feed__item">
            <span class="feed__main">
              <span class="sc-job__name">{{ r.id }}</span>
              <span class="feed__detail sc-muted">{{ describeCron(r.cron) ?? r.cron }} · {{ r.timeZoneId }}</span>
            </span>
            <span class="feed__aside"><RelativeTime :value="r.nextRunAt" :now="stats.serverTime.value" /></span>
          </RouterLink>
        </li>
      </ul>
      <div v-else-if="recurring.data.value" class="sc-empty"><Icon name="recurring" /><span class="sc-empty__title">No upcoming runs</span><span>Declare schedules in code with <code>IRecurringJobClient</code>.</span></div>
      <div v-else class="sc-empty"><span class="sc-skeleton" /></div>
    </section>

    <section class="sc-card">
      <header class="sc-card__header">
        <h2 class="sc-card__title">Top errors <span v-if="failed.total.value" class="sc-card__subtitle">from the {{ Math.min(failed.total.value, 200) }} most recent failures</span></h2>
        <RouterLink :to="{ name: 'jobs', query: { state: 'failed' } }" class="sc-link">All failures <Icon name="arrow" :size="12" /></RouterLink>
      </header>
      <ul v-if="topErrors.length" class="feed">
        <li v-for="e in topErrors" :key="e.name + e.headline">
          <RouterLink :to="{ name: 'jobs', query: { state: 'failed', name: e.name } }" class="feed__item">
            <span class="feed__main">
              <span class="sc-job__name">{{ e.name }}</span>
              <span class="feed__detail sc-text--failed" :title="e.headline">{{ e.headline }}</span>
            </span>
            <span class="feed__aside">
              <span class="count-pill" :title="`${e.count} failed jobs with this error`">× {{ e.count }}</span>
              <RelativeTime :value="e.latest" :now="stats.serverTime.value" class="sc-muted" />
            </span>
          </RouterLink>
        </li>
      </ul>
      <div v-else-if="failed.data.value" class="sc-empty"><Icon name="check" /><span class="sc-empty__title">Nothing is failing</span><span>Jobs that run out of retries show up here, grouped by error.</span></div>
      <div v-else class="sc-empty"><span class="sc-skeleton" /></div>
    </section>

    <section class="sc-card">
      <header class="sc-card__header">
        <h2 class="sc-card__title">Recently completed</h2>
        <RouterLink :to="{ name: 'jobs', query: { state: 'succeeded' } }" class="sc-link">View all <Icon name="arrow" :size="12" /></RouterLink>
      </header>
      <ul v-if="succeeded.items.value.length" class="feed">
        <li v-for="job in succeeded.items.value" :key="job.id">
          <RouterLink :to="{ name: 'jobs', params: { id: job.id } }" class="feed__item">
            <span class="feed__main">
              <span class="sc-job__name">{{ job.displayName }}</span>
              <span class="feed__detail sc-muted">{{ job.attempt > 1 ? `Succeeded on attempt ${job.attempt}` : "Succeeded first time" }}</span>
            </span>
            <span class="feed__aside">
              <span class="sc-chip">{{ job.queue }}</span>
              <RelativeTime :value="job.completedAt" :now="stats.serverTime.value" class="sc-muted" />
            </span>
          </RouterLink>
        </li>
      </ul>
      <div v-else-if="succeeded.data.value" class="sc-empty"><Icon name="inbox" /><span class="sc-empty__title">Nothing completed yet</span><span>Succeeded jobs are retained for a while, then purged.</span></div>
      <div v-else class="sc-empty"><span class="sc-skeleton" /></div>
    </section>
  </div>
</template>
