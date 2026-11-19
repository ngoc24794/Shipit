<script setup lang="ts">
import { ref } from 'vue'

export type WizardStepStatus = 'pending' | 'visited' | 'current' | 'error'

export interface WizardStep {
  id: string
  label: string
  status: WizardStepStatus
}

const props = withDefaults(
  defineProps<{
    steps: WizardStep[]
    currentStep: number
    canNavigateTo?: (index: number) => boolean
  }>(),
  {
    canNavigateTo: () => true
  }
)

const emit = defineEmits<{
  (e: 'select', index: number): void
}>()

const buttonRefs = ref<HTMLButtonElement[]>([])

function isStepClickable(index: number): boolean {
  return props.canNavigateTo(index)
}

function handleClick(index: number) {
  if (!isStepClickable(index)) return
  emit('select', index)
}

function handleKeydown(event: KeyboardEvent, index: number) {
  let targetIndex = -1

  if (event.key === 'ArrowRight') {
    for (let i = index + 1; i < props.steps.length; i++) {
      if (isStepClickable(i)) {
        targetIndex = i
        break
      }
    }
  } else if (event.key === 'ArrowLeft') {
    for (let i = index - 1; i >= 0; i--) {
      if (isStepClickable(i)) {
        targetIndex = i
        break
      }
    }
  } else if (event.key === 'Home') {
    for (let i = 0; i < props.steps.length; i++) {
      if (isStepClickable(i)) {
        targetIndex = i
        break
      }
    }
  } else if (event.key === 'End') {
    for (let i = props.steps.length - 1; i >= 0; i--) {
      if (isStepClickable(i)) {
        targetIndex = i
        break
      }
    }
  }

  if (targetIndex !== -1) {
    event.preventDefault()
    buttonRefs.value[targetIndex]?.focus()
    emit('select', targetIndex)
  }
}
</script>

<template>
  <nav
    aria-label="Các bước biểu mẫu"
    class="w-full flex items-center justify-between gap-2 px-3 py-2 bg-bg-surface border-b border-border overflow-x-auto font-mono"
  >
    <ol class="flex flex-nowrap items-center gap-1.5 min-w-max">
      <li
        v-for="(step, idx) in steps"
        :key="step.id"
        class="flex items-center gap-1.5 shrink-0"
      >
        <button
          ref="buttonRefs"
          type="button"
          :data-step-id="step.id"
          :data-step-status="step.status"
          :aria-current="idx === currentStep ? 'step' : undefined"
          :disabled="!isStepClickable(idx)"
          :class="[
            'inline-flex items-center gap-1.5 px-2.5 py-1 rounded-[2px] border text-[12px] transition-colors duration-[80ms] whitespace-nowrap',
            'focus-visible:outline-2 focus-visible:outline-border-focus focus-visible:outline-offset-2',
            isStepClickable(idx) ? 'cursor-pointer' : 'cursor-not-allowed opacity-50',
            idx === currentStep
              ? 'border-phosphor bg-[var(--phosphor-bg)] text-phosphor font-bold'
              : step.status === 'error'
                ? 'border-danger bg-[var(--danger-bg)]/40 text-danger font-semibold'
                : step.status === 'visited'
                  ? 'border-border-strong bg-bg-elevated text-fg hover:bg-bg-hover'
                  : 'border-border bg-bg-elevated text-fg-muted hover:text-fg-strong hover:bg-bg-hover'
          ]"
          @click="handleClick(idx)"
          @keydown="handleKeydown($event, idx)"
        >
          <span class="text-[11px] font-bold shrink-0">
            <template v-if="step.status === 'error' && idx !== currentStep">!</template>
            <template v-else>{{ idx + 1 }}.</template>
          </span>
          <span>{{ step.label }}</span>
        </button>
      </li>
    </ol>

    <span class="text-[10.5px] text-fg-dim whitespace-nowrap shrink-0 select-none">
      [←/→] chuyển bước · Lưu ở bước cuối
    </span>
  </nav>
</template>
