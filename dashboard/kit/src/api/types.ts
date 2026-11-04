/** Wire types of the Slowcooker dashboard API (`Slowcooker.AspNetCore`). Mirrors `src/Slowcooker.AspNetCore/Dtos.cs`. */

export type JobState = "scheduled" | "processing" | "succeeded" | "failed";

export const JOB_STATES: readonly JobState[] = ["scheduled", "processing", "succeeded", "failed"];

export interface Job {
  id: string;
  /** Stable job name, e.g. "send-welcome-email". */
  name: string;
  displayName: string;
  /** The job record as stored. Shape depends on the job type. */
  payload: unknown;
  queue: string;
  state: JobState;
  createdAt: string;
  /** When the job becomes eligible to run. In the future for scheduled and retrying jobs. */
  runAt: string;
  /** Times a worker has fetched the job. 0 = never run. */
  attempt: number;
  lastError: string | null;
  leaseOwner: string | null;
  leaseExpiresAt: string | null;
  completedAt: string | null;
  idempotencyKey: string | null;
}

export interface RecurringJob {
  id: string;
  name: string;
  displayName: string;
  payload: unknown;
  cron: string;
  timeZoneId: string;
  queue: string;
  /** null = parked (no future occurrence or unknown time zone). */
  nextRunAt: string | null;
  lastRunAt: string | null;
  lastJobId: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface QueueStats {
  name: string;
  scheduled: number;
  processing: number;
  succeeded: number;
  failed: number;
}

export interface Stats {
  scheduled: number;
  processing: number;
  succeeded: number;
  failed: number;
  recurring: number;
  queues: QueueStats[];
  serverTime: string;
}

export interface Page<T> {
  items: T[];
  total: number;
  skip: number;
  take: number;
}

export interface Info {
  version: string;
  storage: string;
  serverTime: string;
}

export interface CronPreviewResult {
  expression: string;
  valid: boolean;
  error: string | null;
  timeZoneId: string;
  upcoming: string[];
}

export interface JobsQuery {
  state?: JobState | null;
  queue?: string | null;
  /** Exact job name, e.g. "send-welcome-email". */
  name?: string | null;
  skip?: number;
  take?: number;
}
