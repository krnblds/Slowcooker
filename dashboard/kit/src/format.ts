/** Small formatting helpers used by the kit components. Replace freely. */

import { ref } from "vue";

const displayTimeZone = ref<string | undefined>(undefined);

/**
 * Show absolute times in this IANA time zone ("UTC", "Europe/London"), or in the browser's zone when undefined.
 * Reactive: components that format times re-render when it changes.
 */
export function setDisplayTimeZone(timeZone: string | undefined): void {
  displayTimeZone.value = timeZone;
}

/** The zone set with `setDisplayTimeZone`, or undefined for the browser's own. */
export function getDisplayTimeZone(): string | undefined {
  return displayTimeZone.value;
}

export function formatRelative(iso: string | null | undefined, now: Date = new Date()): string {
  if (!iso) return "";
  const diffMs = new Date(iso).getTime() - now.getTime();
  const abs = Math.abs(diffMs);
  const units: [number, Intl.RelativeTimeFormatUnit][] = [
    [1000 * 60 * 60 * 24, "day"],
    [1000 * 60 * 60, "hour"],
    [1000 * 60, "minute"],
    [1000, "second"],
  ];
  const rtf = new Intl.RelativeTimeFormat(undefined, { numeric: "auto" });
  for (const [ms, unit] of units) {
    if (abs >= ms || unit === "second") return rtf.format(Math.round(diffMs / ms), unit);
  }
  return "";
}

export function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return "";
  const timeZone = displayTimeZone.value;
  const text = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "medium", timeZone }).format(new Date(iso));
  return timeZone === "UTC" ? `${text} UTC` : text;
}

/** "1.2 s", "3 min 4 s", "2 h 5 min". Negative or missing values give "". */
export function formatDuration(ms: number | null | undefined): string {
  if (ms === null || ms === undefined || !Number.isFinite(ms) || ms < 0) return "";
  if (ms < 1000) return `${Math.round(ms)} ms`;
  const s = ms / 1000;
  if (s < 60) return `${s < 10 ? s.toFixed(1) : Math.round(s)} s`;
  const m = Math.floor(s / 60);
  if (m < 60) return `${m} min ${Math.round(s % 60)} s`;
  const h = Math.floor(m / 60);
  if (h < 24) return `${h} h ${m % 60} min`;
  return `${Math.floor(h / 24)} d ${h % 24} h`;
}

export function formatJson(value: unknown): string {
  return typeof value === "string" ? value : JSON.stringify(value, null, 2);
}

/** First line of an exception string, e.g. "System.InvalidOperationException: gateway timeout". */
export function errorHeadline(error: string | null | undefined): string {
  return (error ?? "").split("\n")[0]?.trim() ?? "";
}

/** An exception string split into its first line, its message lines and its "at ..." stack frames. */
export function splitError(error: string | null | undefined): { headline: string; details: string[]; frames: string[] } {
  const lines = (error ?? "").split(/\r?\n/);
  const headline = lines.shift()?.trim() ?? "";
  const details: string[] = [];
  const frames: string[] = [];
  for (const raw of lines) {
    const line = raw.trim();
    if (!line) continue;
    if (line.startsWith("at ") || line.startsWith("--- End of")) frames.push(line);
    else details.push(line);
  }
  return { headline, details, frames };
}

const DAYS = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
const SHORT_DAYS = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

function int(field: string, min: number, max: number): number | null {
  if (!/^\d+$/.test(field)) return null;
  const n = Number(field);
  return n >= min && n <= max ? n : null;
}

function step(field: string): number | null {
  const m = /^\*\/(\d+)$/.exec(field);
  return m ? Number(m[1]) : null;
}

function ordinal(n: number): string {
  const s = n % 100 >= 11 && n % 100 <= 13 ? "th" : (["th", "st", "nd", "rd"][n % 10] ?? "th");
  return `${n}${s}`;
}

/**
 * A plain-English reading of common five-field cron expressions ("Every day at 06:00", "Every 15 minutes"), or null
 * when the pattern is not one it recognises. It describes; it does not validate. Use the API's cron preview for that.
 */
export function describeCron(expression: string | null | undefined): string | null {
  const fields = (expression ?? "").trim().split(/\s+/);
  if (fields.length !== 5) return null;
  const [min, hour, dom, month, dow] = fields as [string, string, string, string, string];
  if (month !== "*") return null;

  if (hour === "*" && dom === "*" && dow === "*") {
    if (min === "*") return "Every minute";
    const every = step(min);
    if (every) return every === 1 ? "Every minute" : `Every ${every} minutes`;
    const m = int(min, 0, 59);
    if (m !== null) return m === 0 ? "Every hour, on the hour" : `Every hour at :${String(m).padStart(2, "0")}`;
    return null;
  }

  const m = int(min, 0, 59);
  if (m === null) return null;
  const mm = String(m).padStart(2, "0");

  const hourStep = step(hour);
  if (hourStep && dom === "*" && dow === "*") return `Every ${hourStep} hours at :${mm}`;

  const hours = hour.split(",").map((h) => int(h, 0, 23));
  if (hours.some((h) => h === null)) return null;
  const at = `at ${(hours as number[]).map((h) => `${String(h).padStart(2, "0")}:${mm}`).join(" and ")}`;

  if (dom === "*" && dow === "*") return `Every day ${at}`;
  if (dom === "*") {
    if (dow === "1-5") return `Weekdays ${at}`;
    if (dow === "0,6" || dow === "6,0") return `Weekends ${at}`;
    const days = dow.split(",").map((d) => int(d, 0, 7));
    if (days.some((d) => d === null)) return null;
    if (days.length === 1) return `Every ${DAYS[(days[0] as number) % 7]} ${at}`;
    return `${(days as number[]).map((d) => SHORT_DAYS[d % 7]).join(", ")} ${at}`;
  }
  if (dow === "*") {
    const day = int(dom, 1, 31);
    if (day !== null) return `Monthly on the ${ordinal(day)} ${at}`;
  }
  return null;
}
