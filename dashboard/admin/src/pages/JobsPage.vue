<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import { ErrorBanner, JOB_STATES, JobDetail, JobFilters, JobTable, Pagination, useJob, useJobs, type Job, type JobState } from "@slowcooker/vue";
import Drawer from "../components/Drawer.vue";
import Icon from "../components/Icon.vue";
import PageHeader from "../components/PageHeader.vue";
import { confirm } from "../confirm";
import { useSharedStats } from "../stats";
import { attempt, toast } from "../toasts";

const props = defineProps<{ id?: string }>();
const route = useRoute();
const router = useRouter();
const stats = useSharedStats();

function stateFromQuery(v: unknown): JobState | null {
  return typeof v === "string" && (JOB_STATES as readonly string[]).includes(v) ? (v as JobState) : null;
}
function stringFromQuery(v: unknown): string | null {
  return typeof v === "string" && v !== "" ? v : null;
}
function pageFromQuery(v: unknown): number {
  const n = Number(v);
  return Number.isInteger(n) && n > 0 ? n - 1 : 0;
}

const jobs = useJobs({
  state: stateFromQuery(route.query.state),
  queue: stringFromQuery(route.query.queue),
  pageSize: 25,
  intervalMs: 3000,
});

// Filters live in the URL so views are shareable; keep the two in sync without feedback loops.
watch(
  () => route.query,
  (q) => {
    const state = stateFromQuery(q.state);
    const queue = stringFromQuery(q.queue);
    const page = pageFromQuery(q.page);
    if (jobs.state.value !== state) jobs.state.value = state;
    if (jobs.queue.value !== queue) jobs.queue.value = queue;
    if (jobs.page.value !== page) jobs.page.value = page;
  },
);
watch([jobs.state, jobs.queue, jobs.page], ([state, queue, page]) => {
  const query: Record<string, string> = {};
  if (state) query.state = state;
  if (queue) query.queue = queue;
  if (page > 0) query.page = String(page + 1);
  const same = Object.keys(query).length === Object.keys(route.query).length && Object.entries(query).every(([k, v]) => route.query[k] === v);
  if (!same) void router.replace({ query });
});
// jobs.page is reset to 0 by useJobs when filters change; this applies a page from the URL after that reset.
if (route.query.page) jobs.page.value = pageFromQuery(route.query.page);

const selectedId = computed(() => props.id ?? null);
const selected = useJob(selectedId);

const search = ref("");
function openSearch() {
  const id = search.value.trim();
  if (!id) return;
  void router.push({ name: "jobs", params: { id }, query: route.query });
  search.value = "";
}
function open(job: Job) {
  void router.push({ name: "jobs", params: { id: job.id }, query: route.query });
}
function closeDetail() {
  void router.push({ name: "jobs", query: route.query });
}

async function requeue(job: Job) {
  if (await attempt(() => jobs.requeue(job.id), `Requeued ${job.displayName}`)) {
    if (selectedId.value === job.id) await selected.refresh();
  }
}
async function remove(job: Job) {
  const ok = await confirm({
    title: "Delete job?",
    message: `${job.displayName} (${job.id}) will be removed permanently. If it is scheduled it will not run.`,
    confirmLabel: "Delete",
    danger: true,
  });
  if (!ok) return;
  if (await attempt(() => jobs.remove(job.id), `Deleted ${job.displayName}`)) {
    if (selectedId.value === job.id) closeDetail();
  }
}
async function copyId(id: string) {
  try {
    await navigator.clipboard.writeText(id);
    toast("info", "Job id copied");
  } catch {
    toast("error", "Could not access the clipboard");
  }
}

const subtitle = computed(() => {
  const parts: string[] = [];
  if (jobs.state.value) parts.push(jobs.state.value);
  if (jobs.queue.value) parts.push(`queue ${jobs.queue.value}`);
  const scope = parts.length ? parts.join(", ") : "all states and queues";
  return jobs.data.value ? `${jobs.total.value} ${jobs.total.value === 1 ? "job" : "jobs"} · ${scope}` : scope;
});
</script>

<template>
  <PageHeader title="Jobs" :subtitle="subtitle">
    <form class="search" @submit.prevent="openSearch">
      <Icon name="search" class="search__icon" />
      <input v-model="search" class="sc-input sc-input--mono search__input" type="search" placeholder="Open job by id…" aria-label="Open job by id" />
    </form>
    <button type="button" class="sc-btn" :disabled="jobs.loading.value" @click="jobs.refresh()"><Icon name="refresh" /> Refresh</button>
  </PageHeader>

  <ErrorBanner :error="jobs.error.value" />

  <section class="sc-card">
    <div class="toolbar">
      <JobFilters v-model:state="jobs.state.value" v-model:queue="jobs.queue.value" :queues="stats.data.value?.queues" />
      <Pagination :page="jobs.page.value" :page-count="jobs.pageCount.value" :total="jobs.total.value" :page-size="jobs.pageSize.value" @change="(p) => (jobs.page.value = p)" />
    </div>
    <div class="sc-table-wrap">
    <JobTable :jobs="jobs.items.value" :now="stats.serverTime.value" :selected-id="selectedId" @select="open" @requeue="requeue" @delete="remove">
      <template #empty>
        <div class="sc-empty">
          <Icon name="inbox" />
          <span class="sc-empty__title">No jobs match</span>
          <span v-if="jobs.state.value || jobs.queue.value">Try clearing the filters.</span>
          <span v-else>Enqueue something with <code>IJobClient</code> and it will show up here.</span>
        </div>
      </template>
    </JobTable>
    </div>
  </section>

  <Drawer v-if="selectedId" title="Job" @close="closeDetail">
    <template #header>
      <div class="drawer__heading">
        <h2 class="drawer__title">Job</h2>
        <button type="button" class="sc-btn sc-btn--ghost sc-btn--sm id-chip" :title="`Copy ${selectedId}`" @click="copyId(selectedId!)">
          <code>{{ selectedId }}</code><Icon name="copy" :size="12" />
        </button>
      </div>
    </template>
    <ErrorBanner :error="selected.error.value" />
    <JobDetail v-if="selected.job.value" :job="selected.job.value" :now="stats.serverTime.value" :closable="false" @requeue="requeue" @delete="remove" />
    <div v-else-if="selected.loading.value" class="sc-empty"><span class="sc-skeleton" /></div>
  </Drawer>
</template>
