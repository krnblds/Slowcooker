<script setup lang="ts">
import { formatJson } from "../format";

defineProps<{ value: unknown }>();
</script>

<template>
  <pre class="sc-payload"><code>{{ formatJson(value) }}</code></pre>
</template>
