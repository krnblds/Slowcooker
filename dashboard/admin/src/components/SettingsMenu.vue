<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from "vue";
import Icon from "./Icon.vue";
import { settings } from "../settings";
import { useTheme, type Theme } from "../theme";

const open = ref(false);
const root = ref<HTMLElement>();
const { theme, setTheme } = useTheme();
const themes: { value: Theme; label: string; icon: string }[] = [
  { value: "system", label: "System", icon: "system" },
  { value: "light", label: "Light", icon: "sun" },
  { value: "dark", label: "Dark", icon: "moon" },
];

function onDocClick(e: MouseEvent) {
  if (open.value && root.value && !root.value.contains(e.target as Node)) open.value = false;
}
function onKey(e: KeyboardEvent) {
  if (e.key === "Escape") open.value = false;
}
onMounted(() => {
  document.addEventListener("click", onDocClick);
  document.addEventListener("keydown", onKey);
});
onBeforeUnmount(() => {
  document.removeEventListener("click", onDocClick);
  document.removeEventListener("keydown", onKey);
});
</script>

<template>
  <div ref="root" class="menu">
    <button type="button" class="sc-btn sc-btn--ghost sc-btn--icon sc-btn--sm" aria-label="Display settings" title="Display settings" :aria-expanded="open" aria-haspopup="true" @click="open = !open">
      <Icon name="settings" />
    </button>
    <div v-if="open" class="menu__panel" role="group" aria-label="Display settings">
      <div class="menu__row">
        <span class="menu__label">Theme</span>
        <div class="segmented" role="radiogroup" aria-label="Theme">
          <button v-for="t in themes" :key="t.value" type="button" role="radio" class="segmented__btn" :class="{ 'segmented__btn--on': theme === t.value }" :aria-checked="theme === t.value" :title="t.label" @click="setTheme(t.value)">
            <Icon :name="t.icon" :size="14" /><span>{{ t.label }}</span>
          </button>
        </div>
      </div>
      <div class="menu__row">
        <span class="menu__label">Times</span>
        <div class="segmented" role="radiogroup" aria-label="Time zone">
          <button type="button" role="radio" class="segmented__btn" :class="{ 'segmented__btn--on': settings.timeZone === 'local' }" :aria-checked="settings.timeZone === 'local'" @click="settings.timeZone = 'local'">Local</button>
          <button type="button" role="radio" class="segmented__btn" :class="{ 'segmented__btn--on': settings.timeZone === 'utc' }" :aria-checked="settings.timeZone === 'utc'" @click="settings.timeZone = 'utc'">UTC</button>
        </div>
      </div>
      <label class="menu__row menu__row--toggle">
        <span class="menu__label">Live updates<small>Polls every few seconds while this tab is visible</small></span>
        <input v-model="settings.live" type="checkbox" class="switch" role="switch" />
      </label>
    </div>
  </div>
</template>
