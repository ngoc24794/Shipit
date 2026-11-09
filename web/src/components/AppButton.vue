<script setup lang="ts">
import { computed } from 'vue'
import { Loader2 } from 'lucide-vue-next'

const props = withDefaults(
  defineProps<{
    variant?: 'primary' | 'secondary' | 'ghost' | 'danger'
    size?: 'sm' | 'md' | 'lg'
    type?: 'button' | 'submit' | 'reset'
    loading?: boolean
    disabled?: boolean
    ariaLabel?: string
  }>(),
  {
    variant: 'primary',
    size: 'md',
    type: 'button',
    loading: false,
    disabled: false,
    ariaLabel: undefined
  }
)

const variantClasses = computed(() => {
  switch (props.variant) {
    case 'secondary':
      return 'bg-card text-fg border border-border hover:bg-muted hover:border-border'
    case 'ghost':
      return 'bg-transparent text-fg hover:bg-muted border border-transparent'
    case 'danger':
      return 'bg-danger text-white hover:opacity-90 border border-transparent'
    case 'primary':
    default:
      return 'bg-primary text-on-primary hover:opacity-90 border border-transparent font-medium'
  }
})

const sizeClasses = computed(() => {
  switch (props.size) {
    case 'sm':
      return 'h-8 px-3 text-xs rounded-[6px] gap-1.5'
    case 'lg':
      return 'h-11 px-5 text-base rounded-[6px] gap-2.5'
    case 'md':
    default:
      return 'h-10 px-4 text-sm rounded-[6px] gap-2 min-h-[40px]'
  }
})
</script>

<template>
  <button
    :type="type"
    :disabled="disabled || loading"
    :aria-label="ariaLabel"
    :aria-busy="loading ? 'true' : undefined"
    :class="[
      'relative inline-flex items-center justify-center font-medium transition-colors select-none cursor-pointer',
      'focus-visible:outline-2 focus-visible:outline-ring focus-visible:outline-offset-2',
      'disabled:opacity-50 disabled:cursor-not-allowed disabled:pointer-events-none',
      variantClasses,
      sizeClasses
    ]"
  >
    <!-- When loading, maintain layout width by keeping children with invisible opacity -->
    <span
      v-if="loading"
      class="absolute inset-0 flex items-center justify-center pointer-events-none"
      aria-hidden="true"
    >
      <Loader2 class="w-4 h-4 animate-spin text-current" />
    </span>
    <span :class="['inline-flex items-center gap-2', loading ? 'opacity-0' : 'opacity-100']">
      <slot />
    </span>
  </button>
</template>
