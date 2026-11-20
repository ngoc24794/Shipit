<script setup lang="ts">
import { computed } from 'vue'
import {
  GitBranch,
  Hammer,
  Upload,
  Rocket,
  Terminal,
  CheckCircle2,
  XCircle,
  Loader2,
  MinusCircle,
  Circle,
  CircleStop
} from 'lucide-vue-next'
import type { RunStep, StepName, StepStatus } from '../api/types'

const props = withDefaults(
  defineProps<{
    steps?: RunStep[]
    kind?: 'pipeline' | 'job'
    orientation?: 'horizontal' | 'vertical'
  }>(),
  {
    steps: () => [],
    kind: undefined,
    orientation: 'horizontal'
  }
)

interface StepDefinition {
  name: StepName
  label: string
  icon: any
}

const PIPELINE_DEFINITIONS: StepDefinition[] = [
  { name: 'checkout', label: 'Checkout', icon: GitBranch },
  { name: 'build', label: 'Build', icon: Hammer },
  { name: 'push', label: 'Push', icon: Upload },
  { name: 'deploy', label: 'Deploy', icon: Rocket }
]

const JOB_DEFINITIONS: StepDefinition[] = [
  { name: 'checkout', label: 'Checkout', icon: GitBranch },
  { name: 'build', label: 'Build', icon: Hammer },
  { name: 'push', label: 'Push', icon: Upload },
  { name: 'runjob', label: 'Chạy Job', icon: Terminal }
]

const isJob = computed(() => {
  if (props.kind === 'job') return true
  return props.steps?.some((s) => s.name === 'runjob') ?? false
})

const currentDefinitions = computed(() => {
  return isJob.value ? JOB_DEFINITIONS : PIPELINE_DEFINITIONS
})

function formatDuration(startedAt?: string | null, finishedAt?: string | null): string {
  if (!startedAt) return ''
  const start = new Date(startedAt).getTime()
  const end = finishedAt ? new Date(finishedAt).getTime() : Date.now()
  const sec = Math.max(0, Math.floor((end - start) / 1000))
  if (sec < 60) return `${sec}s`
  const min = Math.floor(sec / 60)
  const remSec = sec % 60
  return `${min}m ${remSec}s`
}

const mappedSteps = computed(() => {
  return currentDefinitions.value.map((def) => {
    const stepData = props.steps?.find((s) => s.name === def.name)
    const status: StepStatus = stepData?.status || 'pending'
    const duration = formatDuration(stepData?.startedAt, stepData?.finishedAt)
    return {
      ...def,
      status,
      startedAt: stepData?.startedAt,
      finishedAt: stepData?.finishedAt,
      duration
    }
  })
})

function getStatusGlyph(status: StepStatus): { glyph: string; class: string } {
  switch (status) {
    case 'success':
      return { glyph: '●', class: 'text-phosphor' }
    case 'failed':
      return { glyph: '✕', class: 'text-danger' }
    case 'cancelled':
      return { glyph: '○', class: 'text-warn' }
    case 'running':
      return { glyph: '▲', class: 'text-accent' }
    case 'skipped':
    case 'pending':
    default:
      return { glyph: '◌', class: 'text-fg-dim' }
  }
}

function getStatusBadge(status: StepStatus) {
  switch (status) {
    case 'success':
      return { icon: CheckCircle2, class: 'text-phosphor' }
    case 'failed':
      return { icon: XCircle, class: 'text-danger' }
    case 'cancelled':
      return { icon: CircleStop, class: 'text-warn' }
    case 'running':
      return { icon: Loader2, class: 'text-accent animate-spin' }
    case 'skipped':
      return { icon: MinusCircle, class: 'text-fg-muted' }
    case 'pending':
    default:
      return { icon: Circle, class: 'text-fg-dim' }
  }
}
</script>

<template>
  <div v-if="orientation === 'vertical'" class="w-full font-mono">
    <ol class="flex flex-col w-full space-y-1">
      <li
        v-for="(step, index) in mappedSteps"
        :key="step.name"
        :data-step-name="step.name"
        :data-step-status="step.status"
        :class="[
          'flex items-center justify-between gap-2 px-2 py-1.5 rounded-[2px] border text-xs min-w-0 transition-colors',
          step.status === 'running'
            ? 'border-accent bg-accent-bg'
            : step.status === 'failed'
              ? 'border-danger/50 bg-danger-bg'
              : 'border-border bg-bg-surface'
        ]"
      >
        <div class="flex items-center gap-1.5 min-w-0">
          <span class="text-fg-dim select-none shrink-0" aria-hidden="true">
            {{ index === mappedSteps.length - 1 ? '└─' : '├─' }}
          </span>
          <span
            :class="['font-bold shrink-0 select-none', getStatusGlyph(step.status).class]"
            aria-hidden="true"
          >
            {{ getStatusGlyph(step.status).glyph }}
          </span>
          <component :is="step.icon" class="w-3.5 h-3.5 text-fg-muted shrink-0" aria-hidden="true" />
          <span class="font-medium text-fg-strong whitespace-nowrap truncate">{{ step.label }}</span>
        </div>

        <div class="flex items-center gap-1.5 shrink-0">
          <span
            v-if="step.status === 'running'"
            class="text-[11px] text-accent whitespace-nowrap"
          >
            Đang chạy...
          </span>
          <span
            v-else-if="step.status === 'cancelled'"
            class="text-[11px] text-warn whitespace-nowrap tabular-nums"
          >
            {{ step.duration ? `Đã dừng (${step.duration})` : 'Đã dừng' }}
          </span>
          <span
            v-else-if="step.duration"
            class="text-[11px] text-fg-muted tabular-nums whitespace-nowrap"
          >
            {{ step.duration }}
          </span>
          <span
            v-else
            class="text-[11px] text-fg-dim whitespace-nowrap"
          >
            {{ step.status === 'skipped' ? 'Bỏ qua' : 'Chưa chạy' }}
          </span>
        </div>
      </li>
    </ol>
  </div>

  <div v-else class="w-full py-2">
    <ol class="flex flex-row items-center justify-between w-full gap-0">
      <li
        v-for="(step, index) in mappedSteps"
        :key="step.name"
        :data-step-name="step.name"
        :data-step-status="step.status"
        class="relative flex flex-col items-center flex-1 min-w-0 gap-2"
      >
        <!-- Connecting line -->
        <div
          v-if="index < mappedSteps.length - 1"
          aria-hidden="true"
          class="block absolute top-5 left-[50%] w-full h-0.5 -z-0 transition-colors"
          :class="[
            step.status === 'failed' || mappedSteps[index + 1]?.status === 'failed'
              ? 'bg-danger/40'
              : step.status === 'success'
                ? 'bg-success'
                : 'bg-border'
          ]"
        />

        <!-- Step Icon Circle -->
        <div
          :class="[
            'relative z-10 flex items-center justify-center w-10 h-10 rounded-full border-2 transition-all',
            'bg-card',
            step.status === 'success' ? 'border-success text-success shadow-xs' : '',
            step.status === 'failed' ? 'border-danger text-danger' : '',
            step.status === 'running' ? 'border-info text-info ring-4 ring-info/10' : '',
            step.status === 'cancelled' ? 'border-warn text-warn' : '',
            step.status === 'skipped' ? 'border-border text-muted-fg' : '',
            step.status === 'pending' ? 'border-border text-muted-fg' : ''
          ]"
        >
          <component :is="step.icon" class="w-5 h-5" aria-hidden="true" />
        </div>

        <!-- Step Info -->
        <div class="flex flex-col items-center text-center min-w-0">
          <div class="flex items-center gap-1.5 font-medium text-sm text-fg">
            <span class="whitespace-nowrap">{{ step.label }}</span>
            <component
              :is="getStatusBadge(step.status).icon"
              :class="['w-3.5 h-3.5', getStatusBadge(step.status).class]"
              aria-hidden="true"
            />
          </div>
          <span
            v-if="step.status === 'running'"
            class="text-xs text-info"
          >
            Đang chạy...
          </span>
          <span
            v-else-if="step.status === 'cancelled'"
            class="text-xs text-warn"
          >
            {{ step.duration ? `Đã dừng (${step.duration})` : 'Đã dừng' }}
          </span>
          <span
            v-else-if="step.duration"
            class="text-xs text-muted-fg tabular-nums"
          >
            {{ step.duration }}
          </span>
          <span
            v-else
            class="text-xs text-muted-fg"
          >
            {{ step.status === 'skipped' ? 'Đã bỏ qua' : 'Chưa chạy' }}
          </span>
        </div>
      </li>
    </ol>
  </div>
</template>
