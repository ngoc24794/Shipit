<script setup lang="ts">
import { ref, computed, nextTick, onBeforeUnmount, useId } from 'vue'
import { useFloating, type FloatingPlacement } from '../composables/useFloating'

const props = withDefaults(
  defineProps<{
    text?: string
    placement?: FloatingPlacement
    delay?: number
    disabled?: boolean
  }>(),
  {
    text: '',
    placement: 'top',
    delay: 150,
    disabled: false
  }
)

const tooltipId = useId()
const anchorRef = ref<HTMLElement | null>(null)
const floatingRef = ref<HTMLElement | null>(null)
const isOpen = ref(false)
let timer: ReturnType<typeof setTimeout> | null = null

const { x, y, updatePosition } = useFloating(anchorRef, floatingRef, isOpen, {
  placement: computed(() => props.placement),
  offset: 6
})

// Adapt placement option: useFloating accepts FloatingPlacement | Ref<FloatingPlacement>
// Let's keep it simple and pass props.placement via computed if needed, or update in show()
function clearTimer() {
  if (timer !== null) {
    clearTimeout(timer)
    timer = null
  }
}

async function openNow() {
  if (props.disabled) return
  isOpen.value = true
  await nextTick()
  updatePosition()
}

function scheduleOpen() {
  if (props.disabled) return
  clearTimer()
  if (props.delay <= 0) {
    openNow()
  } else {
    timer = setTimeout(openNow, props.delay)
  }
}

function close() {
  clearTimer()
  isOpen.value = false
}

function onKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape' && isOpen.value) {
    close()
  }
}

onBeforeUnmount(() => {
  clearTimer()
})
</script>

<template>
  <span
    ref="anchorRef"
    class="inline-flex items-center"
    :aria-describedby="isOpen ? tooltipId : undefined"
    @mouseenter="scheduleOpen"
    @mouseleave="close"
    @focusin="openNow"
    @focusout="close"
    @keydown="onKeydown"
  >
    <slot />
  </span>

  <Teleport to="body">
    <Transition name="d1-fade">
      <div
        v-if="isOpen && (text || $slots.content)"
        :id="tooltipId"
        ref="floatingRef"
        role="tooltip"
        :style="{ left: `${x}px`, top: `${y}px` }"
        class="fixed z-[70] px-2 py-1 rounded-[2px] bg-bg-surface border border-border-strong text-fg-strong text-[11px] font-mono shadow-[var(--shadow-overlay)] pointer-events-none whitespace-nowrap max-w-[280px] truncate"
      >
        <slot name="content">{{ text }}</slot>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.d1-fade-enter-active,
.d1-fade-leave-active {
  transition: opacity var(--dur-snap, 80ms) var(--ease-snap, cubic-bezier(0.2, 0, 0, 1));
}
.d1-fade-enter-from,
.d1-fade-leave-to {
  opacity: 0;
}
</style>
