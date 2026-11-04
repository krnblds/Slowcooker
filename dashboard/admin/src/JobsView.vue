<script setup lang="ts">
import { ref } from "vue";
import { ErrorBanner, JobDetail, JobFilters, JobTable, Pagination, useJob, useJobs, useStats, type Job, type JobState } from "@slowcooker/vue";

defineProps<{ now: string | null }>();

const jobs = useJobs({ pageSize: 25, intervalMs: 3000 });
const stats = useStats({ intervalMs: 10_000 });
const selectedId = ref<string | null>(null);
const selected = useJob(selectedId);
const actionError = ref<Error>();

async function guard(action: () => Promise<unknown>) {
  try {
    actionError.value = undefined;
    await action();
  } catch (e) {
    actionError.value = e instanceof Error ? e : new Error(String(e));
  }
}

function requeue(job: Job) {
  void guard(async () => {
    await jobs.requeue(job.id);
    if (selectedId.value === job.id) await selected.refresh();
  });
}
function remove(job: Job) {
  if (!confirm(`Delete job ${job.displayName} (${job.id})?`)) return;
  void guard(async () => {
    await jobs.remove(job.id);
    if (selectedId.value === job.id) selectedId.value = null;
  });
}

defineExpose({
  filterByState: (state: JobState) => {
    jobs.state.value = state;
    jobs.queue.value = null;
  },
  filterByQueue: (queue: string) => {
    jobs.queue.value = queue;
    jobs.state.value = null;
  },
});
</script>

<template>
  <section>
    <div class="demo__toolbar">
      <JobFilters v-model:state="jobs.state.value" v-model:queue="jobs.queue.value" :queues="stats.data.value?.queues" />
      <Pagination :page="jobs.page.value" :page-count="jobs.pageCount.value" :total="jobs.total.value" :page-size="jobs.pageSize.value" @change="(p) => (jobs.page.value = p)" />
    </div>
    <ErrorBanner :error="jobs.error.value ?? actionError" />
    <div class="demo__split">
      <JobTable :jobs="jobs.items.value" :now="now" :selected-id="selectedId" @select="(j) => (selectedId = j.id)" @requeue="requeue" @delete="remove" />
      <JobDetail v-if="selected.job.value" :job="selected.job.value" :now="now" @requeue="requeue" @delete="remove" @close="selectedId = null" />
    </div>
  </section>
</template>
