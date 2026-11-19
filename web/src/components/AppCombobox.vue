<script setup lang="ts">
import { ref, computed, watch, nextTick, onBeforeUnmount, type CSSProperties } from 'vue'
import { ChevronDown, X, Check, Plus } from 'lucide-vue-next'
import { useFloating } from '../composables/useFloating'
import AppTag from './AppTag.vue'
import AppSpinner from './AppSpinner.vue'

export interface ComboboxOption {
  label: string
  value: string | number
  disabled?: boolean
  group?: string
}

export interface ComboboxGroup {
  label: string
  options: ComboboxOption[]
}

const props = withDefaults(
  defineProps<{
    modelValue?: string | number | Array<string | number> | null
    options?: ComboboxOption[]
    groups?: ComboboxGroup[]
    id?: string
    name?: string
    placeholder?: string
    disabled?: boolean
    error?: boolean | string
    ariaDescribedby?: string
    searchable?: boolean
    clearable?: boolean
    multiple?: boolean
    creatable?: boolean
    loading?: boolean
    emptyText?: string
    size?: 'sm' | 'md'
  }>(),
  {
    modelValue: null,
    options: () => [],
    groups: undefined,
    id: undefined,
    name: undefined,
    placeholder: 'Chọn...',
    disabled: false,
    error: false,
    ariaDescribedby: undefined,
    searchable: true,
    clearable: false,
    multiple: false,
    creatable: false,
    loading: false,
    emptyText: 'Không có kết quả',
    size: 'md'
  }
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: string | number | Array<string | number> | null): void
  (e: 'change', value: string | number | Array<string | number> | null): void
  (e: 'create', value: string): void
}>()

const uid = Math.random().toString(36).slice(2, 8)
const listboxId = `combobox-listbox-${uid}`

const triggerRef = ref<HTMLElement | null>(null)
const popupRef = ref<HTMLElement | null>(null)
const searchInputRef = ref<HTMLInputElement | null>(null)
const optionRefs = ref<Record<number, HTMLElement | null>>({})

const isOpen = ref(false)
const searchQuery = ref('')
const activeIndex = ref(-1)
let typeAheadBuffer = ''
let typeAheadTimer: ReturnType<typeof setTimeout> | null = null

const { x: floatingX, y: floatingY, updatePosition } = useFloating(
  triggerRef,
  popupRef,
  isOpen,
  { placement: 'bottom-start', offset: 4, flip: true }
)

const popupStyle = computed<CSSProperties>(() => {
  const triggerWidth = triggerRef.value?.getBoundingClientRect().width
  return {
    position: 'fixed',
    top: `${floatingY.value}px`,
    left: `${floatingX.value}px`,
    zIndex: 9999,
    maxHeight: '260px',
    ...(triggerWidth ? { width: `${triggerWidth}px`, minWidth: `${triggerWidth}px` } : {})
  }
})

function handleOutsidePointerDown(event: MouseEvent) {
  const target = event.target as Node | null
  if (!target) return
  if (triggerRef.value?.contains(target)) return
  if (popupRef.value?.contains(target)) return
  closePopup()
}

function handleOutsideScroll(event: Event) {
  const target = event.target as Node | null
  if (target && popupRef.value && (popupRef.value === target || popupRef.value.contains(target))) {
    return
  }
  closePopup()
}

watch(isOpen, (open) => {
  if (open) {
    nextTick(() => updatePosition())
    document.addEventListener('mousedown', handleOutsidePointerDown, true)
    window.addEventListener('scroll', handleOutsideScroll, true)
  } else {
    document.removeEventListener('mousedown', handleOutsidePointerDown, true)
    window.removeEventListener('scroll', handleOutsideScroll, true)
  }
})

onBeforeUnmount(() => {
  document.removeEventListener('mousedown', handleOutsidePointerDown, true)
  window.removeEventListener('scroll', handleOutsideScroll, true)
})

const allFlatOptions = computed<ComboboxOption[]>(() => {
  if (props.groups && props.groups.length > 0) {
    const out: ComboboxOption[] = []
    for (const g of props.groups) {
      for (const opt of g.options) {
        out.push({ ...opt, group: g.label })
      }
    }
    return out
  }
  return props.options || []
})

const filteredOptions = computed<ComboboxOption[]>(() => {
  const q = searchQuery.value.trim().toLowerCase()
  if (!q) return allFlatOptions.value
  return allFlatOptions.value.filter(
    (o) => o.label.toLowerCase().includes(q) || String(o.value).toLowerCase().includes(q)
  )
})

const canCreate = computed(() => {
  if (!props.creatable) return false
  const q = searchQuery.value.trim()
  if (!q) return false
  return !allFlatOptions.value.some(
    (o) => o.label.toLowerCase() === q.toLowerCase() || String(o.value).toLowerCase() === q.toLowerCase()
  )
})

const groupedRenderList = computed(() => {
  const list = filteredOptions.value
  const hasGroups = list.some((o) => !!o.group)
  if (!hasGroups) {
    return [{ group: '', items: list.map((opt, idx) => ({ opt, idx })) }]
  }
  const map = new Map<string, Array<{ opt: ComboboxOption; idx: number }>>()
  list.forEach((opt, idx) => {
    const g = opt.group || ''
    if (!map.has(g)) map.set(g, [])
    map.get(g)!.push({ opt, idx })
  })
  return Array.from(map.entries()).map(([group, items]) => ({ group, items }))
})

const selectedValues = computed<Array<string | number>>(() => {
  if (props.multiple) {
    return Array.isArray(props.modelValue) ? props.modelValue : []
  }
  if (props.modelValue === null || props.modelValue === undefined || props.modelValue === '') {
    return []
  }
  return [props.modelValue as string | number]
})

function isSelected(val: string | number): boolean {
  return selectedValues.value.some((v) => String(v) === String(val))
}

const selectedSingleOption = computed(() => {
  if (props.multiple || selectedValues.value.length === 0) return null
  const val = selectedValues.value[0]
  return allFlatOptions.value.find((o) => String(o.value) === String(val)) || {
    label: String(val),
    value: val
  }
})

const selectedMultiOptions = computed(() => {
  if (!props.multiple) return []
  return selectedValues.value.map((val) => {
    return (
      allFlatOptions.value.find((o) => String(o.value) === String(val)) || {
        label: String(val),
        value: val
      }
    )
  })
})

const hasSelection = computed(() => selectedValues.value.length > 0)

function getOptionId(idx: number): string {
  return `${listboxId}-opt-${idx}`
}

const activeDescendantId = computed(() => {
  if (!isOpen.value || activeIndex.value < 0) return undefined
  return getOptionId(activeIndex.value)
})

function findFirstEnabledIndex(): number {
  return filteredOptions.value.findIndex((o) => !o.disabled)
}

function findNextEnabledIndex(from: number, step: number): number {
  const list = filteredOptions.value
  if (list.length === 0) return -1
  let idx = from
  for (let i = 0; i < list.length; i++) {
    idx = (idx + step + list.length) % list.length
    if (!list[idx].disabled) return idx
  }
  return -1
}

function scrollActiveIntoView() {
  nextTick(() => {
    const el = optionRefs.value[activeIndex.value]
    if (el && typeof el.scrollIntoView === 'function') {
      el.scrollIntoView({ block: 'nearest' })
    }
  })
}

function openPopup() {
  if (props.disabled) return
  if (isOpen.value) return
  isOpen.value = true
  searchQuery.value = ''
  const selectedIdx = filteredOptions.value.findIndex((o) => !o.disabled && isSelected(o.value))
  activeIndex.value = selectedIdx >= 0 ? selectedIdx : findFirstEnabledIndex()
  nextTick(() => {
    if (props.searchable) {
      searchInputRef.value?.focus()
    }
    scrollActiveIntoView()
  })
}

function closePopup() {
  if (!isOpen.value) return
  isOpen.value = false
  searchQuery.value = ''
  activeIndex.value = -1
}

function togglePopup() {
  if (isOpen.value) closePopup()
  else openPopup()
}

function selectOption(opt: ComboboxOption) {
  if (opt.disabled) return
  if (props.multiple) {
    const current = [...selectedValues.value]
    const existingIdx = current.findIndex((v) => String(v) === String(opt.value))
    if (existingIdx >= 0) {
      current.splice(existingIdx, 1)
    } else {
      current.push(opt.value)
    }
    emit('update:modelValue', current)
    emit('change', current)
    searchQuery.value = ''
    if (props.searchable) searchInputRef.value?.focus()
  } else {
    emit('update:modelValue', opt.value)
    emit('change', opt.value)
    closePopup()
    triggerRef.value?.focus()
  }
}

function createFromQuery() {
  const val = searchQuery.value.trim()
  if (!val || !props.creatable) return
  emit('create', val)
  if (props.multiple) {
    const current = [...selectedValues.value]
    if (!current.some((v) => String(v) === val)) {
      current.push(val)
    }
    emit('update:modelValue', current)
    emit('change', current)
    searchQuery.value = ''
  } else {
    emit('update:modelValue', val)
    emit('change', val)
    closePopup()
    triggerRef.value?.focus()
  }
}

function removeChip(val: string | number) {
  if (props.disabled || !props.multiple) return
  const next = selectedValues.value.filter((v) => String(v) !== String(val))
  emit('update:modelValue', next)
  emit('change', next)
}

function clearSelection() {
  if (props.disabled) return
  const next = props.multiple ? [] : ''
  emit('update:modelValue', next)
  emit('change', next)
}

function handleTypeAhead(char: string) {
  typeAheadBuffer += char.toLowerCase()
  if (typeAheadTimer) clearTimeout(typeAheadTimer)
  typeAheadTimer = setTimeout(() => {
    typeAheadBuffer = ''
  }, 500)

  const idx = filteredOptions.value.findIndex(
    (o) => !o.disabled && o.label.toLowerCase().startsWith(typeAheadBuffer)
  )
  if (idx >= 0) {
    activeIndex.value = idx
    scrollActiveIntoView()
  }
}

function onKeydown(event: KeyboardEvent) {
  if (props.disabled) return

  if (!isOpen.value) {
    if (
      event.key === 'ArrowDown' ||
      event.key === 'ArrowUp' ||
      event.key === 'Enter' ||
      event.key === ' '
    ) {
      event.preventDefault()
      openPopup()
      return
    }
    if (event.key.length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey) {
      openPopup()
      if (!props.searchable) {
        handleTypeAhead(event.key)
      }
    }
    return
  }

  switch (event.key) {
    case 'ArrowDown':
      event.preventDefault()
      activeIndex.value = findNextEnabledIndex(activeIndex.value, 1)
      scrollActiveIntoView()
      break
    case 'ArrowUp':
      event.preventDefault()
      activeIndex.value = findNextEnabledIndex(
        activeIndex.value < 0 ? 0 : activeIndex.value,
        -1
      )
      scrollActiveIntoView()
      break
    case 'PageDown':
      event.preventDefault()
      if (filteredOptions.value.length > 0) {
        let target = Math.min(filteredOptions.value.length - 1, Math.max(0, activeIndex.value) + 10)
        while (target > 0 && filteredOptions.value[target]?.disabled) target--
        if (!filteredOptions.value[target]?.disabled) activeIndex.value = target
        scrollActiveIntoView()
      }
      break
    case 'PageUp':
      event.preventDefault()
      if (filteredOptions.value.length > 0) {
        let target = Math.max(0, activeIndex.value - 10)
        while (target < filteredOptions.value.length - 1 && filteredOptions.value[target]?.disabled) {
          target++
        }
        if (!filteredOptions.value[target]?.disabled) activeIndex.value = target
        scrollActiveIntoView()
      }
      break
    case 'Home':
      event.preventDefault()
      activeIndex.value = findFirstEnabledIndex()
      scrollActiveIntoView()
      break
    case 'End':
      event.preventDefault()
      for (let i = filteredOptions.value.length - 1; i >= 0; i--) {
        if (!filteredOptions.value[i].disabled) {
          activeIndex.value = i
          break
        }
      }
      scrollActiveIntoView()
      break
    case 'Enter':
      event.preventDefault()
      if (activeIndex.value >= 0 && filteredOptions.value[activeIndex.value]) {
        selectOption(filteredOptions.value[activeIndex.value])
      } else if (canCreate.value) {
        createFromQuery()
      }
      break
    case 'Escape':
      event.preventDefault()
      closePopup()
      triggerRef.value?.focus()
      break
    case 'Tab':
      closePopup()
      break
    case 'Backspace':
      if (props.multiple && !searchQuery.value && selectedValues.value.length > 0) {
        const last = selectedValues.value[selectedValues.value.length - 1]
        removeChip(last)
      }
      break
    default:
      if (!props.searchable && event.key.length === 1 && !event.ctrlKey && !event.metaKey) {
        handleTypeAhead(event.key)
      }
  }
}

watch(filteredOptions, () => {
  if (isOpen.value) {
    activeIndex.value = findFirstEnabledIndex()
  }
})
</script>

<template>
  <div class="relative w-full">
    <input v-if="name && !multiple" type="hidden" :name="name" :value="modelValue ?? ''" />

    <div
      ref="triggerRef"
      :id="id"
      role="combobox"
      :tabindex="disabled ? -1 : 0"
      :aria-expanded="isOpen ? 'true' : 'false'"
      :aria-controls="listboxId"
      :aria-activedescendant="activeDescendantId"
      aria-haspopup="listbox"
      :aria-disabled="disabled ? 'true' : undefined"
      :aria-invalid="!!error ? 'true' : undefined"
      :aria-describedby="ariaDescribedby"
      :class="[
        'flex items-center justify-between gap-1.5 w-full rounded-[2px] bg-bg-input text-fg-strong border font-mono select-none transition-colors duration-[80ms]',
        multiple ? 'min-h-7 py-0.5 px-1.5' : size === 'sm' ? 'h-6 px-2 text-[11.5px]' : 'h-7 px-2 text-xs',
        disabled ? 'opacity-50 bg-bg-surface cursor-not-allowed' : 'cursor-pointer',
        error
          ? 'border-danger focus-visible:outline-2 focus-visible:outline-danger'
          : isOpen
            ? 'border-border-focus'
            : 'border-border hover:border-border-strong focus-visible:outline-2 focus-visible:outline-border-focus'
      ]"
      @click="togglePopup"
      @keydown="onKeydown"
    >
      <div class="flex-1 min-w-0 flex items-center flex-wrap gap-1 overflow-hidden">
        <template v-if="multiple && selectedMultiOptions.length > 0">
          <AppTag
            v-for="item in selectedMultiOptions"
            :key="item.value"
            size="sm"
            variant="phosphor"
            removable
            :disabled="disabled"
            @remove="removeChip(item.value)"
          >
            {{ item.label }}
          </AppTag>
        </template>
        <span v-else-if="!multiple && selectedSingleOption" class="truncate text-fg-strong">
          {{ selectedSingleOption.label }}
        </span>
        <span v-else class="truncate text-fg-dim">
          {{ placeholder }}
        </span>
      </div>

      <div class="inline-flex items-center gap-1 shrink-0 text-fg-muted">
        <button
          v-if="clearable && hasSelection && !disabled"
          type="button"
          aria-label="Xoá lựa chọn"
          class="inline-flex items-center justify-center w-4 h-4 rounded-[2px] hover:bg-bg-hover hover:text-fg-strong cursor-pointer"
          @click.stop="clearSelection"
        >
          <X class="w-3 h-3" aria-hidden="true" />
        </button>
        <ChevronDown
          :class="['w-3.5 h-3.5 transition-transform duration-[80ms]', isOpen ? 'rotate-180 text-accent' : '']"
          aria-hidden="true"
        />
      </div>
    </div>

    <Teleport to="body">
      <div
        v-if="isOpen"
        ref="popupRef"
        :style="popupStyle"
        class="bg-bg-elevated border border-border-strong rounded-[2px] shadow-overlay flex flex-col overflow-hidden font-mono"
        @keydown="onKeydown"
      >
        <div v-if="searchable" class="p-1.5 border-b border-border bg-bg-surface">
          <input
            ref="searchInputRef"
            v-model="searchQuery"
            type="text"
            placeholder="Lọc lựa chọn..."
            aria-label="Lọc lựa chọn"
            class="w-full h-6 px-2 text-xs bg-bg-input text-fg-strong border border-border rounded-[2px] outline-none focus:border-border-focus placeholder:text-fg-dim"
            @click.stop
          />
        </div>

        <div
          :id="listboxId"
          role="listbox"
          :aria-multiselectable="multiple ? 'true' : undefined"
          class="overflow-y-auto max-h-[240px] py-1"
        >
          <div v-if="loading" class="flex items-center gap-2 px-2.5 py-2 text-xs text-fg-muted">
            <AppSpinner size="sm" />
            <span>Đang tải...</span>
          </div>

          <template v-else>
            <template v-for="grp in groupedRenderList" :key="grp.group || '_default'">
              <div
                v-if="grp.group"
                class="px-2.5 py-1 text-[10px] font-bold uppercase tracking-wider text-fg-dim select-none"
              >
                {{ grp.group }}
              </div>
              <div
                v-for="{ opt, idx } in grp.items"
                :key="opt.value"
                :ref="(el) => { optionRefs[idx] = el as HTMLElement | null }"
                :id="getOptionId(idx)"
                role="option"
                :aria-selected="isSelected(opt.value) ? 'true' : 'false'"
                :aria-disabled="opt.disabled ? 'true' : undefined"
                :class="[
                  'flex items-center justify-between gap-2 px-2.5 py-1.5 text-xs select-none',
                  opt.disabled
                    ? 'opacity-40 cursor-not-allowed text-fg-dim'
                    : 'cursor-pointer',
                  idx === activeIndex && !opt.disabled ? 'bg-bg-hover text-fg-strong' : '',
                  isSelected(opt.value) ? 'bg-bg-selected text-phosphor font-semibold' : 'text-fg'
                ]"
                @mouseenter="!opt.disabled && (activeIndex = idx)"
                @click.stop="selectOption(opt)"
              >
                <span class="flex items-center gap-1.5 min-w-0">
                  <span class="w-2.5 text-phosphor shrink-0">{{ idx === activeIndex ? '▌' : '' }}</span>
                  <span class="truncate">{{ opt.label }}</span>
                </span>
                <Check v-if="isSelected(opt.value)" class="w-3.5 h-3.5 shrink-0 text-phosphor" aria-hidden="true" />
              </div>
            </template>

            <div
              v-if="canCreate"
              role="option"
              aria-selected="false"
              class="flex items-center gap-1.5 px-2.5 py-1.5 text-xs text-accent hover:bg-bg-hover cursor-pointer border-t border-border"
              @click.stop="createFromQuery"
            >
              <Plus class="w-3.5 h-3.5" aria-hidden="true" />
              <span>Tạo mới "{{ searchQuery.trim() }}"</span>
            </div>

            <div
              v-if="filteredOptions.length === 0 && !canCreate"
              class="px-2.5 py-3 text-xs text-center text-fg-muted"
            >
              {{ emptyText }}
            </div>
          </template>
        </div>
      </div>
    </Teleport>
  </div>
</template>
