import { inject, provide, type InjectionKey } from "vue";
import type { SlowcookerClient } from "../api";

const KEY: InjectionKey<SlowcookerClient> = Symbol("slowcooker-client");

/** Call once near the root of your app so every kit component and composable can find the API client. */
export function provideSlowcooker(client: SlowcookerClient): void {
  provide(KEY, client);
}

/** The client provided by `provideSlowcooker`, or the one passed in explicitly. */
export function useSlowcooker(client?: SlowcookerClient): SlowcookerClient {
  const resolved = client ?? inject(KEY, null);
  if (!resolved) throw new Error("No Slowcooker client: call provideSlowcooker(createClient(...)) in a parent component, or pass a client.");
  return resolved;
}
