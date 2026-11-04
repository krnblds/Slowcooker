<script setup lang="ts">
import type { QueueStats } from "../api";

defineProps<{ queues: QueueStats[] | undefined }>();
const emit = defineEmits<{ select: [queue: string] }>();
</script>

<template>
  <table class="sc-table sc-queues">
    <thead>
      <tr><th>Queue</th><th class="sc-num">Scheduled</th><th class="sc-num">Processing</th><th class="sc-num">Succeeded</th><th class="sc-num">Failed</th></tr>
    </thead>
    <tbody>
      <tr v-for="q in queues ?? []" :key="q.name" class="sc-row--clickable" @click="emit('select', q.name)">
        <td><code>{{ q.name }}</code></td>
        <td class="sc-num">{{ q.scheduled }}</td>
        <td class="sc-num">{{ q.processing }}</td>
        <td class="sc-num">{{ q.succeeded }}</td>
        <td class="sc-num" :class="{ 'sc-text--failed': q.failed > 0 }">{{ q.failed }}</td>
      </tr>
      <tr v-if="!queues?.length"><td colspan="5" class="sc-muted">No queues yet.</td></tr>
    </tbody>
  </table>
</template>
