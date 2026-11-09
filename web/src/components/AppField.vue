<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    label?: string
    id?: string
    required?: boolean
    description?: string
    error?: string | null
  }>(),
  {
    label: undefined,
    id: undefined,
    required: false,
    description: undefined,
    error: null
  }
)

const generatedId = computed(() => props.id || `field-${Math.random().toString(36).substring(2, 9)}`)
const descriptionId = computed(() => props.description ? `${generatedId.value}-desc` : undefined)
const errorId = computed(() => props.error ? `${generatedId.value}-error` : undefined)

const ariaDescribedby = computed(() => {
  const ids: string[] = []
  if (errorId.value) ids.push(errorId.value)
  if (descriptionId.value) ids.push(descriptionId.value)
  return ids.length > 0 ? ids.join(' ') : undefined
})
</script>

<template>
  <div class="flex flex-col gap-1.5 w-full">
    <label
      v-if="label"
      :for="generatedId"
      class="text-sm font-medium text-fg flex items-center gap-1 select-none"
    >
      {{ label }}
      <span v-if="required" class="text-danger" aria-hidden="true">*</span>
    </label>

    <slot :id="generatedId" :error="!!error" :aria-describedby="ariaDescribedby" />

    <p
      v-if="description && !error"
      :id="descriptionId"
      class="text-xs text-muted-fg leading-relaxed"
    >
      {{ description }}
    </p>

    <p
      v-if="error"
      :id="errorId"
      role="alert"
      class="text-xs text-danger font-medium leading-relaxed"
    >
      {{ error }}
    </p>
  </div>
</template>
