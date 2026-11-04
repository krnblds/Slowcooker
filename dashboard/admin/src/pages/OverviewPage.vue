<script setup lang="ts">
import { computed } from "vue";
import { RouterLink, useRouter } from "vue-router";
import { ErrorBanner, QueueTable, RelativeTime, StatsBar, errorHeadline, formatDateTime, useJobs, type JobState } from "@slowcooker/vue";
import Icon from "../components/Icon.vue";
import PageHeader from "../components/PageHeader.vue";
import { useSharedStats } from "../stats";
import { api } from "../client";
import { usePolling } from "@slowcooker/vue";

const router = useRouter();
const stats = useSharedStats();
const failed = useJobs({ state: "failed", pageSize: 6, intervalMs: 5000 });
const succeeded = useJobs({ state: "succeeded", pageSize: 6, intervalMs: 5000 });
const info = usePolling(() => api.info(), { intervalMs: 60_000 });

const total = computed(() => {
  const s = stats.data.value;
  return s ? s.scheduled + s.processing + s.succeeded + s.failed : null;
});
const apiPath = api.baseUrl;

function showState(state: JobState) {
  void router.push({ name: "jobs", query: { state } });
}
function showQueue(queue: string) {
  void router.push({ name: "jobs", query: { queue } });
}
</script>

<template>
  <PageHeader title="Overview">
    <template #subtitle>
      <template v-if="stats.serverTime.value">Server time {{ formatDateTime(stats.serverTime.value) }}<span v-if="total !== null"> · {{ total }} jobs retained</span></template>
      <span v-else class="sc-skeleton" />
    </template>
    <button type="button" class="sc-btn" :disabled="stats.loading.value" @click="stats.refresh()"><Icon name="refresh" /> Refresh</button>
  </PageHeader>

  <ErrorBanner :error="stats.error.value" />
  <StatsBar :stats="stats.data.value" :loading="stats.loading.value" @select="showState" />

  <div class="grid grid--2">
    <section class="sc-card">
      <header class="sc-card__header">
        <h2 class="sc-card__title">Queues <span class="sc-card__subtitle">{{ stats.data.value?.queues.length ?? 0 }}</span></h2>
        <RouterLink to="/jobs" class="sc-link">All jobs <Icon name="arrow" :size="12" /></RouterLink>
      </header>
      <div class="sc-table-wrap"><QueueTable :queues="stats.data.value?.queues" @select="showQueue" /></div>
    </section>

    <section class="sc-card">
      <header class="sc-card__header">
        <h2 class="sc-card__title">Recent failures <span v-if="failed.total.value" class="sc-badge sc-badge--failed">{{ failed.total.value }}</span></h2>
        <RouterLink :to="{ name: 'jobs', query: { state: 'failed' } }" class="sc-link">View all <Icon name="arrow" :size="12" /></RouterLink>
      </header>
      <ul v-if="failed.items.value.length" class="feed">
        <li v-for="job in failed.items.value" :key="job.id">
          <RouterLink :to="{ name: 'jobs', params: { id: job.id } }" class="feed__item">
            <span class="feed__main">
              <span class="sc-job__name">{{ job.displayName }}</span>
              <span class="feed__detail sc-text--failed">{{ errorHeadline(job.lastError) || "Failed without an error message" }}</span>
            </span>
            <span class="feed__aside">
              <span class="sc-chip">{{ job.queue }}</span>
              <RelativeTime :value="job.completedAt ?? job.runAt" :now="stats.serverTime.value" class="sc-muted" />
            </span>
          </RouterLink>
        </li>
      </ul>
      <div v-else class="sc-empty"><Icon name="check" /><span class="sc-empty__title">No failed jobs</span><span>Everything that ran recently succeeded.</span></div>
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
      <div v-else class="sc-empty"><Icon name="inbox" /><span class="sc-empty__title">Nothing completed yet</span><span>Succeeded jobs are retained for a while, then purged.</span></div>
    </section>

    <section class="sc-card">
      <header class="sc-card__header"><h2 class="sc-card__title">System</h2></header>
      <dl class="sc-props sc-card__body">
        <dt>Version</dt><dd><code :title="info.data.value?.version">{{ info.data.value?.version.split("+")[0] ?? "…" }}</code><span v-if="info.data.value?.version.includes('+')" class="sc-muted"> · {{ info.data.value?.version.split("+")[1].slice(0, 7) }}</span></dd>
        <dt>Storage</dt><dd>{{ info.data.value?.storage ?? "…" }}</dd>
        <dt>Server time</dt><dd>{{ stats.serverTime.value ? formatDateTime(stats.serverTime.value) : "…" }}</dd>
        <dt>Recurring</dt><dd>{{ stats.data.value?.recurring ?? "…" }} schedule{{ stats.data.value?.recurring === 1 ? "" : "s" }}</dd>
        <dt>API</dt><dd><code>{{ apiPath }}</code></dd>
        <dt>Polling</dt><dd>Every 3 seconds, paused while this tab is hidden</dd>
      </dl>
    </section>
  </div>
</template>
