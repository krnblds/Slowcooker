<script setup lang="ts">
import { computed } from "vue";
import type { QueueStats } from "@slowcooker/vue";

// Part-to-whole for one queue. Segments are separated by a surface gap, each has a tooltip with its label and
// count, and the failed segment is hatched so it does not rely on red versus green alone.
const props = defineProps<{ queue: QueueStats }>();
const STATES = ["scheduled", "processing", "succeeded", "failed"] as const;
const total = computed(() => STATES.reduce((n, s) => n + props.queue[s], 0));
const segments = computed(() => STATES.filter((s) => props.queue[s] > 0).map((s) => ({ state: s, count: props.queue[s], share: props.queue[s] / Math.max(1, total.value) })));
</script>

<template>
  <div class="qbar" role="img" :aria-label="`${queue.name}: ${STATES.map((s) => `${queue[s]} ${s}`).join(', ')}`">
    <span v-for="seg in segments" :key="seg.state" class="qbar__seg" :class="`qbar__seg--${seg.state}`" :style="{ flexGrow: seg.share }" :title="`${seg.count} ${seg.state}`" />
    <span v-if="!segments.length" class="qbar__seg qbar__seg--empty" title="No retained jobs" />
  </div>
</template>
