<script setup lang="ts">
import { computed, watchEffect } from "vue";
import { RouterLink, RouterView } from "vue-router";
import { provideSlowcooker, usePolling } from "@slowcooker/vue";
import { api } from "./client";
import { paletteOpen } from "./palette";
import { settings } from "./settings";
import { provideSharedStats } from "./stats";
import CommandPalette from "./components/CommandPalette.vue";
import ConfirmDialog from "./components/ConfirmDialog.vue";
import Icon from "./components/Icon.vue";
import SettingsMenu from "./components/SettingsMenu.vue";
import Toasts from "./components/Toasts.vue";

provideSlowcooker(api);
const stats = provideSharedStats(api);
const info = usePolling(() => api.info(), { intervalMs: 60_000 });

const status = computed(() => {
  if (stats.error.value) return { tone: "bad", text: "Disconnected", title: stats.error.value.message };
  if (!stats.data.value) return { tone: "idle", text: "Connecting…", title: "" };
  if (!settings.live) return { tone: "paused", text: "Paused", title: "Live updates are off. Use Refresh or turn them back on in display settings." };
  return { tone: "ok", text: "Live", title: "Polling every 3 seconds while this tab is visible" };
});
const storage = computed(() => info.data.value?.storage.replace(/JobStorage$/, "") ?? null);
const version = computed(() => info.data.value?.version.split("+")[0] ?? null);
const isMac = typeof navigator !== "undefined" && /Mac|iPhone|iPad/.test(navigator.platform);

// The failed count in the tab title, so a pinned tab shows trouble without being opened.
watchEffect(() => {
  const failed = stats.data.value?.failed ?? 0;
  document.title = failed > 0 ? `(${failed} failed) Slowcooker` : "Slowcooker";
});
</script>

<template>
  <div class="app">
    <a class="skip-link" href="#main">Skip to content</a>
    <aside class="sidebar">
      <RouterLink to="/" class="brand" aria-label="Slowcooker overview">
        <span class="brand__mark"><Icon name="logo" :size="18" /></span>
        <span class="brand__text">
          <span class="brand__name">Slowcooker</span>
          <span v-if="storage" class="brand__env">{{ storage }} · v{{ version }}</span>
          <span v-else class="brand__env"><span class="sc-skeleton" /></span>
        </span>
      </RouterLink>

      <button type="button" class="quick-search" @click="paletteOpen = true">
        <Icon name="search" />
        <span>Search</span>
        <kbd class="sc-kbd">{{ isMac ? "⌘K" : "Ctrl K" }}</kbd>
      </button>

      <nav class="nav" aria-label="Main">
        <span class="nav__heading">Monitor</span>
        <RouterLink to="/" class="nav__link" exact-active-class="nav__link--active"><Icon name="overview" /><span>Overview</span></RouterLink>
        <RouterLink to="/jobs" class="nav__link" active-class="nav__link--active">
          <Icon name="jobs" /><span>Jobs</span>
          <span v-if="stats.data.value?.failed" class="nav__count nav__count--failed" :title="`${stats.data.value.failed} failed`">{{ stats.data.value.failed }}</span>
        </RouterLink>
        <RouterLink to="/recurring" class="nav__link" active-class="nav__link--active">
          <Icon name="recurring" /><span>Recurring</span>
          <span v-if="stats.data.value?.recurring" class="nav__count">{{ stats.data.value.recurring }}</span>
        </RouterLink>
        <span class="nav__heading">Tools</span>
        <RouterLink to="/cron" class="nav__link" active-class="nav__link--active"><Icon name="cron" /><span>Cron tester</span></RouterLink>
      </nav>

      <div class="sidebar__footer">
        <div class="status" :class="`status--${status.tone}`" :title="status.title" role="status">
          <span class="status__dot" />
          <span class="status__text">{{ status.text }}</span>
        </div>
        <SettingsMenu />
      </div>
    </aside>

    <main id="main" class="main" tabindex="-1">
      <div v-if="stats.error.value" class="offline" role="alert">
        <Icon name="alert" />
        <span><strong>Cannot reach the Slowcooker API.</strong> {{ stats.error.value.message }} Retrying every few seconds.</span>
        <button type="button" class="sc-btn sc-btn--sm" @click="stats.refresh()">Retry now</button>
      </div>
      <RouterView />
    </main>

    <CommandPalette />
    <Toasts />
    <ConfirmDialog />
  </div>
</template>
