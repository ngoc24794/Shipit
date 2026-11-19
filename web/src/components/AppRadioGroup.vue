<script lang="ts">
import type { Ref } from 'vue'

export interface RadioGroupContext {
  modelValue: Ref<string | number | undefined>
  name: Ref<string | undefined>
  disabled: Ref<boolean>
  orientation: Ref<'vertical' | 'horizontal'>
  registerOption: (value: string | number, el: HTMLElement, isDisabled: boolean) => void
  unregisterOption: (value: string | number) => void
  selectValue: (value: string | number) => void
  moveFocus: (currentValue: string | number, delta: number) => void
}

export const RADIO_GROUP_KEY = Symbol('shipit-radio-group')
</script>

<script setup lang="ts">
import { provide, computed, ref } from 'vue'

const props = withDefaults(
  defineProps<{
    modelValue?: string | number
    name?: string
    ariaLabel?: string
    disabled?: boolean
    orientation?: 'vertical' | 'horizontal'
  }>(),
  {
    modelValue: undefined,
    name: undefined,
    ariaLabel: undefined,
    disabled: false,
    orientation: 'vertical'
  }
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: string | number): void
  (e: 'change', value: string | number): void
}>()

const registered = ref<Array<{ value: string | number; el: HTMLElement; disabled: boolean }>>([])

function registerOption(value: string | number, el: HTMLElement, isDisabled: boolean) {
  const existing = registered.value.find((r) => r.value === value)
  if (existing) {
    existing.el = el
    existing.disabled = isDisabled
  } else {
    registered.value.push({ value, el, disabled: isDisabled })
  }
}

function unregisterOption(value: string | number) {
  registered.value = registered.value.filter((r) => r.value !== value)
}

function selectValue(value: string | number) {
  if (props.disabled) return
  emit('update:modelValue', value)
  emit('change', value)
}

function moveFocus(currentValue: string | number, delta: number) {
  const enabled = registered.value.filter((r) => !r.disabled && !props.disabled)
  if (enabled.length === 0) return
  const idx = enabled.findIndex((r) => r.value === currentValue)
  const nextIdx = idx < 0 ? 0 : (idx + delta + enabled.length) % enabled.length
  const target = enabled[nextIdx]
  if (target) {
    selectValue(target.value)
    target.el.focus()
  }
}

provide(RADIO_GROUP_KEY, {
  modelValue: computed(() => props.modelValue),
  name: computed(() => props.name),
  disabled: computed(() => props.disabled),
  orientation: computed(() => props.orientation),
  registerOption,
  unregisterOption,
  selectValue,
  moveFocus
})
</script>

<template>
  <div
    role="radiogroup"
    :aria-label="ariaLabel"
    :aria-orientation="orientation"
    :class="[
      'flex gap-2',
      orientation === 'horizontal' ? 'flex-row flex-wrap items-center gap-4' : 'flex-col'
    ]"
  >
    <slot />
  </div>
</template>
