<script setup lang="ts">
import { computed } from 'vue'
import type { StepName } from '../api/types'
import AppCheckbox from './AppCheckbox.vue'
import {
  applyStepToggle,
  formatSkipSummary,
  getStepsForKind,
  isStepCheckboxDisabled,
  type RunTargetKind
} from '../utils/runSteps'

const props = withDefaults(
  defineProps<{
    kind: RunTargetKind
    modelValue: StepName[]
    disabled?: boolean
  }>(),
  {
    disabled: false
  }
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: StepName[]): void
}>()

const steps = computed(() => getStepsForKind(props.kind))
const summaryText = computed(() => formatSkipSummary(props.kind, props.modelValue))
const hasSkipped = computed(() => props.modelValue.length > 0)

function isStepChecked(stepName: StepName): boolean {
  return !props.modelValue.includes(stepName)
}

function isStepDisabled(stepName: StepName): boolean {
  return props.disabled || isStepCheckboxDisabled(props.kind, props.modelValue, stepName)
}

function onToggle(stepName: StepName, checked: boolean | Array<string | number>) {
  const next = applyStepToggle(props.kind, props.modelValue, stepName, Boolean(checked))
  emit('update:modelValue', next)
}
</script>

<template>
  <div class="space-y-2" data-testid="run-steps-selector">
    <div class="flex items-center justify-between gap-2">
      <span class="text-xs font-medium text-fg">Các bước thực hiện</span>
      <span class="text-[11px] text-fg-dim">Checkout luôn bắt buộc</span>
    </div>

    <div class="rounded-[2px] border border-border bg-bg-surface divide-y divide-border">
      <div
        v-for="(step, idx) in steps"
        :key="step.name"
        class="flex items-center justify-between gap-3 px-2.5 py-1.5"
        :data-testid="`run-step-row-${step.name}`"
      >
        <div class="flex items-center gap-2 min-w-0">
          <span class="text-[11px] font-mono text-fg-dim w-4 shrink-0">{{ idx + 1 }}.</span>
          <AppCheckbox
            :model-value="isStepChecked(step.name)"
            :disabled="isStepDisabled(step.name)"
            :label="step.label"
            :aria-label="`Chạy bước ${step.label}`"
            @update:model-value="onToggle(step.name, $event)"
          />
        </div>
        <span
          v-if="step.locked"
          class="text-[11px] font-mono text-fg-dim shrink-0"
        >
          Bắt buộc
        </span>
        <span
          v-else-if="!isStepChecked(step.name)"
          class="text-[11px] font-mono text-amber shrink-0"
        >
          Bỏ qua
        </span>
        <span
          v-else
          class="text-[11px] font-mono text-fg-muted shrink-0"
        >
          Chạy
        </span>
      </div>
    </div>

    <p class="text-[11px] text-fg-muted">
      Bỏ chọn một bước sẽ tự động bỏ qua các bước phía sau.
    </p>

    <div
      class="px-2.5 py-1.5 rounded-[2px] border text-xs font-mono"
      :class="
        hasSkipped
          ? 'border-amber/40 bg-amber/10 text-amber'
          : 'border-border bg-bg-surface text-fg-muted'
      "
      data-testid="run-steps-summary"
    >
      {{ summaryText }}
    </div>
  </div>
</template>
