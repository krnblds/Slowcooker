import { describe, expect, it } from "vitest";
import { mount } from "@vue/test-utils";
import JobTimeline from "./JobTimeline.vue";
import ErrorView from "./ErrorView.vue";
import type { Job } from "../api";

const NOW = "2026-06-01T08:10:00+00:00";
function job(overrides: Partial<Job>): Job {
  return {
    id: "01", name: "charge-card", displayName: "charge-card", payload: {}, queue: "payments", state: "scheduled",
    createdAt: "2026-06-01T08:00:00+00:00", runAt: "2026-06-01T08:00:00+00:00", attempt: 0, lastError: null,
    leaseOwner: null, leaseExpiresAt: null, completedAt: null, idempotencyKey: null, ...overrides,
  };
}
const titles = (j: Job) => mount(JobTimeline, { props: { job: j, now: NOW } }).findAll(".sc-timeline__title").map((t) => t.text());

describe("JobTimeline", () => {
  it("shows a due job as waiting for a worker", () => {
    expect(titles(job({}))).toEqual(["Enqueued", "Waiting for a worker"]);
  });

  it("shows a job waiting to retry with the failed attempt and the next one", () => {
    const j = job({ attempt: 2, lastError: "System.TimeoutException: slow\n   at X.Y()", runAt: "2026-06-01T08:20:00+00:00" });
    const wrapper = mount(JobTimeline, { props: { job: j, now: NOW } });
    expect(wrapper.findAll(".sc-timeline__title").map((t) => t.text())).toEqual(["Enqueued", "Attempt 2 failed", "Retry scheduled"]);
    expect(wrapper.text()).toContain("System.TimeoutException: slow");
    expect(wrapper.text()).not.toContain("at X.Y()");
    expect(wrapper.text()).toContain("will be attempt 3");
  });

  it("summarises earlier attempts and marks the final state as current", () => {
    const j = job({ state: "succeeded", attempt: 3, completedAt: "2026-06-01T08:05:00+00:00" });
    const wrapper = mount(JobTimeline, { props: { job: j, now: NOW } });
    expect(wrapper.findAll(".sc-timeline__title").map((t) => t.text())).toEqual(["Enqueued", "2 earlier attempts failed", "Succeeded on attempt 3"]);
    expect(wrapper.find(".sc-timeline__step--current").classes()).toContain("sc-timeline__step--succeeded");
  });

  it("flags a processing job whose lease has lapsed", () => {
    const j = job({ state: "processing", attempt: 1, leaseOwner: "web-1:abc", leaseExpiresAt: "2026-06-01T08:09:00+00:00" });
    expect(titles(j)).toEqual(["Enqueued", "Running attempt 1", "Lease expired; another worker will take it over"]);
  });
});

describe("ErrorView", () => {
  it("shows the headline and folds the stack trace", () => {
    const wrapper = mount(ErrorView, { props: { error: "System.Exception: boom\n   at A.B()\n   at C.D()" } });
    expect(wrapper.find(".sc-error-view__headline").text()).toBe("System.Exception: boom");
    expect(wrapper.find("details summary").text()).toContain("2 frames");
    expect(wrapper.find("details").attributes("open")).toBeUndefined();
  });
});
