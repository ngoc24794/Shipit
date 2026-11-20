<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { Play } from 'lucide-vue-next'
import type { StepName } from '../api/types'
import type { RunTargetKind } from '../utils/runSteps'
import AppDialog from './AppDialog.vue'
import AppButton from './AppButton.vue'
import RunStepsSelector from './RunStepsSelector.vue'

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    kind: RunTargetKind
    targetName: string
    loading?: boolean
    error?: string | null
  }>(),
  {
    loading: false,
    error: null
  }
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: boolean): void
  (e: 'confirm', skipSteps: StepName[]): void
}>()

const skipSteps = ref<StepName[]>([])
const runButtonRef = ref<InstanceType<typeof AppButton> | null>(null)

const dialogTitle = computed(() =>
  props.kind === 'job' ? `Chạy Job: ${props.targetName}` : `Chạy Pipeline: ${props.targetName}`
)

watch(
  () => props.modelValue,
  async (open) => {
    if (open) {
      skipSteps.value = []
      await nextTick()
      const el = runButtonRef.value?.$el as HTMLElement | undefined
      el?.focus?.()
    }
  }
)

function close() {
  if (props.loading) return
  emit('update:modelValue', false)
}

function handleConfirm() {
  if (props.loading) return
  emit('confirm', [...skipSteps.value])
}
</script>

<template>
  <AppDialog
    :model-value="modelValue"
    :title="dialogTitle"
    size="sm"
    :close-on-overlay="!loading"
    :close-on-esc="!loading"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <div class="space-y-3" @keydown.enter.prevent="handleConfirm">
      <div
        v-if="error"
        class="p-2.5 rounded-[2px] border border-red/40 bg-red/10 text-xs text-red"
        role="alert"
      >
        {{ error }}
      </div>

      <RunStepsSelector
        v-model="skipSteps"
        :kind="kind"
        :disabled="loading"
      />
    </div>

    <template #footer>
      <AppButton
        variant="ghost"
        size="sm"
        :disabled="loading"
        @click="close"
      >
        Huỷ
      </AppButton>
      <AppButton
        ref="runButtonRef"
        variant="primary"
        size="sm"
        :loading="loading"
        :icon="Play"
        data-testid="run-dialog-confirm"
        @click="handleConfirm"
      >
        Chạy
      </AppButton>
    </template>
  </AppDialog>
</template>
