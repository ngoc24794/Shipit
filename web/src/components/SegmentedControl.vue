<script setup lang="ts">
import { ref } from 'vue'

export interface SegmentedOption {
  label: string
  value: string | number
  disabled?: boolean
}

const props = withDefaults(
  defineProps<{
    modelValue?: string | number
    options: SegmentedOption[]
    ariaLabel?: string
    size?: 'sm' | 'md'
    disabled?: boolean
  }>(),
  {
    modelValue: undefined,
    ariaLabel: undefined,
    size: 'md',
    disabled: false
  }
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: string | number): void
  (e: 'change', value: string | number): void
}>()

const btnRefs = ref<HTMLButtonElement[]>([])

function selectOption(opt: SegmentedOption) {
  if (props.disabled || opt.disabled) return
  emit('update:modelValue', opt.value)
  emit('change', opt.value)
}

function getEnabledIndices(): number[] {
  return props.options
    .map((o, i) => (o.disabled || props.disabled ? -1 : i))
    .filter((i) => i !== -1)
}

function onKeydown(event: KeyboardEvent, currentIdx: number) {
  const enabled = getEnabledIndices()
  if (enabled.length === 0) return

  let targetIdx: number | null = null
  const pos = enabled.indexOf(currentIdx)

  if (event.key === 'ArrowRight' || event.key === 'ArrowDown') {
    event.preventDefault()
    targetIdx = enabled[(pos + 1) % enabled.length]
  } else if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') {
    event.preventDefault()
    targetIdx = enabled[(pos - 1 + enabled.length) % enabled.length]
  } else if (event.key === 'Home') {
    event.preventDefault()
    targetIdx = enabled[0]
  } else if (event.key === 'End') {
    event.preventDefault()
    targetIdx = enabled[enabled.length - 1]
  }

  if (targetIdx !== null) {
    selectOption(props.options[targetIdx])
    btnRefs.value[targetIdx]?.focus()
  }
}
</script>

<template>
  <div
    role="radiogroup"
    :aria-label="ariaLabel"
    class="inline-flex items-center border border-border rounded-[2px] bg-bg-elevated overflow-hidden"
  >
    <button
      v-for="(opt, idx) in options"
      :key="opt.value"
      ref="btnRefs"
      type="button"
      role="radio"
      :aria-checked="modelValue === opt.value"
      :disabled="disabled || opt.disabled"
      :tabindex="modelValue === opt.value || (modelValue === undefined && idx === 0) ? 0 : -1"
      :class="[
        'font-mono whitespace-nowrap border-r border-border last:border-r-0 transition-colors duration-[80ms] cursor-pointer select-none',
        'focus-visible:outline-2 focus-visible:outline-border-focus focus-visible:-outline-offset-2',
        'disabled:opacity-45 disabled:cursor-not-allowed',
        size === 'sm' ? 'h-6 px-2 text-[11px]' : 'h-7 px-2.5 text-xs',
        modelValue === opt.value
          ? 'bg-bg-selected text-phosphor font-semibold'
          : 'bg-transparent text-fg-muted hover:text-fg-strong hover:bg-bg-hover'
      ]"
      @click="selectOption(opt)"
      @keydown="onKeydown($event, idx)"
    >
      {{ opt.label }}
    </button>
  </div>
</template>
