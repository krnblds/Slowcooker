import { computed, ref, watch, type Ref } from "vue";
import type { Job, JobState, Page, SlowcookerClient } from "../api";
import { useSlowcooker } from "./context";
import { usePolling, type PollingOptions } from "./usePolling";

export interface UseJobsOptions extends PollingOptions {
  client?: SlowcookerClient;
  state?: JobState | null;
  queue?: string | null;
  /** Exact job name. */
  name?: string | null;
  pageSize?: number;
}

/**
 * A filtered, paged, optionally polling job list. Change `state`, `queue`, `name` or `page` and it refetches.
 *
 *   const jobs = useJobs({ state: "failed", pageSize: 50, intervalMs: 5000 });
 *   jobs.queue.value = "payments";
 */
export function useJobs(options: UseJobsOptions = {}) {
  const api = useSlowcooker(options.client);
  const state: Ref<JobState | null> = ref(options.state ?? null);
  const queue: Ref<string | null> = ref(options.queue ?? null);
  const name: Ref<string | null> = ref(options.name ?? null);
  const pageSize = ref(options.pageSize ?? 50);
  const page = ref(0);

  const polling = usePolling<Page<Job>>(
    () => api.jobs({ state: state.value, queue: queue.value, name: name.value, skip: page.value * pageSize.value, take: pageSize.value }),
    { intervalMs: 0, ...options },
  );

  watch([state, queue, name, pageSize], () => {
    page.value = 0;
    void polling.refresh();
  });
  watch(page, () => void polling.refresh());

  const items = computed(() => polling.data.value?.items ?? []);
  const total = computed(() => polling.data.value?.total ?? 0);
  const pageCount = computed(() => Math.max(1, Math.ceil(total.value / pageSize.value)));

  return {
    ...polling,
    state,
    queue,
    name,
    pageSize,
    page,
    items,
    total,
    pageCount,
    next: () => {
      if (page.value + 1 < pageCount.value) page.value++;
    },
    prev: () => {
      if (page.value > 0) page.value--;
    },
    /** Requeue then refresh the list. */
    requeue: async (id: string) => {
      await api.requeue(id);
      await polling.refresh();
    },
    /** Delete then refresh the list. */
    remove: async (id: string) => {
      await api.deleteJob(id);
      await polling.refresh();
    },
  };
}
