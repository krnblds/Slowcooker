import { afterEach, describe, expect, it } from "vitest";
import { describeCron, formatDateTime, formatDuration, setDisplayTimeZone, splitError } from "./format";

describe("describeCron", () => {
  it.each([
    ["* * * * *", "Every minute"],
    ["*/15 * * * *", "Every 15 minutes"],
    ["0 * * * *", "Every hour, on the hour"],
    ["30 * * * *", "Every hour at :30"],
    ["5 */6 * * *", "Every 6 hours at :05"],
    ["0 6 * * *", "Every day at 06:00"],
    ["0 9,17 * * *", "Every day at 09:00 and 17:00"],
    ["0 9 * * 1-5", "Weekdays at 09:00"],
    ["0 10 * * 0,6", "Weekends at 10:00"],
    ["0 8 * * 1", "Every Monday at 08:00"],
    ["0 8 * * 7", "Every Sunday at 08:00"],
    ["15 7 * * 1,3,5", "Mon, Wed, Fri at 07:15"],
    ["0 0 1 * *", "Monthly on the 1st at 00:00"],
    ["0 0 22 * *", "Monthly on the 22nd at 00:00"],
  ])("reads %s as %s", (cron, text) => {
    expect(describeCron(cron)).toBe(text);
  });

  it.each(["", "0 0 1 1 *", "0 0 * * MON", "1-5 * * * *", "0 25 * * *", "0 0 0 * * *", "nonsense"])("returns null for %j", (cron) => {
    expect(describeCron(cron)).toBeNull();
  });
});

describe("splitError", () => {
  it("separates the headline, message lines and stack frames", () => {
    const text = "System.Exception: outer\n ---> System.IO.IOException: inner\n   at A.B()\n   at C.D()\n   --- End of inner exception stack trace ---\n";
    expect(splitError(text)).toEqual({
      headline: "System.Exception: outer",
      details: ["---> System.IO.IOException: inner"],
      frames: ["at A.B()", "at C.D()", "--- End of inner exception stack trace ---"],
    });
    expect(splitError(null)).toEqual({ headline: "", details: [], frames: [] });
  });
});

describe("formatDuration", () => {
  it.each([
    [450, "450 ms"],
    [1500, "1.5 s"],
    [42_000, "42 s"],
    [125_000, "2 min 5 s"],
    [7_500_000, "2 h 5 min"],
    [90_000_000, "1 d 1 h"],
  ])("formats %d ms", (ms, text) => expect(formatDuration(ms)).toBe(text));

  it("is empty for missing or negative values", () => {
    expect(formatDuration(null)).toBe("");
    expect(formatDuration(-1)).toBe("");
  });
});

describe("display time zone", () => {
  afterEach(() => setDisplayTimeZone(undefined));

  it("formats in the chosen zone and marks UTC", () => {
    setDisplayTimeZone("UTC");
    const text = formatDateTime("2026-06-01T23:30:00+00:00");
    expect(text).toMatch(/23:30|11:30/);
    expect(text.endsWith(" UTC")).toBe(true);

    setDisplayTimeZone("Asia/Tokyo");
    expect(formatDateTime("2026-06-01T23:30:00+00:00")).toMatch(/0?8:30/);
    expect(formatDateTime("2026-06-01T23:30:00+00:00")).toMatch(/Jun.*2|2.*Jun/);
  });
});
