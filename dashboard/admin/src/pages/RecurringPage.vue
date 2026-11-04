<script setup lang="ts">
import { computed, ref } from "vue";
import { RouterLink, useRouter } from "vue-router";
import { CronPreview, ErrorBanner, PayloadView, RelativeTime, formatDateTime, useRecurring, type RecurringJob } from "@slowcooker/vue";
import { RecurringTable } from "@slowcooker/vue";
import Drawer from "../components/Drawer.vue";
import Icon from "../components/Icon.vue";
import PageHeader from "../components/PageHeader.vue";
import { confirm } from "../confirm";
import { useSharedStats } from "../stats";
import { attempt } from "../toasts";

const props = defineProps<{ id?: string }>();
const router = useRouter();
const stats = useSharedStats();
const recurring = useRecurring({ intervalMs: 5000 });

const items = computed(() => recurring.data.value ?? []);
const selected = computed(() => (props.id ? items.value.find((r) => r.id === props.id) : undefined));
const parked = computed(() => items.value.filter((r) => !r.nextRunAt).length);

function open(job: RecurringJob) {
  void router.push({ name: "recurring", params: { id: job.id } });
}
function closeDetail() {
  void router.push({ name: "recurring" });
}

async function trigger(job: RecurringJob) {
  await attempt(() => recurring.trigger(job.id), `Triggered ${job.displayName}; it will run shortly`);
}
async function remove(job: RecurringJob) {
  const ok = await confirm({
    title: "Delete schedule?",
    message: `${job.id} will stop running. Jobs it already created are kept.`,
    confirmLabel: "Delete",
    danger: true,
  });
  if (!ok) return;
  if (await attempt(() => recurring.remove(job.id), `Deleted ${job.id}`)) {
    if (props.id === job.id) closeDetail();
  }
}

// A cron scratchpad backed by the preview endpoint. Schedules themselves are declared in code.
const expression = ref("0 6 * * *");
const timeZone = ref(Intl.DateTimeFormat().resolvedOptions().timeZone);
const presets: { label: string; cron: string }[] = [
  { label: "Every minute", cron: "* * * * *" },
  { label: "Every 15 min", cron: "*/15 * * * *" },
  { label: "Hourly", cron: "0 * * * *" },
  { label: "Daily 06:00", cron: "0 6 * * *" },
  { label: "Weekdays 09:00", cron: "0 9 * * 1-5" },
  { label: "Mondays 08:00", cron: "0 8 * * 1" },
  { label: "1st of month", cron: "0 0 1 * *" },
];
</script>

<template>
  <PageHeader title="Recurring jobs">
    <template #subtitle>
      {{ items.length }} {{ items.length === 1 ? "schedule" : "schedules" }}<template v-if="parked"> · {{ parked }} parked</template>
    </template>
    <button type="button" class="sc-btn" :disabled="recurring.loading.value" @click="recurring.refresh()"><Icon name="refresh" /> Refresh</button>
  </PageHeader>

  <ErrorBanner :error="recurring.error.value" />

  <section class="sc-card">
    <div class="sc-table-wrap">
    <RecurringTable :items="items" :now="stats.serverTime.value" @select="open" @trigger="trigger" @delete="remove" />
    </div>
  </section>

  <section class="sc-card">
    <header class="sc-card__header">
      <h2 class="sc-card__title">Cron scratchpad <span class="sc-card__subtitle">validate an expression and see when it fires</span></h2>
    </header>
    <div class="sc-card__body scratchpad">
      <form class="sc-filters" @submit.prevent>
        <label class="sc-field"><span>Expression</span><input v-model="expression" class="sc-input--mono" type="text" size="20" spellcheck="false" /></label>
        <label class="sc-field"><span>Time zone</span><input v-model="timeZone" type="text" size="24" list="time-zones" /></label>
        <datalist id="time-zones">
          <option v-for="z in ['UTC', 'Europe/London', 'Europe/Berlin', 'America/New_York', 'America/Los_Angeles', 'Asia/Tokyo', 'Australia/Sydney']" :key="z" :value="z" />
        </datalist>
      </form>
      <div class="presets">
        <button v-for="p in presets" :key="p.cron" type="button" class="sc-btn sc-btn--sm" :class="{ 'sc-btn--primary': expression === p.cron }" @click="expression = p.cron">{{ p.label }}</button>
      </div>
      <CronPreview :expression="expression" :time-zone="timeZone" :count="5" />
    </div>
  </section>

  <Drawer v-if="props.id" title="Schedule" @close="closeDetail">
    <template v-if="selected">
      <div class="sc-detail">
        <header class="sc-detail__header">
          <h2 class="sc-detail__title">
            {{ selected.displayName }}
            <span v-if="selected.nextRunAt" class="sc-badge sc-badge--scheduled">active</span>
            <span v-else class="sc-badge sc-badge--parked">parked</span>
          </h2>
          <div class="sc-detail__actions">
            <button type="button" class="sc-btn" @click="trigger(selected)"><Icon name="play" /> Run now</button>
            <button type="button" class="sc-btn sc-btn--danger" @click="remove(selected)"><Icon name="trash" /> Delete</button>
          </div>
        </header>
        <dl class="sc-props">
          <dt>Id</dt><dd><code>{{ selected.id }}</code></dd>
          <dt>Job</dt><dd><code>{{ selected.name }}</code></dd>
          <dt>Schedule</dt><dd><span class="sc-chip">{{ selected.cron }}</span> in {{ selected.timeZoneId }}</dd>
          <dt>Queue</dt><dd><span class="sc-chip">{{ selected.queue }}</span></dd>
          <dt>Next run</dt>
          <dd>
            <template v-if="selected.nextRunAt">{{ formatDateTime(selected.nextRunAt) }} (<RelativeTime :value="selected.nextRunAt" :now="stats.serverTime.value" />)</template>
            <span v-else class="sc-muted">Parked: the schedule has no future occurrence, or its time zone is unknown.</span>
          </dd>
          <dt>Last run</dt>
          <dd>
            <template v-if="selected.lastRunAt">{{ formatDateTime(selected.lastRunAt) }} (<RelativeTime :value="selected.lastRunAt" :now="stats.serverTime.value" />)</template>
            <span v-else class="sc-muted">Never</span>
          </dd>
          <dt v-if="selected.lastJobId">Last job</dt>
          <dd v-if="selected.lastJobId"><RouterLink class="sc-link" :to="{ name: 'jobs', params: { id: selected.lastJobId } }"><code>{{ selected.lastJobId }}</code></RouterLink></dd>
          <dt>Created</dt><dd>{{ formatDateTime(selected.createdAt) }}</dd>
          <dt>Updated</dt><dd>{{ formatDateTime(selected.updatedAt) }}</dd>
        </dl>
        <h3>Payload</h3>
        <PayloadView :value="selected.payload" />
        <h3>Upcoming runs</h3>
        <CronPreview :expression="selected.cron" :time-zone="selected.timeZoneId" :count="5" />
      </div>
    </template>
    <div v-else-if="recurring.data.value" class="sc-empty">
      <Icon name="alert" />
      <span class="sc-empty__title">No schedule with id {{ props.id }}</span>
      <span>It may have been deleted.</span>
    </div>
    <div v-else class="sc-empty"><span class="sc-skeleton" /></div>
  </Drawer>
</template>
