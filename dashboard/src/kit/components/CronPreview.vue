<script setup lang="ts">
import { toRef } from "vue";
import { useCronPreview } from "../composables/useCronPreview";
import { formatDateTime } from "../format";

const props = defineProps<{ expression: string; timeZone?: string; count?: number }>();
const { preview, error, loading } = useCronPreview(toRef(props, "expression"), toRef(props, "timeZone"), { count: props.count });
</script>

<template>
  <div class="sc-cron" :class="{ 'sc-cron--invalid': preview && !preview.valid }">
    <p v-if="error" class="sc-text--failed">{{ error.message }}</p>
    <p v-else-if="preview && !preview.valid" class="sc-text--failed">{{ preview.error }}</p>
    <ol v-else-if="preview" class="sc-cron__list">
      <li v-for="t in preview.upcoming" :key="t">{{ formatDateTime(t) }}</li>
      <li v-if="preview.upcoming.length === 0" class="sc-muted">Never fires.</li>
    </ol>
    <p v-else-if="loading" class="sc-muted">…</p>
  </div>
</template>
