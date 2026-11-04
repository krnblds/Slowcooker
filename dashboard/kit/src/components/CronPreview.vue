<script setup lang="ts">
import { toRef } from "vue";
import { useCronPreview } from "../composables/useCronPreview";
import { computed } from "vue";
import { describeCron, formatDateTime } from "../format";

const props = defineProps<{ expression: string; timeZone?: string; count?: number }>();
const description = computed(() => describeCron(props.expression));
const { preview, error, loading } = useCronPreview(toRef(props, "expression"), toRef(props, "timeZone"), { count: props.count });
</script>

<template>
  <div class="sc-cron" :class="{ 'sc-cron--invalid': preview && !preview.valid }">
    <p v-if="description && preview?.valid" class="sc-cron__summary">{{ description }}<template v-if="preview.timeZoneId"> · {{ preview.timeZoneId }}</template></p>
    <p v-if="error" class="sc-text--failed">{{ error.message }}</p>
    <p v-else-if="preview && !preview.valid" class="sc-text--failed">{{ preview.error }}</p>
    <ol v-else-if="preview" class="sc-cron__list">
      <li v-for="t in preview.upcoming" :key="t">{{ formatDateTime(t) }}</li>
      <li v-if="preview.upcoming.length === 0" class="sc-muted">Never fires.</li>
    </ol>
    <p v-else-if="loading" class="sc-muted"><span class="sc-skeleton"></span></p>
  </div>
</template>
