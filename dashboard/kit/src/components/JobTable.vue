<script setup lang="ts">
import type { Job } from "../api";
import { errorHeadline } from "../format";
import RelativeTime from "./RelativeTime.vue";
import StateBadge from "./StateBadge.vue";

defineProps<{
  jobs: Job[];
  /** Server clock for relative times. */
  now?: string | null;
  selectedId?: string | null;
  /** Hide the action buttons (e.g. for read-only users). */
  readonly?: boolean;
}>();
const emit = defineEmits<{ select: [job: Job]; requeue: [job: Job]; delete: [job: Job] }>();
</script>

<template>
  <table class="sc-table sc-jobs">
    <thead>
      <tr>
        <th>Job</th>
        <th>Queue</th>
        <th>State</th>
        <th class="sc-num">Attempt</th>
        <th>Runs</th>
        <th>Created</th>
        <th v-if="!readonly" class="sc-actions"></th>
      </tr>
    </thead>
    <tbody>
      <tr
        v-for="job in jobs"
        :key="job.id"
        class="sc-row--clickable"
        :class="{ 'sc-row--selected': job.id === selectedId }"
        @click="emit('select', job)"
      >
        <td>
          <div class="sc-job__name">{{ job.displayName }}</div>
          <div v-if="job.lastError" class="sc-job__error" :title="job.lastError">{{ errorHeadline(job.lastError) }}</div>
        </td>
        <td><span class="sc-chip">{{ job.queue }}</span></td>
        <td><StateBadge :state="job.state" /></td>
        <td class="sc-num">{{ job.attempt }}</td>
        <td><RelativeTime :value="job.state === 'scheduled' ? job.runAt : job.completedAt ?? job.runAt" :now="now" /></td>
        <td><RelativeTime :value="job.createdAt" :now="now" /></td>
        <td v-if="!readonly" class="sc-actions" @click.stop>
          <slot name="actions" :job="job">
            <button type="button" class="sc-btn" :disabled="job.state === 'processing'" @click="emit('requeue', job)">Requeue</button>
            <button type="button" class="sc-btn sc-btn--danger" @click="emit('delete', job)">Delete</button>
          </slot>
        </td>
      </tr>
      <tr v-if="jobs.length === 0"><td colspan="7"><slot name="empty"><div class="sc-empty"><span class="sc-empty__title">No jobs match</span><span>Try a different state or queue.</span></div></slot></td></tr>
    </tbody>
  </table>
</template>
