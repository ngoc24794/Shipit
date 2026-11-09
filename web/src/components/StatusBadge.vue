<script setup lang="ts">
import { computed } from 'vue'
import {
  Clock,
  Loader2,
  CheckCircle2,
  XCircle,
  MinusCircle,
  Circle
} from 'lucide-vue-next'
import type { RunStatus, StepStatus } from '../api/types'

export type BadgeStatus = RunStatus | StepStatus

const props = withDefaults(
  defineProps<{
    status: BadgeStatus
    size?: 'sm' | 'md'
  }>(),
  {
    size: 'sm'
  }
)

const config = computed(() => {
  switch (props.status) {
    case 'queued':
      return {
        label: 'Đang chờ',
        icon: Clock,
        spin: false,
        classes: 'text-warn bg-warn/10 border-warn/30'
      }
    case 'running':
      return {
        label: 'Đang chạy',
        icon: Loader2,
        spin: true,
        classes: 'text-info bg-info/10 border-info/30'
      }
    case 'success':
      return {
        label: 'Thành công',
        icon: CheckCircle2,
        spin: false,
        classes: 'text-primary bg-primary/10 border-primary/30'
      }
    case 'failed':
      return {
        label: 'Thất bại',
        icon: XCircle,
        spin: false,
        classes: 'text-danger bg-danger/10 border-danger/30'
      }
    case 'skipped':
      return {
        label: 'Bỏ qua',
        icon: MinusCircle,
        spin: false,
        classes: 'text-muted-fg bg-muted border-border'
      }
    case 'pending':
    default:
      return {
        label: 'Chưa chạy',
        icon: Circle,
        spin: false,
        classes: 'text-muted-fg bg-muted border-border'
      }
  }
})
</script>

<template>
  <span
    :class="[
      'inline-flex items-center font-medium border rounded-full whitespace-nowrap select-none',
      size === 'sm' ? 'px-2 py-0.5 text-xs gap-1.5' : 'px-3 py-1 text-sm gap-2',
      config.classes
    ]"
    :data-status="status"
  >
    <component
      :is="config.icon"
      :class="[
        size === 'sm' ? 'w-3.5 h-3.5' : 'w-4 h-4',
        config.spin ? 'animate-spin' : ''
      ]"
      aria-hidden="true"
    />
    <span>{{ config.label }}</span>
  </span>
</template>
