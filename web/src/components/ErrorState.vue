<script setup lang="ts">
import { RotateCcw } from 'lucide-vue-next'
import AppButton from './AppButton.vue'

withDefaults(
  defineProps<{
    title?: string
    message?: string
    retryLabel?: string
    retrying?: boolean
    showRetry?: boolean
  }>(),
  {
    title: 'Đã xảy ra lỗi',
    message: 'Không thể tải dữ liệu. Vui lòng thử lại.',
    retryLabel: 'Thử lại',
    retrying: false,
    showRetry: true
  }
)

const emit = defineEmits<{
  (e: 'retry'): void
}>()
</script>

<template>
  <div
    role="alert"
    class="flex flex-col items-center justify-center p-6 text-center rounded-[2px] border border-danger/40 bg-[var(--danger-bg)]/30 my-2 font-mono"
  >
    <div class="flex items-center justify-center w-9 h-9 rounded-[2px] bg-[var(--danger-bg)] border border-danger/40 text-danger text-[15px] font-bold mb-3" aria-hidden="true">
      ✕
    </div>

    <h3 class="text-[13px] font-bold text-fg-strong mb-1">
      <slot name="title">{{ title }}</slot>
    </h3>

    <p class="prose text-[12.5px] text-fg-muted max-w-md mb-4">
      <slot>{{ message }}</slot>
    </p>

    <div v-if="showRetry || $slots.action" class="flex items-center gap-2">
      <slot name="action">
        <AppButton
          type="button"
          variant="secondary"
          size="sm"
          :loading="retrying"
          @click="emit('retry')"
        >
          <RotateCcw class="w-3.5 h-3.5" aria-hidden="true" />
          <span>{{ retryLabel }}</span>
        </AppButton>
      </slot>
    </div>
  </div>
</template>
