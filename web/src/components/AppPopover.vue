<script setup lang="ts">
import { ref, computed, watch, nextTick, onBeforeUnmount } from 'vue'
import { useFloating, type FloatingPlacement } from '../composables/useFloating'

const props = withDefaults(
  defineProps<{
    modelValue?: boolean
    placement?: FloatingPlacement
    closeOnClickOutside?: boolean
    closeOnEsc?: boolean
  }>(),
  {
    modelValue: undefined,
    placement: 'bottom-start',
    closeOnClickOutside: true,
    closeOnEsc: true
  }
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: boolean): void
  (e: 'open'): void
  (e: 'close'): void
}>()

const internalOpen = ref(false)
const isOpen = computed({
  get: () => (props.modelValue !== undefined ? props.modelValue : internalOpen.value),
  set: (val: boolean) => {
    internalOpen.value = val
    emit('update:modelValue', val)
    if (val) emit('open')
    else emit('close')
  }
})

const triggerRef = ref<HTMLElement | null>(null)
const popoverRef = ref<HTMLElement | null>(null)

const { x, y, updatePosition } = useFloating(
  triggerRef,
  popoverRef,
  isOpen,
  { placement: computed(() => props.placement), offset: 4 }
)

function open() {
  isOpen.value = true
}

function close() {
  isOpen.value = false
}

function toggle() {
  isOpen.value = !isOpen.value
}

function onDocumentClick(e: MouseEvent) {
  if (!isOpen.value || !props.closeOnClickOutside) return
  const target = e.target as Node | null
  if (!target) return
  if (triggerRef.value?.contains(target) || popoverRef.value?.contains(target)) return
  close()
}

function onDocumentKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape' && isOpen.value && props.closeOnEsc) {
    e.stopPropagation()
    close()
    triggerRef.value?.querySelector<HTMLElement>('button, [tabindex]')?.focus()
  }
}

watch(isOpen, async (val) => {
  if (val) {
    await nextTick()
    updatePosition()
    document.addEventListener('mousedown', onDocumentClick)
    document.addEventListener('keydown', onDocumentKeydown)
  } else {
    document.removeEventListener('mousedown', onDocumentClick)
    document.removeEventListener('keydown', onDocumentKeydown)
  }
})

onBeforeUnmount(() => {
  document.removeEventListener('mousedown', onDocumentClick)
  document.removeEventListener('keydown', onDocumentKeydown)
})
</script>

<template>
  <span ref="triggerRef" class="inline-flex items-center">
    <slot name="trigger" :open="isOpen" :toggle="toggle" :close="close" />
  </span>

  <Teleport to="body">
    <Transition name="d1-pop">
      <div
        v-if="isOpen"
        ref="popoverRef"
        :style="{ left: `${x}px`, top: `${y}px` }"
        class="fixed z-[65] min-w-[180px] rounded-[2px] bg-bg-elevated border border-border-strong shadow-[var(--shadow-overlay)] p-2.5 font-mono text-[12px] text-fg"
      >
        <slot :close="close" :open="open" />
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.d1-pop-enter-active,
.d1-pop-leave-active {
  transition: opacity var(--dur-snap, 80ms) var(--ease-snap, cubic-bezier(0.2, 0, 0, 1)),
    transform var(--dur-snap, 80ms) var(--ease-snap, cubic-bezier(0.2, 0, 0, 1));
}
.d1-pop-enter-from,
.d1-pop-leave-to {
  opacity: 0;
  transform: translateY(-2px);
}
</style>
