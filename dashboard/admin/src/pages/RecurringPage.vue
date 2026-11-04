<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { RouterLink, useRouter } from "vue-router";
import { CronPreview, ErrorBanner, JobTable, PayloadView, RecurringTable, RelativeTime, describeCron, formatDateTime, useJobs, useRecurring, type RecurringJob } from "@slowcooker/vue";
import Drawer from "../components/Drawer.vue";
import Icon from "../components/Icon.vue";
import PageHeader from "../components/PageHeader.vue";
import { confirm } from "../confirm";
import { copy } from "../copy";
import { followLive } from "../live";
import { useSharedStats } from "../stats";
import { attempt } from "../toasts";

const props = defineProps<{ id?: string }>();
const router = useRouter();
const stats = useSharedStats();
const recurring = useRecurring({ intervalMs: 5000 });
followLive(recurring);

const search = ref("");
const items = computed(() => recurring.data.value ?? []);
const visible = computed(() => {
  const q = search.value.trim().toLowerCase();
  return q ? items.value.filter((r) => `${r.id} ${r.name} ${r.queue} ${r.cron}`.toLowerCase().includes(q)) : items.value;
});
const selected = computed(() => (props.id ? items.value.find((r) => r.id === props.id) : undefined));
const parked = computed(() => items.value.filter((r) => !r.nextRunAt).length);

// Jobs do not record which schedule created them, so the drawer lists recent jobs of the same job name and says so.
const recent = useJobs({ pageSize: 5, intervalMs: 5000, immediate: false });
followLive(recent);
watch(
  () => selected.value?.name,
  (name) => {
    recent.name.value = name ?? null;
  },
  { immediate: true },
);

function open(job: RecurringJob) {
  void router.push({ name: "recurring", params: { id: job.id } });
}
function closeDetail() {
  void router.push({ name: "recurring" });
}

async function trigger(job: RecurringJob) {
  if (await attempt(() => recurring.trigger(job.id), `Triggered ${job.id}; it will run shortly`)) {
    void stats.refresh();
    if (props.id === job.id) void recent.refresh();
  }
}
async function remove(job: RecurringJob) {
  const ok = await confirm({
    title: "Delete schedule?",
    message: `${job.id} will stop running. Jobs it already created are kept. If the application declares it at startup, it comes back on the next restart.`,
    confirmLabel: "Delete",
    danger: true,
  });
  if (!ok) return;
  if (await attempt(() => recurring.remove(job.id), `Deleted ${job.id}`)) {
    if (props.id === job.id) closeDetail();
  }
}
</script>

<template>
  <PageHeader title="Recurring jobs">
    <template #subtitle>
      {{ items.length }} {{ items.length === 1 ? "schedule" : "schedules" }}<template v-if="parked"> · {{ parked }} parked</template> · declared in code with <code>IRecurringJobClient</code>
    </template>
    <button type="button" class="sc-btn" :disabled="recurring.loading.value" @click="recurring.refresh()"><Icon name="refresh" /> Refresh</button>
  </PageHeader>

  <ErrorBanner :error="recurring.error.value" />

  <section class="sc-card">
    <div class="toolbar">
      <label class="search">
        <Icon name="search" class="search__icon" />
        <input v-model="search" class="sc-input search__input" type="search" placeholder="Filter by id, job, queue or cron" aria-label="Filter schedules" />
      </label>
      <span class="sc-muted toolbar__meta">{{ visible.length }} of {{ items.length }}</span>
    </div>
    <div class="sc-table-wrap">
      <RecurringTable :items="visible" :now="stats.serverTime.value" @select="open" @trigger="trigger" @delete="remove" />
    </div>
  </section>

  <Drawer v-if="props.id" title="Schedule" @close="closeDetail">
    <template #header>
      <div class="drawer__heading">
        <h2 class="drawer__title">Schedule</h2>
        <button v-if="selected" type="button" class="sc-btn sc-btn--ghost sc-btn--sm id-chip" :title="`Copy ${selected.id}`" @click="copy(selected.id, 'Schedule id copied')">
          <code>{{ selected.id }}</code><Icon name="copy" :size="12" />
        </button>
      </div>
    </template>
    <template v-if="selected">
      <div class="sc-detail">
        <header class="sc-detail__header">
          <h2 class="sc-detail__title">
            {{ describeCron(selected.cron) ?? selected.cron }}
            <span v-if="selected.nextRunAt" class="sc-badge sc-badge--scheduled">active</span>
            <span v-else class="sc-badge sc-badge--parked">parked</span>
          </h2>
          <div class="sc-detail__actions">
            <button type="button" class="sc-btn sc-btn--primary" @click="trigger(selected)"><Icon name="play" /> Run now</button>
            <button type="button" class="sc-btn sc-btn--danger" @click="remove(selected)"><Icon name="trash" /> Delete</button>
          </div>
        </header>

        <div class="countdown" :class="{ 'countdown--parked': !selected.nextRunAt }">
          <template v-if="selected.nextRunAt">
            <span class="countdown__label">Next run</span>
            <span class="countdown__value"><RelativeTime :value="selected.nextRunAt" :now="stats.serverTime.value" /></span>
            <span class="countdown__at">{{ formatDateTime(selected.nextRunAt) }}</span>
          </template>
          <template v-else>
            <span class="countdown__label">Parked</span>
            <span class="countdown__at">The expression has no future occurrence, or the time zone {{ selected.timeZoneId }} is unknown on the scheduler.</span>
          </template>
        </div>

        <dl class="sc-props">
          <dt>Job</dt><dd><code>{{ selected.name }}</code></dd>
          <dt>Cron</dt><dd><span class="sc-chip">{{ selected.cron }}</span> in {{ selected.timeZoneId }}</dd>
          <dt>Queue</dt><dd><RouterLink :to="{ name: 'jobs', query: { queue: selected.queue } }" class="sc-link"><span class="sc-chip">{{ selected.queue }}</span></RouterLink></dd>
          <dt>Last run</dt>
          <dd>
            <template v-if="selected.lastRunAt">{{ formatDateTime(selected.lastRunAt) }} (<RelativeTime :value="selected.lastRunAt" :now="stats.serverTime.value" />)</template>
            <span v-else class="sc-muted">Never</span>
          </dd>
          <dt v-if="selected.lastJobId">Last job</dt>
          <dd v-if="selected.lastJobId"><RouterLink class="sc-link" :to="{ name: 'jobs', params: { id: selected.lastJobId } }"><code>{{ selected.lastJobId }}</code></RouterLink></dd>
          <dt>Created</dt><dd>{{ formatDateTime(selected.createdAt) }}</dd>
          <dt>Updated</dt><dd>{{ formatDateTime(selected.updatedAt) }}</dd>
        </dl>

        <h3>Upcoming runs</h3>
        <CronPreview :expression="selected.cron" :time-zone="selected.timeZoneId" :count="5" />

        <h3>Recent {{ selected.name }} jobs</h3>
        <p class="sc-muted fine">Jobs do not record which schedule created them, so this lists every recent job with this job name.</p>
        <div class="sc-table-wrap embedded">
          <JobTable :jobs="recent.items.value" :now="stats.serverTime.value" readonly @select="(j) => router.push({ name: 'jobs', params: { id: j.id } })">
            <template #empty><div class="sc-empty"><span class="sc-empty__title">No retained jobs</span><span>Succeeded jobs are purged after the retention window.</span></div></template>
          </JobTable>
        </div>
        <RouterLink class="sc-link" :to="{ name: 'jobs', query: { name: selected.name } }">All {{ selected.name }} jobs <Icon name="arrow" :size="12" /></RouterLink>

        <h3>Payload</h3>
        <PayloadView :value="selected.payload" />
      </div>
    </template>
    <div v-else-if="recurring.data.value" class="sc-empty">
      <Icon name="alert" />
      <span class="sc-empty__title">No schedule with id {{ props.id }}</span>
      <span>It may have been deleted.</span>
    </div>
    <div v-else class="sc-empty"><span class="sc-skeleton" /></div>
  </Drawer>
</template>
