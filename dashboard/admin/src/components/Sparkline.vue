<script setup lang="ts">
import { computed } from "vue";

// A single-series trend line for a stat tile. The tile's label names the series, so there is no legend; the
// absolute numbers are on the tile and in the tooltip, so the line carries shape only.
const props = withDefaults(defineProps<{ values: number[]; label: string; width?: number; height?: number }>(), { width: 120, height: 32 });

const path = computed(() => {
  const v = props.values;
  if (v.length < 2) return null;
  const min = Math.min(...v);
  const max = Math.max(...v);
  const span = max - min || 1;
  const pad = 3;
  const w = props.width;
  const h = props.height - pad * 2;
  const pts = v.map((y, i) => [(i / (v.length - 1)) * w, pad + h - ((y - min) / span) * h] as const);
  const line = pts.map(([x, y], i) => `${i ? "L" : "M"}${x.toFixed(1)},${y.toFixed(1)}`).join(" ");
  const last = pts[pts.length - 1]!;
  return { line, area: `${line} L${w},${props.height} L0,${props.height} Z`, last };
});
const summary = computed(() => {
  const v = props.values;
  if (v.length < 2) return `${props.label}: collecting samples`;
  return `${props.label}: ${v[0]} to ${v[v.length - 1]} over the last ${v.length} samples, low ${Math.min(...v)}, high ${Math.max(...v)}`;
});
</script>

<template>
  <svg class="spark" :viewBox="`0 0 ${width} ${height}`" :width="width" :height="height" preserveAspectRatio="none" role="img" :aria-label="summary">
    <title>{{ summary }}</title>
    <template v-if="path">
      <path class="spark__area" :d="path.area" />
      <path class="spark__line" :d="path.line" vector-effect="non-scaling-stroke" />
      <circle class="spark__dot" :cx="path.last[0]" :cy="path.last[1]" r="2.5" />
    </template>
    <line v-else class="spark__empty" x1="0" :y1="height - 4" :x2="width" :y2="height - 4" />
  </svg>
</template>
