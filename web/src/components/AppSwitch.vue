<script setup lang="ts">
const props = withDefaults(
  defineProps<{
    modelValue?: boolean
    label?: string
    description?: string
    id?: string
    disabled?: boolean
    ariaLabel?: string
  }>(),
  {
    modelValue: false,
    label: undefined,
    description: undefined,
    id: undefined,
    disabled: false,
    ariaLabel: undefined
  }
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: boolean): void
  (e: 'change', value: boolean): void
}>()

function toggle() {
  if (props.disabled) return
  const next = !props.modelValue
  emit('update:modelValue', next)
  emit('change', next)
}

function onKeydown(event: KeyboardEvent) {
  if (event.key === ' ' || event.key === 'Enter') {
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
    <span
      :id="id"
      role="switch"
      :tabindex="disabled ? -1 : 0"
      :aria-checked="modelValue ? 'true' : 'false'"
      :aria-disabled="disabled ? 'true' : undefined"
      :aria-label="ariaLabel"
      :class="[
        'inline-flex items-center justify-center px-1 h-5 text-[11px] font-bold border rounded-[2px] shrink-0 transition-colors duration-[80ms]',
        'focus-visible:outline-2 focus-visible:outline-border-focus focus-visible:outline-offset-2',
        modelValue
          ? 'bg-phosphor-bg text-phosphor border-phosphor'
          : 'bg-bg-surface text-fg-muted border-border group-hover:border-border-strong'
      ]"
      @keydown="onKeydown"
    >
      {{ modelValue ? '[ON ]' : '[OFF]' }}
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
