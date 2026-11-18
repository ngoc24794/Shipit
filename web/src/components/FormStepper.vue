<script setup lang="ts">
import { ref } from 'vue'
import { AlertCircle } from 'lucide-vue-next'

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
  <nav aria-label="Các bước biểu mẫu" class="w-full overflow-x-auto py-1">
    <ol class="flex flex-nowrap items-center gap-2 min-w-max">
      <li
        v-for="(step, idx) in steps"
        :key="step.id"
        class="flex items-center gap-2 shrink-0"
      >
        <button
          ref="buttonRefs"
          type="button"
          :data-step-id="step.id"
          :data-step-status="step.status"
          :aria-current="idx === currentStep ? 'step' : undefined"
          :disabled="!isStepClickable(idx)"
          :class="[
            'inline-flex items-center gap-2 px-3 py-1.5 rounded-[6px] border text-xs transition-colors whitespace-nowrap',
            'focus-visible:outline-2 focus-visible:outline-ring focus-visible:outline-offset-2',
            isStepClickable(idx) ? 'cursor-pointer' : 'cursor-not-allowed opacity-55',
            idx === currentStep
              ? 'border-primary bg-primary/10 text-primary font-semibold'
              : step.status === 'error'
                ? 'border-danger/50 bg-danger/5 text-danger font-medium hover:bg-danger/10'
                : step.status === 'visited'
                  ? 'border-border bg-card text-fg hover:bg-muted'
                  : 'border-transparent bg-muted/60 text-muted-fg hover:text-fg hover:bg-muted'
          ]"
          @click="handleClick(idx)"
          @keydown="handleKeydown($event, idx)"
        >
          <span
            :class="[
              'w-5 h-5 rounded-full inline-flex items-center justify-center text-[11px] font-semibold shrink-0',
              idx === currentStep
                ? 'bg-primary text-on-primary'
                : step.status === 'error'
                  ? 'bg-danger text-white'
                  : step.status === 'visited'
                    ? 'bg-muted text-fg border border-border'
                    : 'bg-muted-fg/20 text-muted-fg'
            ]"
          >
            <AlertCircle v-if="step.status === 'error' && idx !== currentStep" class="w-3.5 h-3.5" aria-hidden="true" />
            <span v-else>{{ idx + 1 }}</span>
          </span>
          <span>{{ step.label }}</span>
        </button>

        <span
          v-if="idx < steps.length - 1"
          aria-hidden="true"
          class="w-4 h-px bg-border shrink-0"
        />
      </li>
    </ol>
  </nav>
</template>
