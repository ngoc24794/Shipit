<script setup lang="ts">
import { computed, onMounted, onUnmounted } from 'vue'
import { X, Keyboard } from 'lucide-vue-next'
import { useShortcuts } from '../composables/useShortcuts'

const props = withDefaults(
  defineProps<{
    modelValue?: boolean
  }>(),
  {
    modelValue: undefined
  }
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: boolean): void
  (e: 'close'): void
}>()

const { groupedShortcuts, isOverlayOpen, closeOverlay } = useShortcuts()

const visible = computed(() => {
  return props.modelValue !== undefined ? props.modelValue : isOverlayOpen.value
})

function close() {
  closeOverlay()
  emit('update:modelValue', false)
  emit('close')
}

function handleKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape' && visible.value) {
    e.preventDefault()
    close()
  }
}

onMounted(() => {
  window.addEventListener('keydown', handleKeydown)
})

onUnmounted(() => {
  window.removeEventListener('keydown', handleKeydown)
})

function formatKeys(keys: string): string[] {
  return keys.split(' ').filter(Boolean)
}
</script>

<template>
  <Teleport to="body">
    <div
      v-if="visible"
      class="fixed inset-0 z-50 flex items-center justify-center p-4 bg-[var(--backdrop)]"
      data-testid="shortcuts-backdrop"
      @mousedown.self="close"
    >
      <div
        class="w-full max-w-[620px] max-h-[82vh] flex flex-col bg-bg-elevated border border-border-strong rounded-[2px] shadow-[var(--shadow-overlay)] overflow-hidden text-fg"
        role="dialog"
        aria-modal="true"
        aria-label="Phím tắt bàn phím"
        data-testid="shortcuts-dialog"
      >
        <div class="px-3 py-2 bg-bg-surface border-b border-border flex items-center justify-between gap-2 shrink-0">
          <div class="flex items-center gap-2 font-bold text-[13px] text-fg-strong">
            <Keyboard class="w-4 h-4 text-phosphor shrink-0" aria-hidden="true" />
            <span>Phím tắt bàn phím</span>
          </div>
          <button
            type="button"
            aria-label="Đóng bảng phím tắt"
            class="p-1 rounded-[2px] text-fg-muted hover:text-fg-strong hover:bg-bg-hover cursor-pointer focus-visible:outline-2 focus-visible:outline-border-focus"
            data-testid="shortcuts-close"
            @click="close"
          >
            <X class="w-3.5 h-3.5" aria-hidden="true" />
          </button>
        </div>

        <div class="p-3 overflow-y-auto space-y-4">
          <div
            v-if="groupedShortcuts.length === 0"
            class="py-6 text-center text-xs text-fg-muted"
          >
            Chưa có phím tắt nào được đăng ký.
          </div>

          <section
            v-for="group in groupedShortcuts"
            :key="group.scope"
            class="space-y-1.5"
            :data-testid="`shortcut-group-${group.scope}`"
          >
            <h2 class="text-[10px] font-bold uppercase tracking-wider text-fg-dim border-b border-border pb-1">
              {{ group.scope }}
            </h2>
            <div class="grid grid-cols-2 gap-x-4 gap-y-1">
              <div
                v-for="item in group.items"
                :key="item.id"
                class="flex items-center justify-between gap-2 py-1 text-xs min-w-0"
              >
                <span class="text-fg truncate" :title="item.label">{{ item.label }}</span>
                <span class="inline-flex items-center gap-1 shrink-0">
                  <kbd
                    v-for="(part, idx) in formatKeys(item.keys)"
                    :key="idx"
                    class="inline-flex items-center px-1.5 py-0.5 text-[10px] font-mono bg-bg-surface border border-border rounded-[2px] text-fg-strong whitespace-nowrap"
                  >
                    {{ part }}
                  </kbd>
                </span>
              </div>
            </div>
          </section>
        </div>

        <div class="px-3 py-1.5 bg-bg-surface border-t border-border flex items-center justify-between text-[11px] text-fg-muted shrink-0">
          <span>Nhấn <kbd class="px-1 py-0.5 bg-bg-elevated border border-border rounded-[2px] text-[10px]">?</kbd> để mở/đóng</span>
          <span><kbd class="px-1 py-0.5 bg-bg-elevated border border-border rounded-[2px] text-[10px]">Esc</kbd> để đóng</span>
        </div>
      </div>
    </div>
  </Teleport>
</template>
