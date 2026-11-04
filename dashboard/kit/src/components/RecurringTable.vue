<script setup lang="ts">
import type { RecurringJob } from "../api";
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
        <td><code>{{ r.id }}</code></td>
        <td>{{ r.displayName }}</td>
        <td><code>{{ r.cron }}</code></td>
        <td>{{ r.timeZoneId }}</td>
        <td><code>{{ r.queue }}</code></td>
        <td><RelativeTime v-if="r.nextRunAt" :value="r.nextRunAt" :now="now" /><span v-else class="sc-badge sc-badge--parked">parked</span></td>
        <td><RelativeTime :value="r.lastRunAt" :now="now" /></td>
        <td v-if="!readonly" class="sc-actions" @click.stop>
          <slot name="actions" :job="r">
            <button type="button" class="sc-btn" @click="emit('trigger', r)">Run now</button>
            <button type="button" class="sc-btn sc-btn--danger" @click="emit('delete', r)">Delete</button>
          </slot>
        </td>
      </tr>
      <tr v-if="items.length === 0"><td colspan="8" class="sc-muted">No recurring jobs.</td></tr>
    </tbody>
  </table>
</template>
