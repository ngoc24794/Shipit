<script setup lang="ts">
import AppCombobox from './AppCombobox.vue'

export interface SelectOption {
  label: string
  value: string | number | boolean
  disabled?: boolean
}

const props = withDefaults(
  defineProps<{
    modelValue?: string | number | boolean | null
    options: SelectOption[]
    id?: string
    name?: string
    disabled?: boolean
    error?: boolean | string
    ariaDescribedby?: string
    placeholder?: string
    size?: 'sm' | 'md'
    searchable?: boolean
  }>(),
  {
    modelValue: '',
    id: undefined,
    name: undefined,
    disabled: false,
    error: false,
    ariaDescribedby: undefined,
    placeholder: undefined,
    size: 'md',
    searchable: false
  }
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: string | number): void
  (e: 'change', value: string | number): void
}>()

function onUpdate(val: string | number | boolean | Array<string | number | boolean> | null) {
  const next = Array.isArray(val) ? (val[0] ?? '') : (val ?? '')
  emit('update:modelValue', next as string | number)
}

function onChange(val: string | number | boolean | Array<string | number | boolean> | null) {
  const next = Array.isArray(val) ? (val[0] ?? '') : (val ?? '')
  emit('change', next as string | number)
}
</script>

<template>
  <AppCombobox
    :id="id"
    :name="name"
    :model-value="modelValue"
    :options="options"
    :disabled="disabled"
    :error="error"
    :aria-describedby="ariaDescribedby"
    :placeholder="placeholder ?? 'Chọn...'"
    :size="size"
    :searchable="searchable"
    @update:model-value="onUpdate"
    @change="onChange"
  />
</template>
