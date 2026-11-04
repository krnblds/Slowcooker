import { reactive, watch } from "vue";
import { setDisplayTimeZone } from "@slowcooker/vue";

/** Per-browser display preferences. Stored in localStorage; nothing here is sent to the server. */
export interface Settings {
  /** Show absolute times in the browser's zone or in UTC. */
  timeZone: "local" | "utc";
  /** Poll for updates. Off freezes every list until Refresh or until it is turned back on. */
  live: boolean;
}

const KEY = "slowcooker:settings";

function load(): Settings {
  const defaults: Settings = { timeZone: "local", live: true };
  try {
    const raw = JSON.parse(localStorage.getItem(KEY) ?? "{}") as Partial<Settings>;
    return {
      timeZone: raw.timeZone === "utc" ? "utc" : "local",
      live: raw.live !== false,
    };
  } catch {
    return defaults;
  }
}

export const settings = reactive<Settings>(load());

watch(
  settings,
  (s) => {
    setDisplayTimeZone(s.timeZone === "utc" ? "UTC" : undefined);
    try {
      localStorage.setItem(KEY, JSON.stringify(s));
    } catch {
      /* private mode, blocked storage */
    }
  },
  { deep: true, immediate: true },
);
