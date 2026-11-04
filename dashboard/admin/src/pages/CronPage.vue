<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
import { CronPreview, describeCron } from "@slowcooker/vue";
import PageHeader from "../components/PageHeader.vue";

const route = useRoute();
const router = useRouter();

// The expression and zone live in the URL so a schedule under discussion is a link.
const expression = ref(typeof route.query.cron === "string" ? route.query.cron : "0 6 * * *");
const timeZone = ref(typeof route.query.tz === "string" ? route.query.tz : Intl.DateTimeFormat().resolvedOptions().timeZone);
watch([expression, timeZone], ([cron, tz]) => void router.replace({ query: { cron, tz } }));

const presets: { label: string; cron: string; code: string }[] = [
  { label: "Every minute", cron: "* * * * *", code: "Cron.EveryMinute" },
  { label: "Every 15 minutes", cron: "*/15 * * * *", code: "Cron.Every(TimeSpan.FromMinutes(15))" },
  { label: "Hourly", cron: "0 * * * *", code: "Cron.Hourly" },
  { label: "Daily at 06:00", cron: "0 6 * * *", code: "Cron.Daily.At(6)" },
  { label: "Weekdays at 09:00", cron: "0 9 * * 1-5", code: "Cron.Weekdays.At(9)" },
  { label: "Mondays at 08:00", cron: "0 8 * * 1", code: "Cron.Weekly.On(DayOfWeek.Monday).At(8)" },
  { label: "1st of the month", cron: "0 0 1 * *", code: "Cron.Monthly" },
];
const zones = ["UTC", "Europe/London", "Europe/Berlin", "America/New_York", "America/Chicago", "America/Los_Angeles", "Asia/Tokyo", "Asia/Singapore", "Australia/Sydney"];
const description = computed(() => describeCron(expression.value));
const snippet = computed(() => {
  const preset = presets.find((p) => p.cron === expression.value.trim());
  const cron = preset?.code ?? JSON.stringify(expression.value.trim());
  const tz = timeZone.value && timeZone.value !== "UTC" ? `,\n    new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById(${JSON.stringify(timeZone.value)}) }` : "";
  return `await recurring.AddOrUpdateAsync("my-schedule", new MyJob(), ${cron}${tz});`;
});
</script>

<template>
  <PageHeader title="Cron tester" subtitle="Check an expression and see exactly when it fires, evaluated by the server's cron engine and time zone database" />

  <div class="grid grid--cron">
    <section class="sc-card">
      <header class="sc-card__header"><h2 class="sc-card__title">Expression</h2></header>
      <div class="sc-card__body scratchpad">
        <form class="sc-filters" @submit.prevent>
          <label class="sc-field cron-field"><span>Cron (minute hour day month weekday)</span><input v-model="expression" class="sc-input--mono" type="text" spellcheck="false" autocomplete="off" /></label>
          <label class="sc-field"><span>Time zone</span><input v-model="timeZone" type="text" list="time-zones" spellcheck="false" /></label>
          <datalist id="time-zones"><option v-for="z in zones" :key="z" :value="z" /></datalist>
        </form>
        <div class="presets" role="group" aria-label="Presets">
          <button v-for="p in presets" :key="p.cron" type="button" class="sc-btn sc-btn--sm" :class="{ 'sc-btn--primary': expression.trim() === p.cron }" @click="expression = p.cron">{{ p.label }}</button>
        </div>
        <p v-if="description" class="cron-reading">Reads as: <strong>{{ description }}</strong></p>
        <h3 class="section-label">In code</h3>
        <pre class="sc-payload"><code>{{ snippet }}</code></pre>
      </div>
    </section>

    <section class="sc-card">
      <header class="sc-card__header"><h2 class="sc-card__title">Next 10 runs</h2></header>
      <div class="sc-card__body">
        <CronPreview :expression="expression" :time-zone="timeZone" :count="10" />
      </div>
    </section>
  </div>
</template>
