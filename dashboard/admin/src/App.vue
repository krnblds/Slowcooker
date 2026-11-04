<script setup lang="ts">
import { computed } from "vue";
import { RouterLink, RouterView } from "vue-router";
import { provideSlowcooker, usePolling } from "@slowcooker/vue";
import { api } from "./client";
import { provideSharedStats } from "./stats";
import { useTheme } from "./theme";
import ConfirmDialog from "./components/ConfirmDialog.vue";
import Icon from "./components/Icon.vue";
import Toasts from "./components/Toasts.vue";

provideSlowcooker(api);
const stats = provideSharedStats(api);
const info = usePolling(() => api.info(), { intervalMs: 60_000 });
const { theme, cycle } = useTheme();

const themeIcon = computed(() => ({ system: "system", light: "sun", dark: "moon" })[theme.value]);
const themeLabel = computed(() => `Theme: ${theme.value}. Click to change`);
const connected = computed(() => !stats.error.value && stats.data.value !== undefined);
</script>

<template>
  <div class="app">
    <aside class="sidebar">
      <RouterLink to="/" class="brand">
        <span class="brand__mark"><Icon name="logo" :size="18" /></span>
        <span class="brand__name">Slowcooker</span>
      </RouterLink>

      <nav class="nav" aria-label="Main">
        <RouterLink to="/" class="nav__link" exact-active-class="nav__link--active"><Icon name="overview" /><span>Overview</span></RouterLink>
        <RouterLink to="/jobs" class="nav__link" active-class="nav__link--active">
          <Icon name="jobs" /><span>Jobs</span>
          <span v-if="stats.data.value?.failed" class="nav__count nav__count--failed">{{ stats.data.value.failed }}</span>
        </RouterLink>
        <RouterLink to="/recurring" class="nav__link" active-class="nav__link--active">
          <Icon name="recurring" /><span>Recurring</span>
          <span v-if="stats.data.value?.recurring" class="nav__count">{{ stats.data.value.recurring }}</span>
        </RouterLink>
      </nav>

      <div class="sidebar__footer">
        <div class="status" :class="connected ? 'status--ok' : 'status--bad'">
          <span class="status__dot" />
          <span v-if="connected">Connected</span>
          <span v-else-if="stats.error.value" :title="stats.error.value.message">Disconnected</span>
          <span v-else>Connecting…</span>
        </div>
        <div class="sidebar__meta">
          <span v-if="info.data.value" class="sc-truncate" :title="`${info.data.value.storage} · ${info.data.value.version}`">
            {{ info.data.value.storage.replace(/JobStorage$/, "") }} · v{{ info.data.value.version.split("+")[0] }}
          </span>
          <span v-else class="sc-skeleton" />
          <button type="button" class="sc-btn sc-btn--ghost sc-btn--icon sc-btn--sm theme-toggle" :title="themeLabel" :aria-label="themeLabel" @click="cycle">
            <Icon :name="themeIcon" />
          </button>
        </div>
      </div>
    </aside>

    <main class="main">
      <RouterView />
    </main>

    <Toasts />
    <ConfirmDialog />
  </div>
</template>
