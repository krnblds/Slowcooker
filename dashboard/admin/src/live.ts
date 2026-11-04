import { watch } from "vue";
import type { Polling } from "@slowcooker/vue";
import { settings } from "./settings";

/** Pauses a poll while live updates are switched off, and resumes (with an immediate refresh) when they come back. */
export function followLive<T>(polling: Pick<Polling<T>, "pause" | "resume">): void {
  watch(
    () => settings.live,
    (live) => (live ? polling.resume() : polling.pause()),
    { immediate: true },
  );
}
