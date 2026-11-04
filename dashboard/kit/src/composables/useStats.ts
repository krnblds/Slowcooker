import type { SlowcookerClient } from "../api";
import { useSlowcooker } from "./context";
import { usePolling, type Polling, type PollingOptions } from "./usePolling";
import type { Stats } from "../api";

/** `/stats` on a timer (default every 3 s). */
export function useStats(options: PollingOptions & { client?: SlowcookerClient } = {}): Polling<Stats> {
  const api = useSlowcooker(options.client);
  return usePolling(() => api.stats(), { intervalMs: 3000, ...options });
}
