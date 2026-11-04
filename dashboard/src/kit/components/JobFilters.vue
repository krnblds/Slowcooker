<script setup lang="ts">
import { JOB_STATES, type JobState, type QueueStats } from "../api";

defineProps<{ queues?: QueueStats[] }>();
const state = defineModel<JobState | null>("state", { default: null });
const queue = defineModel<string | null>("queue", { default: null });
</script>

<template>
  <form class="sc-filters" @submit.prevent>
    <label class="sc-field">
      <span>State</span>
      <select v-model="state">
        <option :value="null">Any</option>
        <option v-for="s in JOB_STATES" :key="s" :value="s">{{ s }}</option>
      </select>
    </label>
    <label class="sc-field">
      <span>Queue</span>
      <select v-if="queues" v-model="queue">
        <option :value="null">Any</option>
        <option v-for="q in queues" :key="q.name" :value="q.name">{{ q.name }}</option>
      </select>
      <input v-else v-model="queue" type="text" placeholder="any" />
    </label>
    <slot />
  </form>
</template>
