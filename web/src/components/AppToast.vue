<script setup lang="ts">
import { CheckCircle2, AlertCircle, Info, X } from 'lucide-vue-next'
import { useToast } from '../composables/useToast'

const { toasts, remove } = useToast()
</script>

<template>
  <div
    role="status"
    aria-live="polite"
    class="fixed bottom-4 right-4 z-50 flex flex-col gap-2 max-w-sm w-full pointer-events-none"
  >
    <div
      v-for="toast in toasts"
      :key="toast.id"
      :class="[
        'pointer-events-auto flex items-start gap-3 p-3.5 rounded-[8px] border shadow-lg transition-all animate-in slide-in-from-bottom-2 duration-150',
        toast.type === 'success' ? 'bg-card text-fg border-primary/40' : '',
        toast.type === 'error' ? 'bg-card text-fg border-danger/40' : '',
        toast.type === 'info' ? 'bg-card text-fg border-info/40' : ''
      ]"
    >
      <div class="flex-shrink-0 mt-0.5">
        <CheckCircle2
          v-if="toast.type === 'success'"
          class="w-5 h-5 text-primary"
          aria-hidden="true"
        />
        <AlertCircle
          v-else-if="toast.type === 'error'"
          class="w-5 h-5 text-danger"
          aria-hidden="true"
        />
        <Info
          v-else
          class="w-5 h-5 text-info"
          aria-hidden="true"
        />
      </div>

      <div class="flex-1 text-sm font-medium leading-snug">
        {{ toast.message }}
      </div>

      <button
        type="button"
        aria-label="Đóng thông báo"
        class="flex-shrink-0 text-muted-fg hover:text-fg rounded p-1 transition-colors cursor-pointer"
        @click="remove(toast.id)"
      >
        <X class="w-4 h-4" aria-hidden="true" />
      </button>
    </div>
  </div>
</template>
