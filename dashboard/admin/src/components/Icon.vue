<script setup lang="ts">
import { computed } from "vue";

const paths: Record<string, string> = {
  overview: '<rect x="3" y="3" width="7" height="9" rx="1.5"/><rect x="14" y="3" width="7" height="5" rx="1.5"/><rect x="14" y="12" width="7" height="9" rx="1.5"/><rect x="3" y="16" width="7" height="5" rx="1.5"/>',
  jobs: '<path d="M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01"/>',
  recurring: '<path d="M21 12a9 9 0 1 1-2.64-6.36"/><path d="M21 3v6h-6"/><path d="M12 7v5l3 2"/>',
  refresh: '<path d="M21 12a9 9 0 1 1-2.64-6.36"/><path d="M21 3v6h-6"/>',
  sun: '<circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.93 4.93l1.41 1.41M17.66 17.66l1.41 1.41M2 12h2M20 12h2M4.93 19.07l1.41-1.41M17.66 6.34l1.41-1.41"/>',
  moon: '<path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z"/>',
  system: '<rect x="2" y="3" width="20" height="14" rx="2"/><path d="M8 21h8M12 17v4"/>',
  close: '<path d="M18 6 6 18M6 6l12 12"/>',
  copy: '<rect x="9" y="9" width="13" height="13" rx="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/>',
  search: '<circle cx="11" cy="11" r="7"/><path d="m21 21-4.3-4.3"/>',
  play: '<path d="m6 4 14 8-14 8z"/>',
  trash: '<path d="M3 6h18M8 6V4h8v2M19 6l-1 14H6L5 6M10 11v6M14 11v6"/>',
  alert: '<circle cx="12" cy="12" r="9"/><path d="M12 8v4M12 16h.01"/>',
  check: '<path d="m5 12 5 5L20 7"/>',
  info: '<circle cx="12" cy="12" r="9"/><path d="M12 16v-4M12 8h.01"/>',
  arrow: '<path d="M5 12h14M13 6l6 6-6 6"/>',
  inbox: '<path d="M22 12h-6l-2 3h-4l-2-3H2"/><path d="M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z"/>',
  pot: '<path d="M3 10h18"/><path d="M5 10v5a5 5 0 0 0 5 5h4a5 5 0 0 0 5-5v-5"/><path d="M8 10V9a4 4 0 0 1 8 0v1"/><path d="M9 5c0-1.2.8-1.5.8-2.6M14.2 5c0-1.2.8-1.5.8-2.6"/>',
  logo: '<path d="M4 11h16"/><path d="M6 11v4.5A4.5 4.5 0 0 0 10.5 20h3a4.5 4.5 0 0 0 4.5-4.5V11"/><path d="M9 11V9.5a3 3 0 0 1 6 0V11"/>',
};

const props = defineProps<{ name: keyof typeof paths | string; size?: number | string }>();
const html = computed(() => paths[props.name] ?? "");
</script>

<template>
  <svg
    class="icon"
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    stroke-width="2"
    stroke-linecap="round"
    stroke-linejoin="round"
    :width="size ?? 16"
    :height="size ?? 16"
    aria-hidden="true"
    v-html="html"
  />
</template>
