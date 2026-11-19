<script setup lang="ts">
import { ref, computed, watch } from 'vue'
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
    confirmValue?: string
    confirmInputLabel?: string
    confirmInputPlaceholder?: string
  }>(),
  {
    title: 'Xác nhận hành động',
    message: 'Bạn có chắc chắn muốn thực hiện hành động này?',
    confirmText: 'Xác nhận',
    cancelText: 'Huỷ',
    loading: false,
    danger: true,
    confirmValue: undefined,
    confirmInputLabel: undefined,
    confirmInputPlaceholder: undefined
  }
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: boolean): void
  (e: 'confirm'): void
  (e: 'cancel'): void
}>()

const typedConfirm = ref('')

watch(
  () => props.modelValue,
  (open) => {
    if (open) {
      typedConfirm.value = ''
    }
  }
)

const isConfirmDisabled = computed(() => {
  if (props.loading) return true
  if (props.confirmValue && typedConfirm.value !== props.confirmValue) return true
  return false
})

function onCancel() {
  emit('update:modelValue', false)
  emit('cancel')
}

function onConfirm() {
  if (isConfirmDisabled.value) return
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
    <div class="flex items-start gap-3">
      <div
        v-if="danger"
        class="shrink-0 w-7 h-7 rounded-[2px] bg-[var(--danger-bg)] border border-danger/40 text-danger flex items-center justify-center"
        aria-hidden="true"
      >
        <AlertTriangle class="w-4 h-4" />
      </div>
      <div class="flex-1 min-w-0 text-[12.5px] text-fg leading-relaxed space-y-2.5">
        <div>
          <slot>{{ message }}</slot>
        </div>

        <div v-if="confirmValue" class="space-y-1.5 pt-1">
          <label class="block text-[11.5px] text-fg-muted">
            {{ confirmInputLabel || `Nhập "${confirmValue}" để xác nhận:` }}
          </label>
          <input
            v-model="typedConfirm"
            type="text"
            :placeholder="confirmInputPlaceholder || confirmValue"
            :aria-label="confirmInputLabel || `Nhập ${confirmValue} để xác nhận`"
            autocomplete="off"
            class="w-full h-7 px-2 text-[12px] rounded-[2px] bg-bg-input border border-border text-fg-strong placeholder:text-fg-dim focus:outline-none focus:border-border-focus"
          />
        </div>
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
        :disabled="isConfirmDisabled"
        @click="onConfirm"
      >
        {{ confirmText }}
      </AppButton>
    </template>
  </AppDialog>
</template>
