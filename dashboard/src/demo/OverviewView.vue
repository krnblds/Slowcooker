<script setup lang="ts">
import { QueueTable, type Stats } from "@kit";

defineProps<{ stats: Stats | undefined }>();
const emit = defineEmits<{ selectQueue: [queue: string] }>();
</script>

<template>
  <section>
    <h2>Queues</h2>
    <QueueTable :queues="stats?.queues" @select="(q) => emit('selectQueue', q)" />
  </section>
</template>
