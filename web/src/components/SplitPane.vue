<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'

const props = withDefaults(
  defineProps<{
    storageKey?: string
    defaultSize?: number
    minSize?: number
    maxSize?: number
    ariaLabel?: string
  }>(),
  {
    storageKey: '',
    defaultSize: 390,
    minSize: 280,
    maxSize: 580,
    ariaLabel: 'Điều chỉnh độ rộng bảng điều khiển'
  }
)

const emit = defineEmits<{
  (e: 'resize', size: number): void
}>()

function clamp(val: number): number {
  return Math.min(props.maxSize, Math.max(props.minSize, val))
}

function loadInitialSize(): number {
  if (!props.storageKey) return clamp(props.defaultSize)
  try {
    const raw = window.localStorage.getItem(props.storageKey)
    if (raw !== null) {
      const parsed = Number(raw)
      if (!Number.isNaN(parsed)) {
        return clamp(parsed)
      }
    }
  } catch {
    // Ignore localStorage access errors
  }
  return clamp(props.defaultSize)
}

function persistSize(size: number): void {
  if (!props.storageKey) return
  try {
    window.localStorage.setItem(props.storageKey, String(size))
  } catch {
    // Ignore localStorage access errors
  }
}

const leftSize = ref<number>(loadInitialSize())
const isDragging = ref(false)
let startX = 0
let startSize = 0

function updateSize(next: number): void {
  const clamped = clamp(next)
  leftSize.value = clamped
  persistSize(clamped)
  emit('resize', clamped)
}

function onMouseMove(e: MouseEvent): void {
  if (!isDragging.value) return
  updateSize(startSize + (e.clientX - startX))
}

function onMouseUp(): void {
  if (!isDragging.value) return
  isDragging.value = false
  window.removeEventListener('mousemove', onMouseMove)
  window.removeEventListener('mouseup', onMouseUp)
}

function startDrag(e: MouseEvent): void {
  e.preventDefault()
  isDragging.value = true
  startX = e.clientX
  startSize = leftSize.value
  window.addEventListener('mousemove', onMouseMove)
  window.addEventListener('mouseup', onMouseUp)
}

function handleDividerKeydown(e: KeyboardEvent): void {
  const step = e.shiftKey ? 48 : 16
  if (e.key === 'ArrowLeft') {
    e.preventDefault()
    updateSize(leftSize.value - step)
  } else if (e.key === 'ArrowRight') {
    e.preventDefault()
    updateSize(leftSize.value + step)
  } else if (e.key === 'Home') {
    e.preventDefault()
    updateSize(props.minSize)
  } else if (e.key === 'End') {
    e.preventDefault()
    updateSize(props.maxSize)
  }
}

onMounted(() => {
  leftSize.value = loadInitialSize()
})

onBeforeUnmount(() => {
  window.removeEventListener('mousemove', onMouseMove)
  window.removeEventListener('mouseup', onMouseUp)
})
</script>

<template>
  <div
    class="flex-1 min-h-0 flex border border-border rounded-[2px] bg-bg-elevated overflow-hidden"
    data-testid="split-pane"
  >
    <div
      class="shrink-0 flex flex-col min-h-0 overflow-y-auto bg-bg-elevated"
      :style="{ width: `${leftSize}px`, minWidth: `${minSize}px`, maxWidth: `${maxSize}px` }"
      data-testid="split-pane-left"
    >
      <slot name="left" />
    </div>

    <div
      role="separator"
      tabindex="0"
      aria-orientation="vertical"
      :aria-label="ariaLabel"
      :aria-valuenow="leftSize"
      :aria-valuemin="minSize"
      :aria-valuemax="maxSize"
      :class="[
        'w-[5px] shrink-0 cursor-col-resize relative transition-colors focus-visible:outline-2 focus-visible:outline-border-focus',
        isDragging ? 'bg-accent' : 'bg-border hover:bg-accent'
      ]"
      title="Kéo hoặc dùng phím mũi tên trái/phải để thay đổi kích thước"
      data-testid="split-pane-divider"
      @mousedown="startDrag"
      @keydown="handleDividerKeydown"
    />

    <div
      class="flex-1 min-w-0 min-h-0 flex flex-col overflow-hidden bg-bg-elevated"
      data-testid="split-pane-right"
    >
      <slot name="right" />
    </div>
  </div>
</template>
