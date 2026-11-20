<script setup lang="ts">
import { ref, computed, watch, nextTick, onBeforeUnmount, type Component } from 'vue'
import { useFloating, type FloatingPlacement } from '../composables/useFloating'

export interface MenuItem {
  id: string
  label?: string
  icon?: Component
  shortcut?: string
  disabled?: boolean
  danger?: boolean
  separator?: boolean
}

const props = withDefaults(
  defineProps<{
    items: MenuItem[]
    placement?: FloatingPlacement
    label?: string
  }>(),
  {
    placement: 'bottom-end',
    label: 'Menu'
  }
)

const emit = defineEmits<{
  (e: 'select', item: MenuItem): void
}>()

const isOpen = ref(false)
const activeIndex = ref(-1)
const triggerRef = ref<HTMLElement | null>(null)
const menuRef = ref<HTMLElement | null>(null)
const itemRefs = ref<HTMLElement[]>([])

const { x, y, updatePosition } = useFloating(
  triggerRef,
  menuRef,
  isOpen,
  { placement: computed(() => props.placement), offset: 4 }
)

const enabledIndices = computed(() =>
  props.items
    .map((item, idx) => (!item.separator && !item.disabled ? idx : -1))
    .filter((idx) => idx !== -1)
)

function focusItemAt(idx: number) {
  if (idx < 0) return
  activeIndex.value = idx
  const el = itemRefs.value[idx]
  el?.focus()
}

function openMenu(focusFirst = true) {
  isOpen.value = true
  nextTick(() => {
    updatePosition()
    if (focusFirst && enabledIndices.value.length > 0) {
      focusItemAt(enabledIndices.value[0])
    } else {
      menuRef.value?.focus()
    }
  })
}

function closeMenu(restoreFocus = true) {
  isOpen.value = false
  activeIndex.value = -1
  if (restoreFocus) {
    const btn = triggerRef.value?.querySelector<HTMLElement>('button, [tabindex]')
    btn?.focus()
  }
}

function toggle() {
  if (isOpen.value) {
    closeMenu()
  } else {
    openMenu()
  }
}

function selectItem(item: MenuItem) {
  if (item.disabled || item.separator) return
  emit('select', item)
  closeMenu()
}

function onMenuKeydown(e: KeyboardEvent) {
  const indices = enabledIndices.value
  if (indices.length === 0) {
    if (e.key === 'Escape') {
      e.preventDefault()
      closeMenu()
    }
    return
  }

  const currentPos = indices.indexOf(activeIndex.value)

  if (e.key === 'ArrowDown') {
    e.preventDefault()
    const next = currentPos === -1 ? indices[0] : indices[(currentPos + 1) % indices.length]
    focusItemAt(next)
  } else if (e.key === 'ArrowUp') {
    e.preventDefault()
    const prev = currentPos <= 0 ? indices[indices.length - 1] : indices[currentPos - 1]
    focusItemAt(prev)
  } else if (e.key === 'Home') {
    e.preventDefault()
    focusItemAt(indices[0])
  } else if (e.key === 'End') {
    e.preventDefault()
    focusItemAt(indices[indices.length - 1])
  } else if (e.key === 'Escape' || e.key === 'Tab') {
    e.preventDefault()
    closeMenu(e.key === 'Escape')
  } else if (e.key === 'Enter' || e.key === ' ') {
    if (activeIndex.value >= 0) {
      e.preventDefault()
      selectItem(props.items[activeIndex.value])
    }
  } else if (e.key.length === 1 && !e.ctrlKey && !e.metaKey && !e.altKey) {
    // Typeahead
    const char = e.key.toLowerCase()
    const match = indices.find((idx) =>
      props.items[idx].label?.toLowerCase().startsWith(char)
    )
    if (match !== undefined) {
      e.preventDefault()
      focusItemAt(match)
    }
  }
}

function onDocumentClick(e: MouseEvent) {
  if (!isOpen.value) return
  const target = e.target as Node | null
  if (!target) return
  if (triggerRef.value?.contains(target) || menuRef.value?.contains(target)) return
  closeMenu(false)
}

watch(isOpen, (val) => {
  if (val) {
    document.addEventListener('mousedown', onDocumentClick)
  } else {
    document.removeEventListener('mousedown', onDocumentClick)
  }
})

onBeforeUnmount(() => {
  document.removeEventListener('mousedown', onDocumentClick)
})
</script>

<template>
  <span ref="triggerRef" class="inline-flex items-center">
    <slot name="trigger" :open="isOpen" :toggle="toggle" />
  </span>

  <Teleport to="body">
    <Transition name="d1-menu">
      <div
        v-if="isOpen"
        ref="menuRef"
        role="menu"
        :aria-label="label"
        tabindex="-1"
        :style="{ left: `${x}px`, top: `${y}px` }"
        class="fixed z-[65] min-w-[190px] py-1 rounded-[2px] bg-bg-elevated border border-border-strong shadow-[var(--shadow-overlay)] font-mono text-[12px] outline-none"
        @keydown="onMenuKeydown"
      >
        <template v-for="(item, idx) in items" :key="item.id">
          <div
            v-if="item.separator"
            role="separator"
            class="my-1 border-t border-border"
          />
          <button
            v-else
            :ref="(el) => { if (el) itemRefs[idx] = el as HTMLElement }"
            type="button"
            role="menuitem"
            :disabled="item.disabled"
            :tabindex="activeIndex === idx ? 0 : -1"
            :class="[
              'w-full flex items-center justify-between gap-3 px-2.5 py-1.5 text-left transition-colors duration-[80ms] outline-none',
              item.disabled
                ? 'opacity-45 cursor-not-allowed text-fg-dim'
                : item.danger
                  ? 'text-danger hover:bg-[var(--danger-bg)] focus:bg-[var(--danger-bg)] cursor-pointer'
                  : 'text-fg hover:bg-bg-hover focus:bg-bg-selected focus:text-fg-strong cursor-pointer'
            ]"
            @click="selectItem(item)"
            @mouseenter="activeIndex = idx"
          >
            <span class="inline-flex items-center gap-2 min-w-0 truncate">
              <component :is="item.icon" v-if="item.icon" class="w-3.5 h-3.5 shrink-0" aria-hidden="true" />
              <span class="truncate">{{ item.label }}</span>
            </span>
            <span v-if="item.shortcut" class="text-[10px] text-fg-dim shrink-0">
              {{ item.shortcut }}
            </span>
          </button>
        </template>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.d1-menu-enter-active,
.d1-menu-leave-active {
  transition: opacity var(--dur-snap, 80ms) var(--ease-snap, cubic-bezier(0.2, 0, 0, 1));
}
.d1-menu-enter-from,
.d1-menu-leave-to {
  opacity: 0;
}
</style>
