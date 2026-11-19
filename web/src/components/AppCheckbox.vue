<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    modelValue?: boolean | Array<string | number>
    value?: string | number
    indeterminate?: boolean
    label?: string
    description?: string
    id?: string
    name?: string
    disabled?: boolean
    error?: boolean | string
    ariaDescribedby?: string
  }>(),
  {
    modelValue: false,
    value: undefined,
    indeterminate: false,
    label: undefined,
    description: undefined,
    id: undefined,
    name: undefined,
    disabled: false,
    error: false,
    ariaDescribedby: undefined
  }
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: boolean | Array<string | number>): void
  (e: 'change', value: boolean | Array<string | number>): void
}>()

const isChecked = computed(() => {
  if (Array.isArray(props.modelValue)) {
    return props.value !== undefined && props.modelValue.includes(props.value)
  }
  return Boolean(props.modelValue)
})

const glyph = computed(() => {
  if (props.indeterminate) return '[-]'
  return isChecked.value ? '[x]' : '[ ]'
})

function toggle() {
  if (props.disabled) return
  if (Array.isArray(props.modelValue)) {
    if (props.value === undefined) return
    const next = [...props.modelValue]
    const idx = next.indexOf(props.value)
    if (idx >= 0) next.splice(idx, 1)
    else next.push(props.value)
    emit('update:modelValue', next)
    emit('change', next)
  } else {
    const next = !isChecked.value
    emit('update:modelValue', next)
    emit('change', next)
  }
}

function onKeydown(event: KeyboardEvent) {
  if (event.key === ' ') {
    event.preventDefault()
    toggle()
  }
}
</script>

<template>
  <label
    :for="id"
    :class="[
      'inline-flex items-start gap-2 font-mono select-none group',
      disabled ? 'opacity-50 cursor-not-allowed' : 'cursor-pointer'
    ]"
    @click.prevent="toggle"
  >
    <input
      v-if="name"
      type="checkbox"
      :name="name"
      :checked="isChecked"
      :disabled="disabled"
      class="sr-only"
      tabindex="-1"
    />
    <span
      :id="id"
      role="checkbox"
      :tabindex="disabled ? -1 : 0"
      :aria-checked="indeterminate ? 'mixed' : isChecked ? 'true' : 'false'"
      :aria-disabled="disabled ? 'true' : undefined"
      :aria-invalid="!!error ? 'true' : undefined"
      :aria-describedby="ariaDescribedby"
      :class="[
        'inline-flex items-center justify-center text-xs font-bold leading-5 shrink-0 rounded-[2px] transition-colors duration-[80ms]',
        'focus-visible:outline-2 focus-visible:outline-border-focus focus-visible:outline-offset-2',
        error
          ? 'text-danger'
          : isChecked || indeterminate
            ? 'text-phosphor'
            : 'text-fg-muted group-hover:text-fg-strong'
      ]"
      @keydown="onKeydown"
    >
      {{ glyph }}
    </span>
    <span v-if="label || description || $slots.default" class="flex flex-col gap-0.5 leading-5">
      <span class="text-xs text-fg group-hover:text-fg-strong">
        <slot>{{ label }}</slot>
      </span>
      <span v-if="description" class="text-[11px] text-fg-muted leading-snug">
        {{ description }}
      </span>
    </span>
  </label>
</template>
