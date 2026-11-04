<script setup lang="ts">
import type { Stats } from "../api";

defineProps<{ stats: Stats | undefined; loading?: boolean }>();
const emit = defineEmits<{ select: [state: "scheduled" | "processing" | "succeeded" | "failed"] }>();
</script>

<template>
  <div class="sc-stats" :class="{ 'sc-stats--loading': loading }">
    <button class="sc-stat sc-stat--scheduled" type="button" @click="emit('select', 'scheduled')">
      <span class="sc-stat__value">{{ stats?.scheduled ?? "–" }}</span>
      <span class="sc-stat__label">Scheduled</span>
    </button>
    <button class="sc-stat sc-stat--processing" type="button" @click="emit('select', 'processing')">
      <span class="sc-stat__value">{{ stats?.processing ?? "–" }}</span>
      <span class="sc-stat__label">Processing</span>
    </button>
    <button class="sc-stat sc-stat--succeeded" type="button" @click="emit('select', 'succeeded')">
      <span class="sc-stat__value">{{ stats?.succeeded ?? "–" }}</span>
      <span class="sc-stat__label">Succeeded</span>
    </button>
    <button class="sc-stat sc-stat--failed" type="button" @click="emit('select', 'failed')">
      <span class="sc-stat__value">{{ stats?.failed ?? "–" }}</span>
      <span class="sc-stat__label">Failed</span>
    </button>
    <div class="sc-stat sc-stat--recurring">
      <span class="sc-stat__value">{{ stats?.recurring ?? "–" }}</span>
      <span class="sc-stat__label">Recurring</span>
    </div>
    <slot />
  </div>
</template>
