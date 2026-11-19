<script setup lang="ts">
import { computed } from 'vue'
import AppSpinner from './AppSpinner.vue'
import AppKbd from './AppKbd.vue'

const props = withDefaults(
  defineProps<{
    variant?: 'primary' | 'secondary' | 'ghost' | 'danger' | 'link'
    size?: 'sm' | 'md' | 'lg'
    type?: 'button' | 'submit' | 'reset'
    loading?: boolean
    disabled?: boolean
    ariaLabel?: string
    iconOnly?: boolean
    kbd?: string
    block?: boolean
    as?: 'button' | 'a' | 'router-link'
    to?: string | Record<string, unknown>
    href?: string
  }>(),
  {
    variant: 'primary',
    size: 'md',
    type: 'button',
    loading: false,
    disabled: false,
    ariaLabel: undefined,
    iconOnly: false,
    kbd: undefined,
    block: false,
    as: 'button',
    to: undefined,
    href: undefined
  }
)

const variantClasses = computed(() => {
  switch (props.variant) {
    case 'secondary':
      return 'bg-bg-surface text-fg border border-border-strong hover:bg-bg-hover hover:border-fg-muted'
    case 'ghost':
      return 'bg-transparent text-fg-muted border border-transparent hover:bg-bg-hover hover:border-border hover:text-fg-strong'
    case 'danger':
      return 'bg-danger-bg text-danger border border-danger hover:bg-danger hover:text-bg'
    case 'link':
      return 'bg-transparent text-accent border border-transparent hover:underline px-1'
    case 'primary':
    default:
      return 'bg-phosphor-bg text-phosphor border border-phosphor hover:bg-phosphor hover:text-bg font-semibold'
  }
})

const sizeClasses = computed(() => {
  if (props.iconOnly) {
    switch (props.size) {
      case 'sm':
        return 'h-6 w-6 p-0 text-xs rounded-[2px]'
      case 'lg':
        return 'h-[34px] w-[34px] p-0 text-sm rounded-[2px]'
      case 'md':
      default:
        return 'h-7 w-7 p-0 text-xs rounded-[2px]'
    }
  }
  switch (props.size) {
    case 'sm':
      return 'h-6 px-2 text-[11.5px] rounded-[2px] gap-1.5'
    case 'lg':
      return 'h-[34px] px-4 text-sm rounded-[2px] gap-2'
    case 'md':
    default:
      return 'h-7 px-2.5 text-xs rounded-[2px] gap-1.5'
  }
})

const componentTag = computed(() => {
  if (props.as === 'router-link' || props.to) return 'router-link'
  if (props.as === 'a' || props.href) return 'a'
  return 'button'
})
</script>

<template>
  <component
    :is="componentTag"
    :type="componentTag === 'button' ? type : undefined"
    :to="componentTag === 'router-link' ? to : undefined"
    :href="componentTag === 'a' ? href : undefined"
    :disabled="componentTag === 'button' ? (disabled || loading) : undefined"
    :aria-disabled="componentTag !== 'button' && (disabled || loading) ? 'true' : undefined"
    :aria-label="ariaLabel"
    :aria-busy="loading ? 'true' : undefined"
    :class="[
      'relative inline-flex items-center justify-center font-mono font-medium select-none cursor-pointer whitespace-nowrap',
      'transition-[background-color,border-color,color,transform] duration-[80ms] active:translate-y-px',
      'focus-visible:outline-2 focus-visible:outline-border-focus focus-visible:outline-offset-2',
      'disabled:opacity-45 disabled:cursor-not-allowed disabled:pointer-events-none',
      block ? 'w-full' : '',
      variantClasses,
      sizeClasses
    ]"
  >
    <span
      v-if="loading"
      class="absolute inset-0 flex items-center justify-center pointer-events-none"
      aria-hidden="true"
    >
      <AppSpinner :size="size === 'lg' ? 'md' : 'sm'" />
    </span>
    <span :class="['inline-flex items-center gap-1.5', loading ? 'opacity-0' : 'opacity-100']">
      <slot name="leading" />
      <slot />
      <slot name="trailing" />
      <AppKbd v-if="kbd && !iconOnly" :keys="kbd" />
    </span>
  </component>
</template>
