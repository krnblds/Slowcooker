<script setup lang="ts">
import { onBeforeUnmount, onMounted } from "vue";
import Icon from "./Icon.vue";

defineProps<{ title?: string }>();
const emit = defineEmits<{ close: [] }>();

function onKey(e: KeyboardEvent) {
  if (e.key === "Escape") emit("close");
}
onMounted(() => document.addEventListener("keydown", onKey));
onBeforeUnmount(() => document.removeEventListener("keydown", onKey));
</script>

<template>
  <div class="drawer-backdrop" @click="emit('close')" />
  <aside class="drawer" role="dialog" aria-modal="false" :aria-label="title">
    <header v-if="title || $slots.header" class="drawer__header">
      <slot name="header"><h2 class="drawer__title">{{ title }}</h2></slot>
      <button type="button" class="sc-btn sc-btn--ghost sc-btn--icon" aria-label="Close" @click="emit('close')"><Icon name="close" /></button>
    </header>
    <div class="drawer__body"><slot /></div>
  </aside>
</template>
