<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, shallowRef, watch } from "vue";
import { useRouter, type RouteLocationRaw } from "vue-router";
import type { RecurringJob } from "@slowcooker/vue";
import Icon from "./Icon.vue";
import { api } from "../client";
import { paletteOpen } from "../palette";
import { settings } from "../settings";
import { useSharedStats } from "../stats";
import { useTheme } from "../theme";

interface Item {
  id: string;
  group: string;
  label: string;
  hint?: string;
  icon: string;
  keywords?: string;
  run: () => void;
}

const router = useRouter();
const stats = useSharedStats();
const { theme, setTheme } = useTheme();
const dialog = ref<HTMLDialogElement>();
const input = ref<HTMLInputElement>();
const query = ref("");
const active = ref(0);
const schedules = shallowRef<RecurringJob[]>([]);

function go(to: RouteLocationRaw) {
  return () => void router.push(to);
}

const items = computed<Item[]>(() => {
  const q = query.value.trim();
  const list: Item[] = [
    { id: "nav-overview", group: "Go to", label: "Overview", icon: "overview", run: go("/") },
    { id: "nav-jobs", group: "Go to", label: "All jobs", icon: "jobs", run: go("/jobs") },
    { id: "nav-failed", group: "Go to", label: "Failed jobs", hint: String(stats.data.value?.failed ?? ""), icon: "alert", keywords: "errors dead letter", run: go({ name: "jobs", query: { state: "failed" } }) },
    { id: "nav-processing", group: "Go to", label: "Processing jobs", hint: String(stats.data.value?.processing ?? ""), icon: "live", keywords: "running", run: go({ name: "jobs", query: { state: "processing" } }) },
    { id: "nav-scheduled", group: "Go to", label: "Scheduled jobs", hint: String(stats.data.value?.scheduled ?? ""), icon: "cron", keywords: "queued waiting retry", run: go({ name: "jobs", query: { state: "scheduled" } }) },
    { id: "nav-succeeded", group: "Go to", label: "Succeeded jobs", icon: "check", keywords: "done completed", run: go({ name: "jobs", query: { state: "succeeded" } }) },
    { id: "nav-recurring", group: "Go to", label: "Recurring schedules", icon: "recurring", keywords: "cron", run: go("/recurring") },
    { id: "nav-cron", group: "Go to", label: "Cron tester", icon: "cron", keywords: "expression preview", run: go("/cron") },
    ...(stats.data.value?.queues ?? []).map((qs) => ({ id: `queue-${qs.name}`, group: "Queues", label: `Jobs in ${qs.name}`, hint: `${qs.scheduled + qs.processing} pending`, icon: "layers", keywords: "queue", run: go({ name: "jobs", query: { queue: qs.name } }) })),
    ...schedules.value.map((r) => ({ id: `rec-${r.id}`, group: "Schedules", label: r.id, hint: r.cron, icon: "recurring", keywords: r.name, run: go({ name: "recurring", params: { id: r.id } }) })),
    { id: "set-theme-light", group: "Preferences", label: "Light theme", hint: theme.value === "light" ? "current" : "", icon: "sun", keywords: "appearance", run: () => setTheme("light") },
    { id: "set-theme-dark", group: "Preferences", label: "Dark theme", hint: theme.value === "dark" ? "current" : "", icon: "moon", keywords: "appearance", run: () => setTheme("dark") },
    { id: "set-theme-system", group: "Preferences", label: "System theme", hint: theme.value === "system" ? "current" : "", icon: "system", keywords: "appearance auto", run: () => setTheme("system") },
    { id: "set-tz", group: "Preferences", label: settings.timeZone === "utc" ? "Show times in local time" : "Show times in UTC", icon: "globe", keywords: "time zone utc local", run: () => (settings.timeZone = settings.timeZone === "utc" ? "local" : "utc") },
    { id: "set-live", group: "Preferences", label: settings.live ? "Pause live updates" : "Resume live updates", icon: settings.live ? "pause" : "live", keywords: "polling refresh freeze", run: () => (settings.live = !settings.live) },
  ];
  const needle = q.toLowerCase();
  const filtered = needle ? list.filter((i) => `${i.label} ${i.group} ${i.keywords ?? ""} ${i.hint ?? ""}`.toLowerCase().includes(needle)) : list;
  if (/^[\w-]{6,}$/.test(q)) {
    filtered.unshift({ id: "open-job", group: "Job", label: `Open job ${q}`, icon: "search", run: go({ name: "jobs", params: { id: q } }) });
  }
  return filtered;
});

const groups = computed(() => {
  const out: { name: string; items: (Item & { index: number })[] }[] = [];
  items.value.forEach((item, index) => {
    let g = out.find((x) => x.name === item.group);
    if (!g) out.push((g = { name: item.group, items: [] }));
    g.items.push({ ...item, index });
  });
  return out;
});

watch(query, () => (active.value = 0));
watch(paletteOpen, async (open) => {
  if (open) {
    query.value = "";
    active.value = 0;
    dialog.value?.showModal();
    await nextTick();
    input.value?.focus();
    api.recurring().then((r) => (schedules.value = r), () => {});
  } else {
    dialog.value?.close();
  }
});

function choose(item: Item | undefined) {
  if (!item) return;
  paletteOpen.value = false;
  item.run();
}
function move(delta: number) {
  const n = items.value.length;
  if (!n) return;
  active.value = (active.value + delta + n) % n;
  void nextTick(() => document.getElementById(`cmd-${items.value[active.value]?.id}`)?.scrollIntoView({ block: "nearest" }));
}

function onGlobalKey(e: KeyboardEvent) {
  const target = e.target as HTMLElement | null;
  const typing = !!target && (target.isContentEditable || ["INPUT", "TEXTAREA", "SELECT"].includes(target.tagName));
  if ((e.key === "k" || e.key === "K") && (e.metaKey || e.ctrlKey)) {
    e.preventDefault();
    paletteOpen.value = !paletteOpen.value;
  } else if (e.key === "/" && !typing && !paletteOpen.value) {
    e.preventDefault();
    paletteOpen.value = true;
  }
}
onMounted(() => document.addEventListener("keydown", onGlobalKey));
onBeforeUnmount(() => document.removeEventListener("keydown", onGlobalKey));
</script>

<template>
  <dialog ref="dialog" class="palette" aria-label="Command palette" @cancel.prevent="paletteOpen = false" @click.self="paletteOpen = false">
    <div class="palette__panel">
      <div class="palette__search">
        <Icon name="search" />
        <input
          ref="input"
          v-model="query"
          class="palette__input"
          type="text"
          placeholder="Go to a page, queue or schedule, or paste a job id…"
          aria-label="Command"
          role="combobox"
          aria-expanded="true"
          aria-controls="palette-list"
          :aria-activedescendant="items[active] ? `cmd-${items[active].id}` : undefined"
          spellcheck="false"
          autocomplete="off"
          @keydown.down.prevent="move(1)"
          @keydown.up.prevent="move(-1)"
          @keydown.enter.prevent="choose(items[active])"
        />
        <kbd class="sc-kbd">Esc</kbd>
      </div>
      <div id="palette-list" class="palette__list" role="listbox">
        <template v-for="g in groups" :key="g.name">
          <div class="palette__group" role="presentation">{{ g.name }}</div>
          <button
            v-for="item in g.items"
            :id="`cmd-${item.id}`"
            :key="item.id"
            type="button"
            role="option"
            class="palette__item"
            :class="{ 'palette__item--active': item.index === active }"
            :aria-selected="item.index === active"
            tabindex="-1"
            @mousemove="active = item.index"
            @click="choose(item)"
          >
            <Icon :name="item.icon" />
            <span class="palette__label">{{ item.label }}</span>
            <span v-if="item.hint" class="palette__hint">{{ item.hint }}</span>
            <Icon v-if="item.index === active" name="enter" class="palette__enter" />
          </button>
        </template>
        <div v-if="!items.length" class="sc-empty"><span class="sc-empty__title">Nothing matches</span><span>Job ids need at least six characters.</span></div>
      </div>
      <div class="palette__footer">
        <span><kbd class="sc-kbd">↑</kbd><kbd class="sc-kbd">↓</kbd> move</span>
        <span><kbd class="sc-kbd">Enter</kbd> open</span>
        <span><kbd class="sc-kbd">/</kbd> or <kbd class="sc-kbd">Ctrl K</kbd> anywhere</span>
      </div>
    </div>
  </dialog>
</template>
