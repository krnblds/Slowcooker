import { computed, inject, provide, shallowRef, watch, type ComputedRef, type InjectionKey, type ShallowRef } from "vue";
import { useStats, type SlowcookerClient, type Stats } from "@slowcooker/vue";
import { followLive } from "./live";

/** One /stats sample, kept for the overview's sparklines. */
export interface Sample {
  at: number;
  scheduled: number;
  processing: number;
  succeeded: number;
  failed: number;
}

type Shared = ReturnType<typeof useStats> & {
  serverTime: ComputedRef<string | null>;
  /** Samples since this page was opened, oldest first. Not persisted; the server keeps no history. */
  history: ShallowRef<Sample[]>;
};
const KEY: InjectionKey<Shared> = Symbol("stats");
const MAX_SAMPLES = 120;

/** One /stats poll for the whole app: the shell shows counts, pages use the same data and the server clock. */
export function provideSharedStats(client: SlowcookerClient): Shared {
  // The client is passed explicitly: this runs in the component that provides it, and provide() only reaches descendants.
  const stats = useStats({ client, intervalMs: 3000 });
  followLive(stats);

  const history = shallowRef<Sample[]>([]);
  watch(stats.data, (s: Stats | undefined) => {
    if (!s) return;
    const sample = { at: new Date(s.serverTime).getTime(), scheduled: s.scheduled, processing: s.processing, succeeded: s.succeeded, failed: s.failed };
    history.value = [...history.value, sample].slice(-MAX_SAMPLES);
  });

  const shared = { ...stats, serverTime: computed(() => stats.data.value?.serverTime ?? null), history };
  provide(KEY, shared);
  return shared;
}

export function useSharedStats(): Shared {
  const s = inject(KEY, null);
  if (!s) throw new Error("provideSharedStats() must be called in App.vue");
  return s;
}
