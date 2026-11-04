<script setup lang="ts">
import { computed } from "vue";
import { formatDateTime, formatRelative } from "../format";

const props = defineProps<{
  value: string | null | undefined;
  /** The server clock (from /stats or /info) so "in 3 min" does not depend on the browser's clock. */
  now?: string | null;
}>();

const text = computed(() => formatRelative(props.value, props.now ? new Date(props.now) : new Date()));
</script>

<template>
  <time v-if="value" :datetime="value" :title="formatDateTime(value)" class="sc-time">{{ text }}</time>
  <span v-else class="sc-muted">–</span>
</template>
