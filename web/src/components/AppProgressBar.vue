<script setup lang="ts">
import { computed } from 'vue'

export type ProgressVariant = 'phosphor' | 'accent' | 'warn' | 'danger'

const props = withDefaults(
  defineProps<{
    value?: number
    max?: number
    indeterminate?: boolean
    label?: string
    showPercent?: boolean
    variant?: ProgressVariant
  }>(),
  {
    value: 0,
    max: 100,
    indeterminate: false,
    label: undefined,
    showPercent: true,
    variant: 'phosphor'
  }
)

const percent = computed(() => {
  if (props.max <= 0) return 0
  const raw = (props.value / props.max) * 100
  return Math.max(0, Math.min(100, Math.round(raw)))
})

const barColorClass = computed(() => {
  switch (props.variant) {
    case 'accent':
      return 'bg-accent'
    case 'warn':
      return 'bg-warn'
    case 'danger':
      return 'bg-danger'
    case 'phosphor':
    default:
      return 'bg-phosphor'
  }
})

const asciiBar = computed(() => {
  const width = 20
  const filled = Math.round((percent.value / 100) * width)
  return '█'.repeat(filled) + '░'.repeat(width - filled)
})
</script>

<template>
  <div class="w-full font-mono text-[12px] space-y-1">
    <div v-if="label || showPercent" class="flex items-center justify-between gap-2 text-[11.5px]">
      <span v-if="label" class="text-fg-muted truncate">{{ label }}</span>
      <span v-else />
      <span v-if="showPercent && !indeterminate" class="text-fg-strong tabular-nums shrink-0">
        {{ percent }}%
      </span>
      <span v-else-if="indeterminate" class="text-accent shrink-0">...</span>
    </div>

    <div
      role="progressbar"
      :aria-label="label || 'Tiến độ'"
      :aria-valuenow="indeterminate ? undefined : percent"
      aria-valuemin="0"
      aria-valuemax="100"
      class="relative h-2.5 w-full bg-bg-input border border-border rounded-[2px] overflow-hidden"
    >
      <div
        v-if="!indeterminate"
        :class="['h-full transition-[width] duration-[110ms] ease-out', barColorClass]"
        :style="{ width: `${percent}%` }"
      />
      <div
        v-else
        :class="['h-full w-1/3 d1-progress-indeterminate', barColorClass]"
      />
      <span class="sr-only">[{{ asciiBar }}] {{ percent }}%</span>
    </div>
  </div>
</template>

<style scoped>
.d1-progress-indeterminate {
  animation: d1Indeterminate 1.2s steps(8, end) infinite;
}

@keyframes d1Indeterminate {
  0% {
    transform: translateX(-100%);
  }
  100% {
    transform: translateX(300%);
  }
}
</style>
