<script setup lang="ts">
import { computed } from 'vue'

export type AppBadgeVariant = 'default' | 'phosphor' | 'accent' | 'warn' | 'danger'

const props = withDefaults(
  defineProps<{
    variant?: AppBadgeVariant
    size?: 'sm' | 'md'
    count?: number | string
  }>(),
  {
    variant: 'default',
    size: 'sm',
    count: undefined
  }
)

const variantClasses = computed(() => {
  switch (props.variant) {
    case 'phosphor':
      return 'text-phosphor bg-[var(--phosphor-bg)] border-phosphor/30'
    case 'accent':
      return 'text-accent bg-[var(--accent-bg)] border-accent/30'
    case 'warn':
      return 'text-warn bg-[var(--warn-bg)] border-warn/30'
    case 'danger':
      return 'text-danger bg-[var(--danger-bg)] border-danger/30'
    case 'default':
    default:
      return 'text-fg-muted bg-bg-surface border-border'
  }
})
</script>

<template>
  <span
    :class="[
      'inline-flex items-center justify-center font-mono font-semibold border rounded-[2px] tabular-nums whitespace-nowrap select-none',
      size === 'sm' ? 'px-1.5 py-0 text-[11px] leading-[18px]' : 'px-2 py-0.5 text-[12px]',
      variantClasses
    ]"
  >
    <slot>{{ count }}</slot>
  </span>
</template>
