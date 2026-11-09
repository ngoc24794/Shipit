<script setup lang="ts">
const props = withDefaults(
  defineProps<{
    modelValue?: string | number
    type?: string
    id?: string
    name?: string
    placeholder?: string
    disabled?: boolean
    readonly?: boolean
    error?: boolean | string
    ariaDescribedby?: string
  }>(),
  {
    modelValue: '',
    type: 'text',
    id: undefined,
    name: undefined,
    placeholder: undefined,
    disabled: false,
    readonly: false,
    error: false,
    ariaDescribedby: undefined
  }
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: string): void
  (e: 'blur', event: FocusEvent): void
  (e: 'focus', event: FocusEvent): void
}>()

function onInput(event: Event) {
  const target = event.target as HTMLInputElement
  emit('update:modelValue', target.value)
}
</script>

<template>
  <input
    :id="id"
    :name="name"
    :type="type"
    :value="modelValue"
    :placeholder="placeholder"
    :disabled="disabled"
    :readonly="readonly"
    :aria-invalid="!!error ? 'true' : undefined"
    :aria-describedby="ariaDescribedby"
    :class="[
      'w-full h-10 px-3 text-base sm:text-sm rounded-[6px] bg-card text-fg border transition-colors',
      'placeholder:text-muted-fg/60',
      'focus-visible:outline-2 focus-visible:outline-ring focus-visible:outline-offset-2',
      'disabled:opacity-50 disabled:bg-muted disabled:cursor-not-allowed',
      error ? 'border-danger focus-visible:outline-danger' : 'border-border focus-visible:border-primary'
    ]"
    @input="onInput"
    @blur="emit('blur', $event)"
    @focus="emit('focus', $event)"
  />
</template>
