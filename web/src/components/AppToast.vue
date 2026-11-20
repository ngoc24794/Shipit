<script setup lang="ts">
import { X, Trash2 } from 'lucide-vue-next'
import { useToast, type ToastType } from '../composables/useToast'

const {
  toasts,
  entries,
  drawerOpen,
  remove,
  pause,
  resume,
  clear,
  closeDrawer
} = useToast()

function getGlyph(type: ToastType): string {
  switch (type) {
    case 'success':
      return '●'
    case 'error':
      return '✕'
    case 'warn':
    case 'warning':
      return '▲'
    case 'info':
    default:
      return 'ℹ'
  }
}

function getBorderClass(type: ToastType): string {
  switch (type) {
    case 'success':
      return 'border-l-phosphor'
    case 'error':
      return 'border-l-danger'
    case 'warn':
    case 'warning':
      return 'border-l-warn'
    case 'info':
    default:
      return 'border-l-accent'
  }
}

function getGlyphColorClass(type: ToastType): string {
  switch (type) {
    case 'success':
      return 'text-phosphor'
    case 'error':
      return 'text-danger'
    case 'warn':
    case 'warning':
      return 'text-warn'
    case 'info':
    default:
      return 'text-accent'
  }
}
</script>

<template>
  <!-- Floating D1 console line toast stack -->
  <div
    role="status"
    aria-live="polite"
    class="fixed right-3 bottom-[34px] z-[60] flex flex-col gap-1 max-w-[460px] w-full pointer-events-none font-mono"
  >
    <TransitionGroup name="d1-toast">
      <div
        v-for="toast in toasts"
        :key="toast.id"
        :data-toast-type="toast.type"
        :class="[
          'pointer-events-auto flex items-center gap-2 px-2.5 py-1.5 rounded-[2px] border border-border-strong border-l-[3px] bg-bg-elevated text-fg text-[11.5px] shadow-[var(--shadow-overlay)]',
          getBorderClass(toast.type)
        ]"
        @mouseenter="pause(toast.id)"
        @mouseleave="resume(toast.id)"
      >
        <span :class="['shrink-0 font-bold select-none', getGlyphColorClass(toast.type)]" aria-hidden="true">
          {{ getGlyph(toast.type) }}
        </span>

        <span class="flex-1 min-w-0 truncate" :title="toast.message">
          {{ toast.message }}
        </span>

        <button
          v-if="toast.action"
          type="button"
          class="shrink-0 px-1.5 py-0.5 text-[11px] font-semibold text-accent hover:underline cursor-pointer rounded-[2px] focus-visible:outline-2 focus-visible:outline-border-focus"
          @click="toast.action.onClick()"
        >
          [{{ toast.action.label }}]
        </button>

        <button
          type="button"
          aria-label="Đóng thông báo"
          class="shrink-0 text-fg-dim hover:text-fg-strong p-0.5 rounded-[2px] transition-colors cursor-pointer focus-visible:outline-2 focus-visible:outline-border-focus"
          @click="remove(toast.id)"
        >
          <X class="w-3.5 h-3.5" aria-hidden="true" />
        </button>
      </div>
    </TransitionGroup>
  </div>

  <!-- Persistent Console History Drawer (opened from status bar) -->
  <Transition name="d1-console">
    <section
      v-if="drawerOpen"
      aria-label="Nhật ký thông báo"
      class="fixed left-0 right-0 bottom-[26px] h-[200px] z-40 flex flex-col bg-bg-elevated border-t border-border-strong shadow-[0_-8px_24px_rgba(0,0,0,0.4)] font-mono"
    >
      <div class="shrink-0 h-7 px-3 bg-bg-surface border-b border-border flex items-center justify-between gap-2 text-[11px]">
        <div class="flex items-center gap-2 text-fg-strong font-bold">
          <span class="text-phosphor">$</span>
          <span>Nhật ký sự kiện ({{ entries.length }})</span>
        </div>
        <div class="flex items-center gap-1.5">
          <button
            type="button"
            class="inline-flex items-center gap-1 px-1.5 py-0.5 rounded-[2px] text-fg-muted hover:text-fg-strong hover:bg-bg-hover cursor-pointer"
            @click="clear"
          >
            <Trash2 class="w-3 h-3" aria-hidden="true" />
            <span>Xoá</span>
          </button>
          <button
            type="button"
            aria-label="Đóng nhật ký"
            class="inline-flex items-center justify-center w-5 h-5 rounded-[2px] text-fg-muted hover:text-fg-strong hover:bg-bg-hover cursor-pointer"
            @click="closeDrawer"
          >
            <X class="w-3.5 h-3.5" aria-hidden="true" />
          </button>
        </div>
      </div>

      <div class="flex-1 min-h-0 overflow-y-auto px-3 py-1.5 bg-bg-input text-[11.5px] space-y-0.5">
        <div v-if="entries.length === 0" class="text-fg-dim py-4 text-center">
          Chưa có sự kiện nào được ghi nhận.
        </div>
        <div
          v-for="entry in entries"
          :key="entry.id"
          class="flex items-center gap-2 py-0.5 px-1 rounded-[1px] hover:bg-bg-hover"
        >
          <span class="text-fg-dim shrink-0 tabular-nums">{{ entry.timestamp }}</span>
          <span :class="['shrink-0 font-bold', getGlyphColorClass(entry.type)]" aria-hidden="true">
            {{ getGlyph(entry.type) }}
          </span>
          <span class="flex-1 min-w-0 truncate text-fg" :title="entry.message">
            {{ entry.message }}
          </span>
        </div>
      </div>
    </section>
  </Transition>
</template>

<style scoped>
.d1-toast-enter-active {
  animation: d1ToastStep var(--dur-pane, 140ms) var(--ease-step, steps(4, end));
}
.d1-toast-leave-active {
  transition: opacity var(--dur-snap, 80ms) var(--ease-snap, cubic-bezier(0.2, 0, 0, 1));
}
.d1-toast-leave-to {
  opacity: 0;
}

.d1-console-enter-active,
.d1-console-leave-active {
  transition: transform var(--dur-fast, 110ms) var(--ease-snap, cubic-bezier(0.2, 0, 0, 1)),
    opacity var(--dur-fast, 110ms) var(--ease-snap, cubic-bezier(0.2, 0, 0, 1));
}
.d1-console-enter-from,
.d1-console-leave-to {
  opacity: 0;
  transform: translateY(12px);
}

@keyframes d1ToastStep {
  from {
    opacity: 0;
    transform: translateX(14px);
  }
  to {
    opacity: 1;
    transform: translateX(0);
  }
}
</style>
