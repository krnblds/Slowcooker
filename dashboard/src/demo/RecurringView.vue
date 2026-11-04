<script setup lang="ts">
import { ref } from "vue";
import { CronPreview, ErrorBanner, RecurringTable, useRecurring, type RecurringJob } from "@kit";

defineProps<{ now: string | null }>();

const recurring = useRecurring({ intervalMs: 5000 });
const actionError = ref<Error>();
const lastTriggered = ref<string>();

// A little cron scratchpad to show the preview endpoint. Creating recurring jobs is done in code, not from the dashboard.
const expression = ref("0 6 * * *");
const timeZone = ref(Intl.DateTimeFormat().resolvedOptions().timeZone);

async function trigger(job: RecurringJob) {
  try {
    lastTriggered.value = await recurring.trigger(job.id);
    actionError.value = undefined;
  } catch (e) {
    actionError.value = e instanceof Error ? e : new Error(String(e));
  }
}
async function remove(job: RecurringJob) {
  if (!confirm(`Delete recurring job ${job.id}? Jobs it already created are kept.`)) return;
  try {
    await recurring.remove(job.id);
  } catch (e) {
    actionError.value = e instanceof Error ? e : new Error(String(e));
  }
}
</script>

<template>
  <section>
    <ErrorBanner :error="recurring.error.value ?? actionError" />
    <p v-if="lastTriggered" class="sc-muted">Triggered job <code>{{ lastTriggered }}</code>.</p>
    <RecurringTable :items="recurring.data.value ?? []" :now="now" @trigger="trigger" @delete="remove" />

    <h2 style="margin-top: 1.5rem">Cron scratchpad</h2>
    <form class="sc-filters" @submit.prevent>
      <label class="sc-field"><span>Expression</span><input v-model="expression" type="text" size="24" /></label>
      <label class="sc-field"><span>Time zone</span><input v-model="timeZone" type="text" size="24" /></label>
    </form>
    <CronPreview :expression="expression" :time-zone="timeZone" :count="5" />
  </section>
</template>
