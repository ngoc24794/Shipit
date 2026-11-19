<script setup lang="ts">
import { computed } from 'vue'
import type { RunStatus, StepStatus } from '../api/types'

export type BadgeStatus = RunStatus | StepStatus

const props = withDefaults(
  defineProps<{
    status: BadgeStatus
    size?: 'sm' | 'md'
    label?: string
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
        glyph: '◌',
        pulse: false,
        classes: 'text-warn bg-[var(--warn-bg)] border-warn/30'
      }
    case 'running':
      return {
        label: 'Đang chạy',
        glyph: '▲',
        pulse: true,
        classes: 'text-accent bg-[var(--accent-bg)] border-accent/30'
      }
    case 'success':
      return {
        label: 'Thành công',
        glyph: '●',
        pulse: false,
        classes: 'text-phosphor bg-[var(--phosphor-bg)] border-phosphor/30'
      }
    case 'failed':
      return {
        label: 'Thất bại',
        glyph: '✕',
        pulse: false,
        classes: 'text-danger bg-[var(--danger-bg)] border-danger/30'
      }
    case 'cancelled':
      return {
        label: 'Đã huỷ',
        glyph: '○',
        pulse: false,
        classes: 'text-fg-muted bg-[var(--muted-badge-bg)] border-border'
      }
    case 'skipped':
      return {
        label: 'Bỏ qua',
        glyph: '─',
        pulse: false,
        classes: 'text-fg-muted bg-[var(--muted-badge-bg)] border-border'
      }
    case 'pending':
    default:
      return {
        label: 'Chưa chạy',
        glyph: '○',
        pulse: false,
        classes: 'text-fg-muted bg-[var(--muted-badge-bg)] border-border'
      }
  }
})
</script>

<template>
  <span
    :class="[
      'inline-flex items-center font-mono font-semibold border rounded-[2px] whitespace-nowrap select-none',
      size === 'sm' ? 'px-1.5 py-[1px] text-[11.5px] gap-1.5' : 'px-2.5 py-0.5 text-[12.5px] gap-2',
      config.classes
    ]"
    :data-status="status"
  >
    <span
      :class="['leading-none shrink-0', config.pulse ? 'live-pulse' : '']"
      aria-hidden="true"
    >
      {{ config.glyph }}
    </span>
    <span>{{ label || config.label }}</span>
  </span>
</template>
