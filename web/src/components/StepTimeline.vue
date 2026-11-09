<script setup lang="ts">
import { computed } from 'vue'
import {
  GitBranch,
  Hammer,
  Upload,
  Rocket,
  CheckCircle2,
  XCircle,
  Loader2,
  MinusCircle,
  Circle,
  CircleStop
} from 'lucide-vue-next'
import type { RunStep, StepName, StepStatus } from '../api/types'

const props = defineProps<{
  steps?: RunStep[]
}>()

interface StepDefinition {
  name: StepName
  label: string
  icon: any
}

const STEP_DEFINITIONS: StepDefinition[] = [
  { name: 'checkout', label: 'Checkout', icon: GitBranch },
  { name: 'build', label: 'Build', icon: Hammer },
  { name: 'push', label: 'Push', icon: Upload },
  { name: 'deploy', label: 'Deploy', icon: Rocket }
]

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
  return STEP_DEFINITIONS.map((def) => {
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

function getStatusBadge(status: StepStatus) {
  switch (status) {
    case 'success':
      return { icon: CheckCircle2, class: 'text-primary' }
    case 'failed':
      return { icon: XCircle, class: 'text-danger' }
    case 'cancelled':
      return { icon: CircleStop, class: 'text-warn' }
    case 'running':
      return { icon: Loader2, class: 'text-info animate-spin' }
    case 'skipped':
      return { icon: MinusCircle, class: 'text-muted-fg' }
    case 'pending':
    default:
      return { icon: Circle, class: 'text-muted-fg' }
  }
}
</script>

<template>
  <div class="w-full py-4">
    <ol class="flex flex-col md:flex-row items-start md:items-center justify-between w-full gap-4 md:gap-0">
      <li
        v-for="(step, index) in mappedSteps"
        :key="step.name"
        :data-step-name="step.name"
        :data-step-status="step.status"
        class="relative flex flex-row md:flex-col items-center md:items-center flex-1 w-full gap-3 md:gap-2"
      >
        <!-- Connecting line on desktop -->
        <div
          v-if="index < mappedSteps.length - 1"
          aria-hidden="true"
          class="hidden md:block absolute top-5 left-[50%] w-full h-0.5 -z-0 transition-colors"
          :class="[
            step.status === 'success' ? 'bg-primary' : 'bg-border'
          ]"
        />

        <!-- Step Icon Circle -->
        <div
          :class="[
            'relative z-10 flex items-center justify-center w-10 h-10 rounded-full border-2 transition-all',
            'bg-card',
            step.status === 'success' ? 'border-primary text-primary shadow-xs' : '',
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
        <div class="flex flex-col md:items-center text-left md:text-center min-w-0">
          <div class="flex items-center gap-1.5 font-medium text-sm text-fg">
            <span>{{ step.label }}</span>
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
