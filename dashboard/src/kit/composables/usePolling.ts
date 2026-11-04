import { onScopeDispose, ref, shallowRef, type Ref, type ShallowRef } from "vue";

export interface PollingOptions {
  /** Milliseconds between refreshes. 0 disables polling (manual `refresh()` only). */
  intervalMs?: number;
  /** Fetch immediately on creation. Default true. */
  immediate?: boolean;
  /** Pause while the browser tab is hidden. Default true. */
  pauseWhenHidden?: boolean;
}

export interface Polling<T> {
  data: ShallowRef<T | undefined>;
  error: Ref<Error | undefined>;
  loading: Ref<boolean>;
  /** Fetch now. Concurrent calls are coalesced. */
  refresh: () => Promise<void>;
  pause: () => void;
  resume: () => void;
  paused: Ref<boolean>;
}

/**
 * Runs `fetcher` now and every `intervalMs`, keeping the last good result while a refresh is in flight.
 * Stops when the component scope is disposed. The server does not push dashboard updates; polling `/stats`
 * every few seconds is the intended pattern.
 */
export function usePolling<T>(fetcher: () => Promise<T>, options: PollingOptions = {}): Polling<T> {
  const intervalMs = options.intervalMs ?? 3000;
  const data = shallowRef<T>();
  const error = ref<Error>();
  const loading = ref(false);
  const paused = ref(false);
  let timer: ReturnType<typeof setTimeout> | undefined;
  let inFlight: Promise<void> | undefined;
  let disposed = false;

  const refresh = (): Promise<void> => {
    if (inFlight) return inFlight;
    loading.value = true;
    inFlight = fetcher()
      .then((result) => {
        data.value = result;
        error.value = undefined;
      })
      .catch((e: unknown) => {
        error.value = e instanceof Error ? e : new Error(String(e));
      })
      .finally(() => {
        loading.value = false;
        inFlight = undefined;
        schedule();
      });
    return inFlight;
  };

  const schedule = () => {
    clearTimeout(timer);
    if (disposed || paused.value || intervalMs <= 0) return;
    if (options.pauseWhenHidden !== false && typeof document !== "undefined" && document.hidden) return;
    timer = setTimeout(() => void refresh(), intervalMs);
  };

  const pause = () => {
    paused.value = true;
    clearTimeout(timer);
  };
  const resume = () => {
    if (!paused.value) return;
    paused.value = false;
    void refresh();
  };

  const onVisibility = () => {
    if (!document.hidden && !paused.value) void refresh();
  };
  if (typeof document !== "undefined" && options.pauseWhenHidden !== false) {
    document.addEventListener("visibilitychange", onVisibility);
  }

  onScopeDispose(() => {
    disposed = true;
    clearTimeout(timer);
    if (typeof document !== "undefined") document.removeEventListener("visibilitychange", onVisibility);
  });

  if (options.immediate !== false) void refresh();

  return { data, error, loading, refresh, pause, resume, paused };
}
