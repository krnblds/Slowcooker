import { describe, expect, it } from "vitest";
import { mount } from "@vue/test-utils";
import StateTabs from "./StateTabs.vue";

describe("StateTabs", () => {
  it("renders All plus one tab per state with counts and marks the active one", () => {
    const wrapper = mount(StateTabs, { props: { state: "failed", counts: { scheduled: 4, failed: 2 }, total: 9 } });
    const tabs = wrapper.findAll(".sc-tab");

    expect(tabs.map((t) => t.find(".sc-tab__label").exists() ? t.find(".sc-tab__label").text() : "All")).toEqual(["All", "scheduled", "processing", "succeeded", "failed"]);
    expect(tabs[0].find(".sc-tab__count").text()).toBe("9");
    expect(tabs[2].find(".sc-tab__count").exists()).toBe(false);
    expect(tabs[4].classes()).toContain("sc-tab--active");
    expect(tabs[4].attributes("aria-selected")).toBe("true");
    expect(tabs[4].find(".sc-tab__count").classes()).toContain("sc-tab__count--alert");
    expect(mount(StateTabs, { props: { counts: { failed: 0 } } }).find(".sc-tab--failed .sc-tab__count").classes()).not.toContain("sc-tab__count--alert");
  });

  it("updates v-model:state, with null for All", async () => {
    const wrapper = mount(StateTabs, { props: { state: null } });
    await wrapper.findAll(".sc-tab")[1].trigger("click");
    await wrapper.findAll(".sc-tab")[0].trigger("click");
    expect(wrapper.emitted("update:state")).toEqual([["scheduled"], [null]]);
  });
});
