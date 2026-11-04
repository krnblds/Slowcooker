<script setup lang="ts">
import { computed } from "vue";
import type { Job } from "../api";
import { errorHeadline } from "../format";
import RelativeTime from "./RelativeTime.vue";
import StateBadge from "./StateBadge.vue";

const props = defineProps<{
  jobs: Job[];
  /** Server clock for relative times. */
  now?: string | null;
  selectedId?: string | null;
  /** Hide the action buttons (e.g. for read-only users). */
  readonly?: boolean;
  /** Show a checkbox per row, bound with `v-model:selection`. */
  selectable?: boolean;
}>();
const emit = defineEmits<{ select: [job: Job]; requeue: [job: Job]; delete: [job: Job] }>();
const selection = defineModel<string[]>("selection", { default: () => [] });

const columns = computed(() => 6 + (props.readonly ? 0 : 1) + (props.selectable ? 1 : 0));
const allChecked = computed(() => props.jobs.length > 0 && props.jobs.every((j) => selection.value.includes(j.id)));
const someChecked = computed(() => !allChecked.value && props.jobs.some((j) => selection.value.includes(j.id)));

function toggle(id: string, on: boolean) {
  selection.value = on ? [...new Set([...selection.value, id])] : selection.value.filter((x) => x !== id);
}
function toggleAll(on: boolean) {
  const ids = props.jobs.map((j) => j.id);
  selection.value = on ? [...new Set([...selection.value, ...ids])] : selection.value.filter((x) => !ids.includes(x));
}
function due(job: Job) {
  const now = props.now ? new Date(props.now).getTime() : Date.now();
  return new Date(job.runAt).getTime() <= now;
}
</script>

<template>
  <table class="sc-table sc-jobs">
    <thead>
      <tr>
        <th v-if="selectable" class="sc-check">
          <input type="checkbox" class="sc-checkbox" aria-label="Select all on this page" :checked="allChecked" :indeterminate="someChecked" @change="toggleAll(($event.target as HTMLInputElement).checked)" />
        </th>
        <th>Job</th>
        <th>Queue</th>
        <th>State</th>
        <th class="sc-num">Attempt</th>
        <th>Runs</th>
        <th>Created</th>
        <th v-if="!readonly" class="sc-actions"><span class="sc-visually-hidden">Actions</span></th>
      </tr>
    </thead>
    <tbody>
      <tr
        v-for="job in jobs"
        :key="job.id"
        class="sc-row--clickable"
        :class="{ 'sc-row--selected': job.id === selectedId, 'sc-row--checked': selection.includes(job.id) }"
        @click="emit('select', job)"
      >
        <td v-if="selectable" class="sc-check" @click.stop>
          <input type="checkbox" class="sc-checkbox" :aria-label="`Select ${job.displayName} ${job.id}`" :checked="selection.includes(job.id)" @change="toggle(job.id, ($event.target as HTMLInputElement).checked)" />
        </td>
        <td>
          <div class="sc-job__name">{{ job.displayName }}</div>
          <div v-if="job.lastError" class="sc-job__error" :title="job.lastError">{{ errorHeadline(job.lastError) }}</div>
        </td>
        <td><span class="sc-chip">{{ job.queue }}</span></td>
        <td class="sc-state-cell">
          <StateBadge :state="job.state" />
          <span v-if="job.state === 'scheduled' && job.attempt > 0 && job.lastError" class="sc-hint" title="Waiting to retry after a failed attempt">retry</span>
          <span v-else-if="job.state === 'scheduled' && due(job)" class="sc-hint" title="Due; waiting for a worker">due</span>
        </td>
        <td class="sc-num">{{ job.attempt }}</td>
        <td><RelativeTime :value="job.state === 'scheduled' ? job.runAt : job.completedAt ?? job.runAt" :now="now" /></td>
        <td><RelativeTime :value="job.createdAt" :now="now" /></td>
        <td v-if="!readonly" class="sc-actions" @click.stop>
          <slot name="actions" :job="job">
            <button type="button" class="sc-btn sc-btn--sm" :disabled="job.state === 'processing'" @click="emit('requeue', job)">Requeue</button>
            <button type="button" class="sc-btn sc-btn--sm sc-btn--danger" @click="emit('delete', job)">Delete</button>
          </slot>
        </td>
      </tr>
      <tr v-if="jobs.length === 0"><td :colspan="columns"><slot name="empty"><div class="sc-empty"><span class="sc-empty__title">No jobs match</span><span>Try a different state or queue.</span></div></slot></td></tr>
    </tbody>
  </table>
</template>
