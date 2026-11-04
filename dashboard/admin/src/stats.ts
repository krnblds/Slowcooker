import { computed, inject, provide, type ComputedRef, type InjectionKey } from "vue";
import { useStats, type SlowcookerClient } from "@slowcooker/vue";

type Shared = ReturnType<typeof useStats> & { serverTime: ComputedRef<string | null> };
const KEY: InjectionKey<Shared> = Symbol("stats");

/** One /stats poll for the whole app: the shell shows counts, pages use the same data and the server clock. */
export function provideSharedStats(client: SlowcookerClient): Shared {
  // The client is passed explicitly: this runs in the component that provides it, and provide() only reaches descendants.
  const stats = useStats({ client, intervalMs: 3000 });
  const shared = { ...stats, serverTime: computed(() => stats.data.value?.serverTime ?? null) };
  provide(KEY, shared);
  return shared;
}

export function useSharedStats(): Shared {
  const s = inject(KEY, null);
  if (!s) throw new Error("provideSharedStats() must be called in App.vue");
  return s;
}
