<script setup lang="ts">
import { JOB_STATES, type JobState } from "../api";

defineProps<{
  /** Count per state, e.g. from `/stats`. Missing counts are not shown. */
  counts?: Partial<Record<JobState, number>>;
  /** Count for the "All" tab. */
  total?: number;
}>();
const state = defineModel<JobState | null>("state", { default: null });
</script>

<template>
  <div class="sc-tabs" role="tablist" aria-label="Job state">
    <button type="button" role="tab" class="sc-tab" :class="{ 'sc-tab--active': state === null }" :aria-selected="state === null" @click="state = null">
      <span>All</span><span v-if="total !== undefined" class="sc-tab__count">{{ total }}</span>
    </button>
    <button
      v-for="s in JOB_STATES"
      :key="s"
      type="button"
      role="tab"
      class="sc-tab"
      :class="[`sc-tab--${s}`, { 'sc-tab--active': state === s }]"
      :aria-selected="state === s"
      @click="state = s"
    >
      <span class="sc-tab__label">{{ s }}</span><span v-if="counts?.[s] !== undefined" class="sc-tab__count" :class="{ 'sc-tab__count--alert': s === 'failed' && (counts[s] ?? 0) > 0 }">{{ counts[s] }}</span>
    </button>
  </div>
</template>
