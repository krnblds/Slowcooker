<script setup lang="ts">
import type { JobState } from "../api";

defineProps<{ state: JobState }>();
</script>

<template>
  <span class="sc-badge" :class="`sc-badge--${state}`">{{ state }}</span>
</template>
