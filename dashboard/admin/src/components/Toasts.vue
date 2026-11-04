<script setup lang="ts">
import Icon from "./Icon.vue";
import { useToasts } from "../toasts";

const { toasts, dismiss } = useToasts();
const icon = { success: "check", error: "alert", info: "info" } as const;
</script>

<template>
  <div class="toasts" aria-live="polite">
    <TransitionGroup name="toast">
      <div v-for="t in toasts" :key="t.id" class="toast" :class="`toast--${t.kind}`" @click="dismiss(t.id)">
        <Icon :name="icon[t.kind]" />
        <span>{{ t.text }}</span>
      </div>
    </TransitionGroup>
  </div>
</template>
