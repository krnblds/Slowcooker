import { describe, expect, it } from "vitest";
import { mount } from "@vue/test-utils";
import { h } from "vue";
import JobTable from "./JobTable.vue";
import type { Job } from "../api";

function job(overrides: Partial<Job> = {}): Job {
  return {
    id: "01",
    name: "send-welcome-email",
    displayName: "SendWelcomeEmail",
    payload: { email: "ada@example.com" },
    queue: "email",
    state: "scheduled",
    createdAt: "2026-06-01T08:00:00+00:00",
    runAt: "2026-06-01T08:05:00+00:00",
    attempt: 0,
    lastError: null,
    leaseOwner: null,
    leaseExpiresAt: null,
    completedAt: null,
    idempotencyKey: null,
    ...overrides,
  };
}

describe("JobTable", () => {
  it("renders a row per job with the first line of the last error", () => {
    const jobs = [job(), job({ id: "02", state: "failed", attempt: 3, lastError: "System.Exception: boom\n   at Handler.Run()" })];
    const wrapper = mount(JobTable, { props: { jobs, now: "2026-06-01T08:00:00+00:00" } });

    const rows = wrapper.findAll("tbody tr");
    expect(rows).toHaveLength(2);
    expect(rows[1].text()).toContain("System.Exception: boom");
    expect(rows[1].text()).not.toContain("at Handler.Run()");
    expect(rows[1].find(".sc-badge").text()).toBe("failed");
  });

  it("emits select on row click and marks the selected row", async () => {
    const jobs = [job(), job({ id: "02" })];
    const wrapper = mount(JobTable, { props: { jobs, selectedId: "02" } });

    await wrapper.findAll("tbody tr")[0].trigger("click");

    expect(wrapper.emitted("select")?.[0]).toEqual([jobs[0]]);
    expect(wrapper.findAll("tbody tr")[1].classes()).toContain("sc-row--selected");
  });

  it("emits requeue and delete from the action buttons without selecting the row", async () => {
    const jobs = [job({ state: "failed" })];
    const wrapper = mount(JobTable, { props: { jobs } });
    const [requeue, remove] = wrapper.findAll("tbody button");

    await requeue.trigger("click");
    await remove.trigger("click");

    expect(wrapper.emitted("requeue")?.[0]).toEqual([jobs[0]]);
    expect(wrapper.emitted("delete")?.[0]).toEqual([jobs[0]]);
    expect(wrapper.emitted("select")).toBeUndefined();
  });

  it("disables requeue while a job is processing", () => {
    const wrapper = mount(JobTable, { props: { jobs: [job({ state: "processing" })] } });
    expect(wrapper.findAll("tbody button")[0].attributes("disabled")).toBeDefined();
  });

  it("hides actions when readonly and lets the actions slot replace them", () => {
    expect(mount(JobTable, { props: { jobs: [job()], readonly: true } }).findAll("tbody button")).toHaveLength(0);

    const custom = mount(JobTable, {
      props: { jobs: [job()] },
      slots: { actions: ({ job }: { job: Job }) => h("a", { href: `/jobs/${job.id}` }, "open") },
    });
    expect(custom.find("tbody a").attributes("href")).toBe("/jobs/01");
    expect(custom.findAll("tbody button")).toHaveLength(0);
  });

  it("shows the empty slot when there are no jobs", () => {
    const wrapper = mount(JobTable, { props: { jobs: [] }, slots: { empty: "Nothing queued" } });
    expect(wrapper.text()).toContain("Nothing queued");
  });
});
