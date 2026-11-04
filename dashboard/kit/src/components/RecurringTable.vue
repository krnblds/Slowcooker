<script setup lang="ts">
import type { RecurringJob } from "../api";
import { describeCron } from "../format";
import RelativeTime from "./RelativeTime.vue";

defineProps<{ items: RecurringJob[]; now?: string | null; readonly?: boolean }>();
const emit = defineEmits<{ select: [job: RecurringJob]; trigger: [job: RecurringJob]; delete: [job: RecurringJob] }>();
</script>

<template>
  <table class="sc-table sc-recurring">
    <thead>
      <tr><th>Id</th><th>Job</th><th>Schedule</th><th>Time zone</th><th>Queue</th><th>Next run</th><th>Last run</th><th v-if="!readonly" class="sc-actions"></th></tr>
    </thead>
    <tbody>
      <tr v-for="r in items" :key="r.id" class="sc-row--clickable" @click="emit('select', r)">
        <td><span class="sc-job__name">{{ r.id }}</span></td>
        <td>{{ r.displayName }}</td>
        <td><span class="sc-chip">{{ r.cron }}</span><div v-if="describeCron(r.cron)" class="sc-cron__desc">{{ describeCron(r.cron) }}</div></td>
        <td>{{ r.timeZoneId }}</td>
        <td><span class="sc-chip">{{ r.queue }}</span></td>
        <td><RelativeTime v-if="r.nextRunAt" :value="r.nextRunAt" :now="now" /><span v-else class="sc-badge sc-badge--parked">parked</span></td>
        <td><RelativeTime :value="r.lastRunAt" :now="now" /></td>
        <td v-if="!readonly" class="sc-actions" @click.stop>
          <slot name="actions" :job="r">
            <button type="button" class="sc-btn sc-btn--sm" @click="emit('trigger', r)">Run now</button>
            <button type="button" class="sc-btn sc-btn--sm sc-btn--danger" @click="emit('delete', r)">Delete</button>
          </slot>
        </td>
      </tr>
      <tr v-if="items.length === 0"><td colspan="8"><div class="sc-empty"><span class="sc-empty__title">No recurring jobs</span><span>Declare schedules in code with IRecurringJobClient.AddOrUpdateAsync.</span></div></td></tr>
    </tbody>
  </table>
</template>
