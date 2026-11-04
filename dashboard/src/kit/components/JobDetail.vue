<script setup lang="ts">
import type { Job } from "../api";
import { formatDateTime } from "../format";
import PayloadView from "./PayloadView.vue";
import RelativeTime from "./RelativeTime.vue";
import StateBadge from "./StateBadge.vue";

defineProps<{ job: Job; now?: string | null; readonly?: boolean }>();
const emit = defineEmits<{ requeue: [job: Job]; delete: [job: Job]; close: [] }>();
</script>

<template>
  <section class="sc-detail">
    <header class="sc-detail__header">
      <h2 class="sc-detail__title">{{ job.displayName }} <StateBadge :state="job.state" /></h2>
      <div class="sc-detail__actions">
        <slot name="actions" :job="job">
          <button v-if="!readonly" type="button" class="sc-btn" :disabled="job.state === 'processing'" @click="emit('requeue', job)">Requeue</button>
          <button v-if="!readonly" type="button" class="sc-btn sc-btn--danger" @click="emit('delete', job)">Delete</button>
        </slot>
        <button type="button" class="sc-btn sc-btn--ghost" @click="emit('close')">Close</button>
      </div>
    </header>

    <dl class="sc-props">
      <dt>Id</dt><dd><code>{{ job.id }}</code></dd>
      <dt>Name</dt><dd><code>{{ job.name }}</code></dd>
      <dt>Queue</dt><dd><code>{{ job.queue }}</code></dd>
      <dt>Attempt</dt><dd>{{ job.attempt }}</dd>
      <dt>Created</dt><dd>{{ formatDateTime(job.createdAt) }} (<RelativeTime :value="job.createdAt" :now="now" />)</dd>
      <dt>Runs at</dt><dd>{{ formatDateTime(job.runAt) }} (<RelativeTime :value="job.runAt" :now="now" />)</dd>
      <dt v-if="job.completedAt">Completed</dt><dd v-if="job.completedAt">{{ formatDateTime(job.completedAt) }}</dd>
      <dt v-if="job.leaseOwner">Lease</dt><dd v-if="job.leaseOwner"><code>{{ job.leaseOwner }}</code> until {{ formatDateTime(job.leaseExpiresAt) }}</dd>
      <dt v-if="job.idempotencyKey">Idempotency key</dt><dd v-if="job.idempotencyKey"><code>{{ job.idempotencyKey }}</code></dd>
    </dl>

    <h3>Payload</h3>
    <PayloadView :value="job.payload" />

    <template v-if="job.lastError">
      <h3>Last error</h3>
      <pre class="sc-error"><code>{{ job.lastError }}</code></pre>
    </template>

    <slot />
  </section>
</template>
