<script setup lang="ts">
import { computed } from "vue";
import type { Job } from "../api";
import { errorHeadline, formatDateTime } from "../format";
import RelativeTime from "./RelativeTime.vue";

const props = defineProps<{ job: Job; now?: string | null }>();

interface Step {
  key: string;
  tone: "neutral" | "scheduled" | "processing" | "succeeded" | "failed";
  title: string;
  at?: string | null;
  detail?: string;
  current?: boolean;
}

// Built only from what storage records: creation, the run time, the lease, the attempt count, the last error and
// completion. Individual attempts are not stored, so they are summarised rather than invented.
const steps = computed<Step[]>(() => {
  const j = props.job;
  const now = props.now ? new Date(props.now).getTime() : Date.now();
  const out: Step[] = [{ key: "created", tone: "neutral", title: "Enqueued", at: j.createdAt, detail: `on queue ${j.queue}` }];
  const delayed = new Date(j.runAt).getTime() - new Date(j.createdAt).getTime() > 1000;

  if (j.state === "scheduled") {
    if (j.attempt > 0 && j.lastError) {
      out.push({ key: "failed-attempt", tone: "failed", title: `Attempt ${j.attempt} failed`, detail: errorHeadline(j.lastError) });
      out.push({ key: "retry", tone: "scheduled", title: `Retry ${new Date(j.runAt).getTime() <= now ? "due now" : "scheduled"}`, at: j.runAt, detail: `will be attempt ${j.attempt + 1}`, current: true });
    } else {
      out.push({ key: "run", tone: "scheduled", title: new Date(j.runAt).getTime() <= now ? "Waiting for a worker" : "Scheduled", at: j.runAt, current: true });
    }
    return out;
  }

  if (delayed) out.push({ key: "run", tone: "scheduled", title: "Scheduled to run", at: j.runAt });
  if (j.attempt > 1) out.push({ key: "attempts", tone: "neutral", title: `${j.attempt - 1} earlier ${j.attempt - 1 === 1 ? "attempt" : "attempts"} failed`, detail: j.state === "processing" && j.lastError ? errorHeadline(j.lastError) : undefined });

  if (j.state === "processing") {
    out.push({ key: "running", tone: "processing", title: `Running attempt ${j.attempt}`, detail: j.leaseOwner ? `on ${j.leaseOwner}` : undefined, current: true });
    if (j.leaseExpiresAt) {
      const lapsed = new Date(j.leaseExpiresAt).getTime() < now;
      out.push({ key: "lease", tone: lapsed ? "failed" : "neutral", title: lapsed ? "Lease expired; another worker will take it over" : "Lease held until", at: j.leaseExpiresAt });
    }
  } else if (j.state === "succeeded") {
    out.push({ key: "done", tone: "succeeded", title: j.attempt > 1 ? `Succeeded on attempt ${j.attempt}` : "Succeeded", at: j.completedAt, current: true });
  } else {
    out.push({ key: "done", tone: "failed", title: `Failed after ${j.attempt} ${j.attempt === 1 ? "attempt" : "attempts"}`, at: j.completedAt, detail: errorHeadline(j.lastError) || undefined, current: true });
  }
  return out;
});
</script>

<template>
  <ol class="sc-timeline">
    <li v-for="s in steps" :key="s.key" class="sc-timeline__step" :class="[`sc-timeline__step--${s.tone}`, { 'sc-timeline__step--current': s.current }]">
      <span class="sc-timeline__dot" aria-hidden="true" />
      <div class="sc-timeline__body">
        <span class="sc-timeline__title">{{ s.title }}</span>
        <span v-if="s.at" class="sc-timeline__at"><time :datetime="s.at">{{ formatDateTime(s.at) }}</time> · <RelativeTime :value="s.at" :now="now" /></span>
        <span v-if="s.detail" class="sc-timeline__detail">{{ s.detail }}</span>
      </div>
    </li>
  </ol>
</template>
