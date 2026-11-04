<script setup lang="ts">
import { computed } from "vue";

const props = defineProps<{ page: number; pageCount: number; total: number; pageSize: number }>();
const emit = defineEmits<{ change: [page: number] }>();

const from = computed(() => (props.total === 0 ? 0 : props.page * props.pageSize + 1));
const to = computed(() => Math.min(props.total, (props.page + 1) * props.pageSize));
</script>

<template>
  <nav class="sc-pagination" aria-label="Pagination">
    <span class="sc-muted">{{ from }}–{{ to }} of {{ total }}</span>
    <button type="button" class="sc-btn sc-btn--ghost" :disabled="page === 0" @click="emit('change', page - 1)">‹ Prev</button>
    <span>Page {{ page + 1 }} / {{ pageCount }}</span>
    <button type="button" class="sc-btn sc-btn--ghost" :disabled="page + 1 >= pageCount" @click="emit('change', page + 1)">Next ›</button>
  </nav>
</template>
