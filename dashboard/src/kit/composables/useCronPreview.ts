import { ref, watch, type Ref } from "vue";
import type { CronPreviewResult, SlowcookerClient } from "../api";
import { useSlowcooker } from "./context";

/** Debounced `/cron/preview` for a schedule editor: validity, error text and upcoming runs. */
export function useCronPreview(expression: Ref<string>, timeZone: Ref<string | undefined>, options: { count?: number; debounceMs?: number; client?: SlowcookerClient } = {}) {
  const api = useSlowcooker(options.client);
  const preview = ref<CronPreviewResult>();
  const error = ref<Error>();
  const loading = ref(false);
  let timer: ReturnType<typeof setTimeout> | undefined;
  let latest = 0;

  const run = async () => {
    const expr = expression.value.trim();
    if (!expr) {
      preview.value = undefined;
      return;
    }
    const ticket = ++latest;
    loading.value = true;
    try {
      const result = await api.cronPreview(expr, timeZone.value, options.count ?? 5);
      if (ticket === latest) {
        preview.value = result;
        error.value = undefined;
      }
    } catch (e) {
      if (ticket === latest) error.value = e instanceof Error ? e : new Error(String(e));
    } finally {
      if (ticket === latest) loading.value = false;
    }
  };

  watch(
    [expression, timeZone],
    () => {
      clearTimeout(timer);
      timer = setTimeout(() => void run(), options.debounceMs ?? 300);
    },
    { immediate: true },
  );

  return { preview, error, loading, refresh: run };
}
