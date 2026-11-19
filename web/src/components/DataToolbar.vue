<script setup lang="ts">
import { ref, watch, onBeforeUnmount } from 'vue'
import { Search, X, ChevronDown, RotateCcw } from 'lucide-vue-next'

export interface DataToolbarFilterOption {
  label: string
  value: string
}

export interface DataToolbarFilter {
  key: string
  label: string
  type?: 'select' | 'segmented'
  options: DataToolbarFilterOption[]
}

const props = withDefaults(
  defineProps<{
    q?: string
    searchPlaceholder?: string
    filters?: DataToolbarFilter[]
    filterValues?: Record<string, string>
    startItem?: number
    endItem?: number
    total?: number
    hasActiveFilters?: boolean
    debounceMs?: number
  }>(),
  {
    q: '',
    searchPlaceholder: 'Tìm kiếm...',
    filters: () => [],
    filterValues: () => ({}),
    startItem: 0,
    endItem: 0,
    total: 0,
    hasActiveFilters: false,
    debounceMs: 300
  }
)

const emit = defineEmits<{
  (e: 'update:q', value: string): void
  (e: 'update:filter', key: string, value: string): void
  (e: 'clear'): void
}>()

const localQ = ref(props.q)
let debounceTimer: ReturnType<typeof setTimeout> | null = null

function clearTimer() {
  if (debounceTimer !== null) {
    clearTimeout(debounceTimer)
    debounceTimer = null
  }
}

watch(
  () => props.q,
  (val) => {
    if (val !== localQ.value) {
      clearTimer()
      localQ.value = val
    }
  }
)

function onSearchInput(event: Event) {
  const val = (event.target as HTMLInputElement).value
  localQ.value = val
  clearTimer()
  debounceTimer = setTimeout(() => {
    emit('update:q', val)
  }, props.debounceMs)
}

function clearSearch() {
  clearTimer()
  localQ.value = ''
  emit('update:q', '')
}

function onSelectChange(key: string, event: Event) {
  const val = (event.target as HTMLSelectElement).value
  emit('update:filter', key, val)
}

function onSegmentedClick(key: string, value: string) {
  emit('update:filter', key, value)
}

function handleClearAll() {
  clearTimer()
  localQ.value = ''
  emit('clear')
}

onBeforeUnmount(() => {
  clearTimer()
})
</script>

<template>
  <div class="shrink-0 flex flex-row items-center justify-between gap-2 flex-nowrap px-3 py-2 border-b border-border bg-bg-elevated font-mono overflow-x-auto">
    <!-- Left: Search + Filters -->
    <div class="flex flex-row items-center gap-2 flex-nowrap min-w-0">
      <!-- Search input -->
      <div class="relative w-[220px] shrink-0">
        <Search
          class="w-3.5 h-3.5 text-fg-dim absolute left-2 top-1/2 -translate-y-1/2 pointer-events-none"
          aria-hidden="true"
        />
        <input
          type="text"
          :value="localQ"
          :placeholder="searchPlaceholder"
          :aria-label="searchPlaceholder"
          class="w-full h-7 pl-7 pr-6 text-[12px] rounded-[2px] bg-bg-input text-fg-strong border border-border placeholder:text-fg-dim focus:outline-none focus:border-border-focus transition-colors duration-[80ms]"
          @input="onSearchInput"
        />
        <button
          v-if="localQ"
          type="button"
          aria-label="Xoá từ khoá tìm kiếm"
          class="absolute right-1 top-1/2 -translate-y-1/2 p-0.5 rounded-[2px] text-fg-muted hover:text-fg-strong cursor-pointer"
          @click="clearSearch"
        >
          <X class="w-3 h-3" aria-hidden="true" />
        </button>
      </div>

      <!-- Dynamic filters -->
      <template v-for="filter in filters" :key="filter.key">
        <!-- Segmented -->
        <div
          v-if="filter.type === 'segmented'"
          role="group"
          :aria-label="filter.label"
          class="inline-flex items-center rounded-[2px] bg-bg-elevated border border-border shrink-0 whitespace-nowrap overflow-hidden"
        >
          <button
            v-for="opt in filter.options"
            :key="opt.value"
            type="button"
            :aria-pressed="(filterValues[filter.key] ?? '') === opt.value"
            :class="[
              'h-[26px] px-2.5 text-[12px] border-r border-border last:border-r-0 transition-colors duration-[80ms] cursor-pointer whitespace-nowrap',
              (filterValues[filter.key] ?? '') === opt.value
                ? 'bg-bg-selected text-phosphor font-semibold'
                : 'text-fg-muted hover:text-fg-strong hover:bg-bg-hover'
            ]"
            @click="onSegmentedClick(filter.key, opt.value)"
          >
            {{ opt.label }}
          </button>
        </div>

        <!-- Select -->
        <div v-else class="relative shrink-0">
          <select
            :value="filterValues[filter.key] ?? ''"
            :aria-label="filter.label"
            class="h-7 pl-2 pr-6 text-[12px] rounded-[2px] bg-bg-input text-fg-strong border border-border appearance-none cursor-pointer focus:outline-none focus:border-border-focus transition-colors duration-[80ms] max-w-[180px] truncate"
            @change="onSelectChange(filter.key, $event)"
          >
            <option
              v-for="opt in filter.options"
              :key="opt.value"
              :value="opt.value"
            >
              {{ opt.label }}
            </option>
          </select>
          <ChevronDown
            class="w-3.5 h-3.5 text-fg-muted absolute right-1.5 top-1/2 -translate-y-1/2 pointer-events-none"
            aria-hidden="true"
          />
        </div>
      </template>

      <slot name="filters" />
    </div>

    <!-- Right: Count, Clear button, Actions -->
    <div class="flex flex-row items-center gap-2 shrink-0 whitespace-nowrap">
      <button
        v-if="hasActiveFilters"
        type="button"
        class="inline-flex items-center gap-1 h-7 px-2 text-[11.5px] font-medium rounded-[2px] border border-border bg-bg-surface text-fg-muted hover:text-fg-strong hover:bg-bg-hover transition-colors duration-[80ms] cursor-pointer whitespace-nowrap shrink-0"
        @click="handleClearAll"
      >
        <RotateCcw class="w-3 h-3" aria-hidden="true" />
        <span>Xoá bộ lọc</span>
      </button>

      <span class="text-[11.5px] text-fg-muted tabular-nums whitespace-nowrap" data-testid="result-count">
        <template v-if="total > 0">
          Hiển thị {{ startItem }} đến {{ endItem }} trong {{ total }}
        </template>
        <template v-else>
          0 kết quả
        </template>
      </span>

      <slot name="actions" />
    </div>
  </div>
</template>
