<script setup lang="ts">
import { computed } from "vue";
import { splitError } from "../format";

const props = defineProps<{ error: string }>();
const parts = computed(() => splitError(props.error));
</script>

<template>
  <div class="sc-error-view">
    <p class="sc-error-view__headline">{{ parts.headline }}</p>
    <pre v-if="parts.details.length" class="sc-error"><code>{{ parts.details.join("\n") }}</code></pre>
    <details v-if="parts.frames.length" class="sc-error-view__stack">
      <summary>Stack trace <span class="sc-muted">{{ parts.frames.length }} {{ parts.frames.length === 1 ? "frame" : "frames" }}</span></summary>
      <pre class="sc-error"><code>{{ parts.frames.join("\n") }}</code></pre>
    </details>
  </div>
</template>
