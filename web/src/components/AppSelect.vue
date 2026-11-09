<script setup lang="ts">
import { ChevronDown } from 'lucide-vue-next'

export interface SelectOption {
  label: string
  value: string | number
  disabled?: boolean
}

const props = withDefaults(
  defineProps<{
    modelValue?: string | number
    options: SelectOption[]
    id?: string
    name?: string
    disabled?: boolean
    error?: boolean | string
    ariaDescribedby?: string
    placeholder?: string
  }>(),
  {
    modelValue: '',
    id: undefined,
    name: undefined,
    disabled: false,
    error: false,
    ariaDescribedby: undefined,
    placeholder: undefined
  }
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: string | number): void
  (e: 'change', value: string | number): void
}>()

function onChange(event: Event) {
  const target = event.target as HTMLSelectElement
  emit('update:modelValue', target.value)
  emit('change', target.value)
}
</script>

<template>
  <div class="relative w-full">
    <select
      :id="id"
      :name="name"
      :value="modelValue"
      :disabled="disabled"
      :aria-invalid="!!error ? 'true' : undefined"
      :aria-describedby="ariaDescribedby"
      :class="[
        'w-full h-10 pl-3 pr-10 text-base sm:text-sm rounded-[6px] bg-card text-fg border appearance-none transition-colors cursor-pointer',
        'focus-visible:outline-2 focus-visible:outline-ring focus-visible:outline-offset-2',
        'disabled:opacity-50 disabled:bg-muted disabled:cursor-not-allowed',
        error ? 'border-danger focus-visible:outline-danger' : 'border-border focus-visible:border-primary'
      ]"
      @change="onChange"
    >
      <option v-if="placeholder" value="" disabled :selected="modelValue === ''">
        {{ placeholder }}
      </option>
      <option
        v-for="opt in options"
        :key="opt.value"
        :value="opt.value"
        :disabled="opt.disabled"
      >
        {{ opt.label }}
      </option>
    </select>
    <div
      class="absolute inset-y-0 right-0 flex items-center pr-3 pointer-events-none text-muted-fg"
      aria-hidden="true"
    >
      <ChevronDown class="w-4 h-4" />
    </div>
  </div>
</template>
