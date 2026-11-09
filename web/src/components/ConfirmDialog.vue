<script setup lang="ts">
import { AlertTriangle } from 'lucide-vue-next'
import AppDialog from './AppDialog.vue'
import AppButton from './AppButton.vue'

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    title?: string
    message?: string
    confirmText?: string
    cancelText?: string
    loading?: boolean
    danger?: boolean
  }>(),
  {
    title: 'Xác nhận hành động',
    message: 'Bạn có chắc chắn muốn thực hiện hành động này?',
    confirmText: 'Xác nhận',
    cancelText: 'Huỷ',
    loading: false,
    danger: true
  }
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: boolean): void
  (e: 'confirm'): void
  (e: 'cancel'): void
}>()

function onCancel() {
  emit('update:modelValue', false)
  emit('cancel')
}

function onConfirm() {
  emit('confirm')
}
</script>

<template>
  <AppDialog
    :model-value="modelValue"
    max-width="sm"
    :title="title"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <div class="flex items-start gap-4">
      <div
        v-if="danger"
        class="flex-shrink-0 w-10 h-10 rounded-full bg-danger/10 text-danger flex items-center justify-center"
        aria-hidden="true"
      >
        <AlertTriangle class="w-5 h-5" />
      </div>
      <div class="flex-1 text-sm text-muted-fg leading-relaxed">
        <slot>{{ message }}</slot>
      </div>
    </div>

    <template #footer>
      <AppButton
        variant="secondary"
        size="md"
        :disabled="loading"
        @click="onCancel"
      >
        {{ cancelText }}
      </AppButton>
      <AppButton
        :variant="danger ? 'danger' : 'primary'"
        size="md"
        :loading="loading"
        @click="onConfirm"
      >
        {{ confirmText }}
      </AppButton>
    </template>
  </AppDialog>
</template>
