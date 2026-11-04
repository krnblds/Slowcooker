import type { CronPreviewResult, Info, Job, JobsQuery, Page, QueueStats, RecurringJob, Stats } from "./types";

export class SlowcookerApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    public readonly url: string,
  ) {
    super(message);
    this.name = "SlowcookerApiError";
  }
}

export interface ClientOptions {
  /** Where `MapSlowcookerApi` was mounted. Default "/slowcooker". */
  baseUrl?: string;
  /** Extra headers, e.g. an Authorization header. Can be a function for tokens that rotate. */
  headers?: HeadersInit | (() => HeadersInit | Promise<HeadersInit>);
  /** Custom fetch (tests, SSR). Default `globalThis.fetch`. */
  fetch?: typeof fetch;
}

/**
 * A typed client for every endpoint of the dashboard API. One method per endpoint; errors become `SlowcookerApiError`.
 *
 *   const api = createClient({ baseUrl: "/slowcooker" });
 *   const failed = await api.jobs({ state: "failed", take: 100 });
 *   await api.requeue(failed.items[0].id);
 */
export function createClient(options: ClientOptions = {}) {
  const base = (options.baseUrl ?? "/slowcooker").replace(/\/$/, "");
  const doFetch = options.fetch ?? globalThis.fetch.bind(globalThis);

  async function request<T>(method: string, path: string, query?: Record<string, string | number | null | undefined>): Promise<T> {
    const url = new URL(base + path, globalThis.location?.origin ?? "http://localhost");
    for (const [key, value] of Object.entries(query ?? {})) {
      if (value !== undefined && value !== null && value !== "") url.searchParams.set(key, String(value));
    }
    const headers = new Headers(typeof options.headers === "function" ? await options.headers() : options.headers);
    headers.set("accept", "application/json");

    const response = await doFetch(url.toString(), { method, headers });
    if (response.status === 204) return undefined as T;

    const text = await response.text();
    let body: unknown = undefined;
    try {
      body = text ? JSON.parse(text) : undefined;
    } catch {
      /* not JSON */
    }

    if (!response.ok) {
      const message = (body as { error?: string } | undefined)?.error ?? `${response.status} ${response.statusText}`;
      throw new SlowcookerApiError(response.status, message, url.toString());
    }
    return body as T;
  }

  return {
    /** Library version, storage provider and server clock. */
    info: () => request<Info>("GET", "/info"),

    /** Counts by state, per queue, plus the recurring job count. Cheap; poll it. */
    stats: () => request<Stats>("GET", "/stats"),

    /** Per-queue counts only. */
    queues: () => request<QueueStats[]>("GET", "/queues"),

    /** Jobs newest first. `take` is clamped to 1..500 by the server. */
    jobs: (query: JobsQuery = {}) =>
      request<Page<Job>>("GET", "/jobs", { state: query.state, queue: query.queue, skip: query.skip, take: query.take }),

    job: (id: string) => request<Job>("GET", `/jobs/${encodeURIComponent(id)}`),

    /** Run again now with the attempt counter reset. 409 while the job is processing. */
    requeue: (id: string) => request<Job>("POST", `/jobs/${encodeURIComponent(id)}/requeue`),

    deleteJob: (id: string) => request<void>("DELETE", `/jobs/${encodeURIComponent(id)}`),

    recurring: () => request<RecurringJob[]>("GET", "/recurring"),

    recurringJob: (id: string) => request<RecurringJob>("GET", `/recurring/${encodeURIComponent(id)}`),

    /** Create a job from the template now; the schedule is untouched. */
    trigger: (id: string) => request<{ jobId: string }>("POST", `/recurring/${encodeURIComponent(id)}/trigger`),

    deleteRecurring: (id: string) => request<void>("DELETE", `/recurring/${encodeURIComponent(id)}`),

    /** Validate a cron expression and list its next runs in a time zone. Invalid expressions come back with `valid: false`, not an error. */
    cronPreview: (expression: string, timeZone?: string, count = 5) =>
      request<CronPreviewResult>("GET", "/cron/preview", { expression, timeZone, count }),
  };
}

export type SlowcookerClient = ReturnType<typeof createClient>;
