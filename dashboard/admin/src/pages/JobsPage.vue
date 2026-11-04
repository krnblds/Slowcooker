<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from "vue";
import { RouterLink, useRoute, useRouter } from "vue-router";
import { ErrorBanner, JOB_STATES, JobDetail, JobTable, Pagination, StateTabs, useJob, useJobs, type Job, type JobState } from "@slowcooker/vue";
import Drawer from "../components/Drawer.vue";
import Icon from "../components/Icon.vue";
import PageHeader from "../components/PageHeader.vue";
import { api } from "../client";
import { confirm } from "../confirm";
import { copy } from "../copy";
import { followLive } from "../live";
import { useSharedStats } from "../stats";
import { attempt, toast } from "../toasts";

const props = defineProps<{ id?: string }>();
const route = useRoute();
const router = useRouter();
const stats = useSharedStats();
const PAGE_SIZES = [25, 50, 100];

function stateFromQuery(v: unknown): JobState | null {
  return typeof v === "string" && (JOB_STATES as readonly string[]).includes(v) ? (v as JobState) : null;
}
function stringFromQuery(v: unknown): string | null {
  return typeof v === "string" && v.trim() !== "" ? v.trim() : null;
}
function pageFromQuery(v: unknown): number {
  const n = Number(v);
  return Number.isInteger(n) && n > 0 ? n - 1 : 0;
}
function sizeFromQuery(v: unknown): number {
  const n = Number(v);
  return PAGE_SIZES.includes(n) ? n : 25;
}

const jobs = useJobs({
  state: stateFromQuery(route.query.state),
  queue: stringFromQuery(route.query.queue),
  name: stringFromQuery(route.query.name),
  pageSize: sizeFromQuery(route.query.size),
  intervalMs: 3000,
});
followLive(jobs);

// Filters live in the URL so a view is a link you can share; keep the two in sync without feedback loops.
watch(
  () => route.query,
  (q) => {
    if (route.name !== "jobs") return;
    const state = stateFromQuery(q.state);
    const queue = stringFromQuery(q.queue);
    const name = stringFromQuery(q.name);
    const size = sizeFromQuery(q.size);
    if (jobs.state.value !== state) jobs.state.value = state;
    if (jobs.queue.value !== queue) jobs.queue.value = queue;
    if (jobs.name.value !== name) jobs.name.value = name;
    if (jobs.pageSize.value !== size) jobs.pageSize.value = size;
    const page = pageFromQuery(q.page);
    if (jobs.page.value !== page) jobs.page.value = page;
  },
);
watch([jobs.state, jobs.queue, jobs.name, jobs.page, jobs.pageSize], ([state, queue, name, page, size]) => {
  const query: Record<string, string> = {};
  if (state) query.state = state;
  if (queue) query.queue = queue;
  if (name) query.name = name;
  if (page > 0) query.page = String(page + 1);
  if (size !== 25) query.size = String(size);
  const same = Object.keys(query).length === Object.keys(route.query).length && Object.entries(query).every(([k, v]) => route.query[k] === v);
  if (!same) void router.replace({ params: route.params, query });
});
// useJobs resets the page to 0 when a filter changes; this applies a page from the URL after that reset.
if (route.query.page) jobs.page.value = pageFromQuery(route.query.page);

// Counts on the tabs come from /stats, scoped to the queue filter. With a job-name filter they are unknown.
const tabCounts = computed(() => {
  const s = stats.data.value;
  if (!s || jobs.name.value) return undefined;
  const scope = jobs.queue.value ? s.queues.find((q) => q.name === jobs.queue.value) : s;
  return scope ? { scheduled: scope.scheduled, processing: scope.processing, succeeded: scope.succeeded, failed: scope.failed } : undefined;
});
const tabTotal = computed(() => (tabCounts.value ? Object.values(tabCounts.value).reduce((a, b) => a + b, 0) : undefined));

// Job names offered by the filter: everything seen on screen plus the recurring templates.
const knownNames = ref(new Set<string>());
watch(jobs.items, (items) => {
  const next = new Set(knownNames.value);
  for (const j of items) next.add(j.name);
  if (next.size !== knownNames.value.size) knownNames.value = next;
});
api.recurring().then((r) => {
  const next = new Set(knownNames.value);
  r.forEach((x) => next.add(x.name));
  knownNames.value = next;
}, () => {});
const nameInput = ref(jobs.name.value ?? "");
watch(jobs.name, (n) => (nameInput.value = n ?? ""));
function applyName() {
  jobs.name.value = nameInput.value.trim() || null;
}

const filtered = computed(() => !!(jobs.state.value || jobs.queue.value || jobs.name.value));
function clearFilters() {
  void router.replace({ name: "jobs", params: route.params, query: {} });
}

// ----- Selection and bulk actions -----

const selection = ref<string[]>([]);
watch([jobs.state, jobs.queue, jobs.name], () => (selection.value = []));
const selectedJobs = computed(() => jobs.items.value.filter((j) => selection.value.includes(j.id)));
const requeueable = computed(() => selectedJobs.value.filter((j) => j.state !== "processing"));
const busy = ref(false);

async function runBulk(ids: string[], action: (id: string) => Promise<unknown>) {
  busy.value = true;
  let ok = 0;
  const failures: string[] = [];
  try {
    // A few at a time: fast enough for a page of jobs, gentle on the storage.
    for (let i = 0; i < ids.length; i += 5) {
      const batch = ids.slice(i, i + 5);
      const results = await Promise.allSettled(batch.map(action));
      results.forEach((r) => (r.status === "fulfilled" ? ok++ : failures.push(r.reason instanceof Error ? r.reason.message : String(r.reason))));
    }
  } finally {
    busy.value = false;
    selection.value = [];
    await jobs.refresh();
    void stats.refresh();
  }
  return { ok, failures };
}

async function bulkRequeue() {
  const targets = requeueable.value;
  const skipped = selectedJobs.value.length - targets.length;
  const ok = await confirm({
    title: `Requeue ${targets.length} ${targets.length === 1 ? "job" : "jobs"}?`,
    message: `They will run again now with their attempt counters reset.${skipped ? ` ${skipped} processing ${skipped === 1 ? "job is" : "jobs are"} skipped.` : ""}`,
    confirmLabel: "Requeue",
  });
  if (!ok) return;
  const r = await runBulk(targets.map((j) => j.id), (id) => api.requeue(id));
  if (r.failures.length) toast("error", `Requeued ${r.ok}; ${r.failures.length} failed: ${r.failures[0]}`, 8000);
  else toast("success", `Requeued ${r.ok} ${r.ok === 1 ? "job" : "jobs"}`);
}
async function bulkDelete() {
  const targets = selectedJobs.value;
  const ok = await confirm({
    title: `Delete ${targets.length} ${targets.length === 1 ? "job" : "jobs"}?`,
    message: "They will be removed permanently. Scheduled ones will not run. A job that is processing finishes, but its result is discarded.",
    confirmLabel: "Delete",
    danger: true,
  });
  if (!ok) return;
  const r = await runBulk(targets.map((j) => j.id), (id) => api.deleteJob(id));
  if (r.failures.length) toast("error", `Deleted ${r.ok}; ${r.failures.length} failed: ${r.failures[0]}`, 8000);
  else toast("success", `Deleted ${r.ok} ${r.ok === 1 ? "job" : "jobs"}`);
  if (props.id && targets.some((j) => j.id === props.id)) closeDetail();
}

// ----- Detail drawer -----

const selectedId = computed(() => props.id ?? null);
const selected = useJob(selectedId);
// Keep an open job fresh while it is changing state.
let detailTimer: ReturnType<typeof setInterval> | undefined;
onMounted(() => {
  detailTimer = setInterval(() => {
    const s = selected.job.value?.state;
    if (selectedId.value && (s === "processing" || s === "scheduled") && !document.hidden) void selected.refresh();
  }, 3000);
});
onBeforeUnmount(() => clearInterval(detailTimer));

function open(job: Job) {
  void router.push({ name: "jobs", params: { id: job.id }, query: route.query });
}
function closeDetail() {
  void router.push({ name: "jobs", query: route.query });
}
const position = computed(() => jobs.items.value.findIndex((j) => j.id === selectedId.value));
function step(delta: number) {
  const next = jobs.items.value[position.value + delta];
  if (next) void router.replace({ name: "jobs", params: { id: next.id }, query: route.query });
}
function onKey(e: KeyboardEvent) {
  const t = e.target as HTMLElement | null;
  if (!selectedId.value || e.metaKey || e.ctrlKey || e.altKey || (t && ["INPUT", "TEXTAREA", "SELECT"].includes(t.tagName))) return;
  if (e.key === "j") step(1);
  else if (e.key === "k") step(-1);
}
onMounted(() => document.addEventListener("keydown", onKey));
onBeforeUnmount(() => document.removeEventListener("keydown", onKey));

async function requeue(job: Job) {
  if (await attempt(() => jobs.requeue(job.id), `Requeued ${job.displayName}`)) {
    if (selectedId.value === job.id) await selected.refresh();
    void stats.refresh();
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
    void stats.refresh();
  }
}
function permalink(id: string) {
  return new URL(router.resolve({ name: "jobs", params: { id } }).href, location.origin).href;
}

const subtitle = computed(() => {
  const parts: string[] = [];
  if (jobs.state.value) parts.push(jobs.state.value);
  if (jobs.name.value) parts.push(jobs.name.value);
  if (jobs.queue.value) parts.push(`queue ${jobs.queue.value}`);
  const scope = parts.length ? parts.join(" · ") : "all states and queues";
  return jobs.data.value ? `${jobs.total.value.toLocaleString()} ${jobs.total.value === 1 ? "job" : "jobs"} · ${scope}` : scope;
});
</script>

<template>
  <PageHeader title="Jobs" :subtitle="subtitle">
    <button type="button" class="sc-btn" :disabled="jobs.loading.value" @click="jobs.refresh()"><Icon name="refresh" /> Refresh</button>
  </PageHeader>

  <ErrorBanner :error="jobs.error.value" />

  <section class="sc-card">
    <div class="toolbar">
      <StateTabs v-model:state="jobs.state.value" :counts="tabCounts" :total="tabTotal" />
      <div class="toolbar__filters">
        <label class="sc-field inline-field">
          <span>Queue</span>
          <select v-model="jobs.queue.value">
            <option :value="null">Any</option>
            <option v-for="q in stats.data.value?.queues ?? []" :key="q.name" :value="q.name">{{ q.name }}</option>
            <option v-if="jobs.queue.value && !stats.data.value?.queues.some((q) => q.name === jobs.queue.value)" :value="jobs.queue.value">{{ jobs.queue.value }}</option>
          </select>
        </label>
        <form class="sc-field inline-field" @submit.prevent="applyName">
          <label for="job-name">Job</label>
          <input id="job-name" v-model="nameInput" class="sc-input sc-input--mono name-input" type="search" list="job-names" placeholder="any name" spellcheck="false" @change="applyName" />
          <datalist id="job-names"><option v-for="n in [...knownNames].sort()" :key="n" :value="n" /></datalist>
        </form>
        <button v-if="filtered" type="button" class="sc-btn sc-btn--ghost sc-btn--sm" @click="clearFilters"><Icon name="close" :size="12" /> Clear filters</button>
      </div>
    </div>

    <div v-if="selection.length" class="bulkbar" role="region" aria-label="Bulk actions">
      <span class="bulkbar__count"><strong>{{ selection.length }}</strong> selected</span>
      <button type="button" class="sc-btn sc-btn--sm" :disabled="busy || !requeueable.length" @click="bulkRequeue"><Icon name="refresh" :size="12" /> Requeue</button>
      <button type="button" class="sc-btn sc-btn--sm sc-btn--danger" :disabled="busy" @click="bulkDelete"><Icon name="trash" :size="12" /> Delete</button>
      <button type="button" class="sc-btn sc-btn--sm sc-btn--ghost" :disabled="busy" @click="selection = []">Clear selection</button>
    </div>

    <div class="sc-table-wrap">
      <JobTable v-model:selection="selection" :jobs="jobs.items.value" :now="stats.serverTime.value" :selected-id="selectedId" selectable @select="open" @requeue="requeue" @delete="remove">
        <template #actions="{ job }">
          <span class="row-actions">
            <button type="button" class="sc-btn sc-btn--ghost sc-btn--icon sc-btn--sm" :disabled="job.state === 'processing'" :title="job.state === 'processing' ? 'Processing jobs cannot be requeued' : 'Requeue: run again now'" :aria-label="`Requeue ${job.displayName} ${job.id}`" @click="requeue(job)"><Icon name="refresh" :size="14" /></button>
            <button type="button" class="sc-btn sc-btn--ghost sc-btn--icon sc-btn--sm row-actions__delete" title="Delete" :aria-label="`Delete ${job.displayName} ${job.id}`" @click="remove(job)"><Icon name="trash" :size="14" /></button>
          </span>
        </template>
        <template #empty>
          <div class="sc-empty">
            <Icon name="inbox" />
            <template v-if="!jobs.data.value"><span class="sc-skeleton" /></template>
            <template v-else-if="filtered">
              <span class="sc-empty__title">No jobs match these filters</span>
              <button type="button" class="sc-btn sc-btn--sm" @click="clearFilters">Clear filters</button>
            </template>
            <template v-else>
              <span class="sc-empty__title">No jobs yet</span>
              <span>Enqueue something with <code>IJobClient</code> and it will show up here.</span>
            </template>
          </div>
        </template>
      </JobTable>
    </div>

    <footer class="sc-card__footer">
      <label class="sc-field inline-field">
        <span>Rows</span>
        <select v-model.number="jobs.pageSize.value"><option v-for="n in PAGE_SIZES" :key="n" :value="n">{{ n }}</option></select>
      </label>
      <Pagination :page="jobs.page.value" :page-count="jobs.pageCount.value" :total="jobs.total.value" :page-size="jobs.pageSize.value" @change="(p) => (jobs.page.value = p)" />
    </footer>
  </section>

  <Drawer v-if="selectedId" title="Job" @close="closeDetail">
    <template #header>
      <div class="drawer__heading">
        <h2 class="drawer__title">Job</h2>
        <button type="button" class="sc-btn sc-btn--ghost sc-btn--sm id-chip" :title="`Copy ${selectedId}`" @click="copy(selectedId!, 'Job id copied')">
          <code>{{ selectedId }}</code><Icon name="copy" :size="12" />
        </button>
      </div>
      <div class="drawer__tools">
        <span v-if="position >= 0" class="drawer__nav" aria-label="Move between jobs on this page">
          <button type="button" class="sc-btn sc-btn--ghost sc-btn--icon sc-btn--sm" title="Previous job (k)" :disabled="position <= 0" @click="step(-1)"><Icon name="chevron" class="flip" :size="14" /></button>
          <button type="button" class="sc-btn sc-btn--ghost sc-btn--icon sc-btn--sm" title="Next job (j)" :disabled="position >= jobs.items.value.length - 1" @click="step(1)"><Icon name="chevron" :size="14" /></button>
        </span>
        <button type="button" class="sc-btn sc-btn--ghost sc-btn--icon sc-btn--sm" title="Copy link to this job" aria-label="Copy link to this job" @click="copy(permalink(selectedId!), 'Link copied')"><Icon name="link" :size="14" /></button>
      </div>
    </template>
    <ErrorBanner :error="selected.error.value" />
    <JobDetail v-if="selected.job.value" :job="selected.job.value" :now="stats.serverTime.value" :closable="false" @requeue="requeue" @delete="remove">
      <h3>Related</h3>
      <div class="related">
        <RouterLink class="sc-btn sc-btn--sm" :to="{ name: 'jobs', query: { name: selected.job.value.name } }"><Icon name="filter" :size="12" /> All {{ selected.job.value.name }} jobs</RouterLink>
        <RouterLink v-if="selected.job.value.lastError" class="sc-btn sc-btn--sm" :to="{ name: 'jobs', query: { name: selected.job.value.name, state: 'failed' } }"><Icon name="alert" :size="12" /> Failed {{ selected.job.value.name }} jobs</RouterLink>
        <button type="button" class="sc-btn sc-btn--sm" @click="copy(JSON.stringify(selected.job.value.payload, null, 2), 'Payload copied')"><Icon name="copy" :size="12" /> Copy payload</button>
        <button v-if="selected.job.value.lastError" type="button" class="sc-btn sc-btn--sm" @click="copy(selected.job.value.lastError, 'Error copied')"><Icon name="copy" :size="12" /> Copy error</button>
      </div>
    </JobDetail>
    <div v-else-if="selected.loading.value" class="sc-empty"><span class="sc-skeleton" /></div>
  </Drawer>
</template>
