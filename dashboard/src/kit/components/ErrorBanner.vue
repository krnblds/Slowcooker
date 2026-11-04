<script setup lang="ts">
defineProps<{ error: Error | undefined | null }>();
</script>

<template>
  <div v-if="error" class="sc-banner sc-banner--error" role="alert">{{ error.message }}</div>
</template>
