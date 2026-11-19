<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    label?: string
    id?: string
    required?: boolean
    description?: string
    help?: string
    error?: string | null
    layout?: 'vertical' | 'horizontal'
  }>(),
  {
    label: undefined,
    id: undefined,
    required: false,
    description: undefined,
    help: undefined,
    error: null,
    layout: 'vertical'
  }
)

const uid = Math.random().toString(36).substring(2, 9)
const generatedId = computed(() => props.id || `field-${uid}`)
const helpText = computed(() => props.help ?? props.description)
const descriptionId = computed(() => (helpText.value ? `${generatedId.value}-desc` : undefined))
const errorId = computed(() => (props.error ? `${generatedId.value}-error` : undefined))

const ariaDescribedby = computed(() => {
  const ids: string[] = []
  if (errorId.value) ids.push(errorId.value)
  if (descriptionId.value) ids.push(descriptionId.value)
  return ids.length > 0 ? ids.join(' ') : undefined
})
</script>

<template>
  <div
    :class="[
      'w-full font-mono',
      layout === 'horizontal'
        ? 'grid grid-cols-[160px_minmax(0,1fr)] items-start gap-x-3 gap-y-1'
        : 'flex flex-col gap-1'
    ]"
  >
    <label
      v-if="label"
      :for="generatedId"
      :class="[
        'text-[11.5px] font-semibold text-fg-muted flex items-center gap-1 select-none',
        layout === 'horizontal' ? 'pt-1.5' : ''
      ]"
    >
      <span>{{ label }}</span>
      <span v-if="required" class="text-danger" aria-hidden="true">*</span>
    </label>

    <div class="flex flex-col gap-1 min-w-0">
      <slot :id="generatedId" :error="!!error" :aria-describedby="ariaDescribedby" />

      <p
        v-if="helpText && !error"
        :id="descriptionId"
        class="text-[11px] text-fg-muted leading-snug prose"
      >
        {{ helpText }}
      </p>

      <p
        v-if="error"
        :id="errorId"
        role="alert"
        class="text-[11px] text-danger font-medium leading-snug"
      >
        {{ error }}
      </p>
    </div>
  </div>
</template>
