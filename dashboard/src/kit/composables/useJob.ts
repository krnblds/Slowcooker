import { ref, watch, type Ref } from "vue";
import type { Job, SlowcookerClient } from "../api";
import { useSlowcooker } from "./context";

/** One job by id, with requeue/remove actions. Pass a ref to follow a selection. */
export function useJob(id: Ref<string | null | undefined>, client?: SlowcookerClient) {
  const api = useSlowcooker(client);
  const job = ref<Job>();
  const error = ref<Error>();
  const loading = ref(false);

  const refresh = async () => {
    if (!id.value) {
      job.value = undefined;
      return;
    }
    loading.value = true;
    try {
      job.value = await api.job(id.value);
      error.value = undefined;
    } catch (e) {
      error.value = e instanceof Error ? e : new Error(String(e));
    } finally {
      loading.value = false;
    }
  };

  watch(id, () => void refresh(), { immediate: true });

  return {
    job,
    error,
    loading,
    refresh,
    requeue: async () => {
      if (!id.value) return;
      job.value = await api.requeue(id.value);
    },
    remove: async () => {
      if (!id.value) return;
      await api.deleteJob(id.value);
      job.value = undefined;
    },
  };
}
