<script setup lang="ts">
import { computed, ref } from "vue";
import { createClient, provideSlowcooker, useStats, usePolling, ErrorBanner, StatsBar } from "@slowcooker/vue";
import JobsView from "./JobsView.vue";
import OverviewView from "./OverviewView.vue";
import RecurringView from "./RecurringView.vue";

// AddAdminUserInterface() serves this page at {Path}/ and the API at {Path}/api, so the API is found relative to the page
// and the dashboard works at any mount path. Browser cookies travel with the requests, so no auth header is needed here.
// In your own app: createClient({ baseUrl: "/slowcooker/api", headers: () => ({ Authorization: `Bearer ${token}` }) }).
const api = createClient({ baseUrl: new URL("api", document.baseURI).pathname });
provideSlowcooker(api);

const stats = useStats({ client: api, intervalMs: 2000 });
const info = usePolling(() => api.info(), { intervalMs: 0 });

type Tab = "overview" | "jobs" | "recurring";
const tab = ref<Tab>("overview");
const jobsView = ref<InstanceType<typeof JobsView>>();

function showState(state: "scheduled" | "processing" | "succeeded" | "failed") {
  tab.value = "jobs";
  queueMicrotask(() => jobsView.value?.filterByState(state));
}
function showQueue(queue: string) {
  tab.value = "jobs";
  queueMicrotask(() => jobsView.value?.filterByQueue(queue));
}

const serverTime = computed(() => stats.data.value?.serverTime ?? null);
</script>

<template>
  <div class="demo">
    <header class="demo__header">
      <h1>Slowcooker</h1>
      <span class="sc-muted">
        <template v-if="info.data.value">v{{ info.data.value.version }} · {{ info.data.value.storage }}</template>
      </span>
    </header>

    <ErrorBanner :error="stats.error.value" />
    <StatsBar :stats="stats.data.value" :loading="stats.loading.value" @select="showState" />

    <nav class="demo__tabs">
      <button class="demo__tab" :class="{ 'demo__tab--active': tab === 'overview' }" @click="tab = 'overview'">Overview</button>
      <button class="demo__tab" :class="{ 'demo__tab--active': tab === 'jobs' }" @click="tab = 'jobs'">Jobs</button>
      <button class="demo__tab" :class="{ 'demo__tab--active': tab === 'recurring' }" @click="tab = 'recurring'">Recurring</button>
    </nav>

    <OverviewView v-if="tab === 'overview'" :stats="stats.data.value" @select-queue="showQueue" />
    <JobsView v-else-if="tab === 'jobs'" ref="jobsView" :now="serverTime" />
    <RecurringView v-else :now="serverTime" />
  </div>
</template>
