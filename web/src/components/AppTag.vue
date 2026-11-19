<script setup lang="ts">
import { computed } from 'vue'
import { X } from 'lucide-vue-next'

const props = withDefaults(
  defineProps<{
    variant?: 'default' | 'accent' | 'phosphor' | 'warn' | 'danger'
    removable?: boolean
    disabled?: boolean
    size?: 'sm' | 'md'
  }>(),
  {
    variant: 'default',
    removable: false,
    disabled: false,
    size: 'md'
  }
)

const emit = defineEmits<{
  (e: 'remove'): void
}>()

const variantClass = computed(() => {
  switch (props.variant) {
    case 'accent':
      return 'bg-accent-bg text-accent border-accent/30'
    case 'phosphor':
      return 'bg-phosphor-bg text-phosphor border-phosphor/30'
    case 'warn':
      return 'bg-warn-bg text-warn border-warn/30'
    case 'danger':
      return 'bg-danger-bg text-danger border-danger/30'
    case 'default':
    default:
      return 'bg-bg-surface text-fg border-border'
  }
})

const sizeClass = computed(() => {
  return props.size === 'sm' ? 'h-5 px-1.5 text-[11px]' : 'h-6 px-2 text-xs'
})
</script>

<template>
  <span
    :class="[
      'inline-flex items-center gap-1 font-mono font-medium border rounded-[2px] whitespace-nowrap select-none',
      variantClass,
      sizeClass,
      disabled ? 'opacity-50' : ''
    ]"
  >
    <span class="truncate max-w-[200px]">
      <slot />
    </span>
    <button
      v-if="removable"
      type="button"
      :disabled="disabled"
      aria-label="Xoá thẻ"
      class="inline-flex items-center justify-center p-0.5 -mr-0.5 rounded-[2px] hover:bg-bg-hover text-current opacity-75 hover:opacity-100 cursor-pointer disabled:cursor-not-allowed focus-visible:outline-2 focus-visible:outline-border-focus"
      @click.stop="emit('remove')"
    >
      <X class="w-3 h-3" aria-hidden="true" />
    </button>
  </span>
</template>
