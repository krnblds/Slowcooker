import type { RecurringJob, SlowcookerClient } from "../api";
import { useSlowcooker } from "./context";
import { usePolling, type PollingOptions } from "./usePolling";

/** The recurring job list with trigger/remove actions. Polls every 10 s by default. */
export function useRecurring(options: PollingOptions & { client?: SlowcookerClient } = {}) {
  const api = useSlowcooker(options.client);
  const polling = usePolling<RecurringJob[]>(() => api.recurring(), { intervalMs: 10_000, ...options });
  return {
    ...polling,
    trigger: async (id: string) => {
      const result = await api.trigger(id);
      await polling.refresh();
      return result.jobId;
    },
    remove: async (id: string) => {
      await api.deleteRecurring(id);
      await polling.refresh();
    },
  };
}
