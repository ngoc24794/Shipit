<script setup lang="ts">
import { computed, nextTick, onActivated, onDeactivated, ref } from 'vue'
import AppCheckbox from './AppCheckbox.vue'
import EmptyState from './EmptyState.vue'
import ErrorState from './ErrorState.vue'

const props = withDefaults(
  defineProps<{
    bordered?: boolean
    density?: 'compact' | 'normal'
    loading?: boolean
    skeletonRows?: number
    skeletonCols?: number
    empty?: boolean
    emptyTitle?: string
    emptyDescription?: string
    error?: string | null
    rows?: any[]
    rowKey?: string | ((row: any, index: number) => string | number)
    selectable?: boolean
    selectedKeys?: Array<string | number>
    activeIndex?: number
    keyboardNav?: boolean
  }>(),
  {
    bordered: true,
    density: 'normal',
    loading: false,
    skeletonRows: 5,
    skeletonCols: 5,
    empty: false,
    emptyTitle: 'Chưa có dữ liệu',
    emptyDescription: undefined,
    error: null,
    rows: undefined,
    rowKey: 'id',
    selectable: false,
    selectedKeys: () => [],
    activeIndex: -1,
    keyboardNav: false
  }
)

const emit = defineEmits<{
  (e: 'update:selectedKeys', keys: Array<string | number>): void
  (e: 'update:activeIndex', index: number): void
  (e: 'row-activate', row: any, index: number): void
  (e: 'retry'): void
}>()

const internalActiveIndex = ref(props.activeIndex)
const currentActiveIndex = computed(() =>
  props.activeIndex >= 0 ? props.activeIndex : internalActiveIndex.value
)

function getRowKey(row: any, index: number): string | number {
  if (typeof props.rowKey === 'function') {
    return props.rowKey(row, index)
  }
  return row?.[props.rowKey] ?? index
}

const allRowKeys = computed(() =>
  (props.rows ?? []).map((row, idx) => getRowKey(row, idx))
)

const allSelected = computed(() => {
  if (!props.rows || props.rows.length === 0) return false
  return allRowKeys.value.every((k) => props.selectedKeys.includes(k))
})

const someSelected = computed(() => {
  if (!props.rows || props.rows.length === 0) return false
  return !allSelected.value && allRowKeys.value.some((k) => props.selectedKeys.includes(k))
})

function toggleSelectAll() {
  if (!props.rows) return
  if (allSelected.value) {
    const remaining = props.selectedKeys.filter((k) => !allRowKeys.value.includes(k))
    emit('update:selectedKeys', remaining)
  } else {
    const merged = Array.from(new Set([...props.selectedKeys, ...allRowKeys.value]))
    emit('update:selectedKeys', merged)
  }
}

function toggleSelectRow(key: string | number) {
  const exists = props.selectedKeys.includes(key)
  const next = exists
    ? props.selectedKeys.filter((k) => k !== key)
    : [...props.selectedKeys, key]
  emit('update:selectedKeys', next)
}

function setActiveIndex(idx: number) {
  internalActiveIndex.value = idx
  emit('update:activeIndex', idx)
}

const containerRef = ref<HTMLElement | null>(null)
let savedScrollTop = 0
let savedScrollLeft = 0

onDeactivated(() => {
  if (containerRef.value) {
    savedScrollTop = containerRef.value.scrollTop
    savedScrollLeft = containerRef.value.scrollLeft
  }
})

onActivated(() => {
  nextTick(() => {
    if (containerRef.value) {
      containerRef.value.scrollTop = savedScrollTop
      containerRef.value.scrollLeft = savedScrollLeft
    }
  })
})

function handleTableKeydown(e: KeyboardEvent) {
  if (!props.keyboardNav || !props.rows || props.rows.length === 0) return
  const target = e.target as HTMLElement | null
  if (target && ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName)) return

  const len = props.rows.length
  const cur = currentActiveIndex.value

  if (e.key === 'j' || e.key === 'ArrowDown') {
    e.preventDefault()
    const next = cur < len - 1 ? cur + 1 : 0
    setActiveIndex(next)
  } else if (e.key === 'k' || e.key === 'ArrowUp') {
    e.preventDefault()
    const prev = cur > 0 ? cur - 1 : len - 1
    setActiveIndex(prev)
  } else if (e.key === 'Enter' && cur >= 0 && cur < len) {
    e.preventDefault()
    emit('row-activate', props.rows[cur], cur)
  }
}
</script>

<template>
  <div
    ref="containerRef"
    :tabindex="keyboardNav ? 0 : undefined"
    :class="[
      'w-full flex-1 min-h-0 max-h-full overflow-auto bg-bg-elevated font-mono focus-visible:outline-2 focus-visible:outline-border-focus',
      bordered ? 'rounded-[2px] border border-border' : '',
      density === 'compact' ? 'd1-table-compact' : 'd1-table-normal'
    ]"
    @keydown="handleTableKeydown"
  >
    <div v-if="error" class="p-4">
      <slot name="error">
        <ErrorState :message="error" @retry="emit('retry')" />
      </slot>
    </div>

    <div v-else-if="empty && !loading" class="p-4">
      <slot name="empty">
        <EmptyState :title="emptyTitle" :description="emptyDescription" />
      </slot>
    </div>

    <table v-else class="w-full text-left text-[12.5px] tabular-nums border-collapse table-fixed">
      <thead class="sticky top-0 z-10 bg-bg-surface text-[11px] font-bold uppercase tracking-[0.04em] text-fg-muted border-b border-border-strong">
        <slot name="header">
          <tr v-if="$slots['header-columns']">
            <th v-if="selectable" scope="col" class="w-12 text-center">
              <div class="inline-flex items-center justify-center">
                <AppCheckbox
                  :model-value="allSelected"
                  :indeterminate="someSelected"
                  aria-label="Chọn tất cả dòng"
                  @update:model-value="toggleSelectAll"
                />
              </div>
            </th>
            <th v-if="keyboardNav" scope="col" class="w-5" aria-label="Con trỏ" />
            <slot name="header-columns" />
          </tr>
        </slot>
      </thead>

      <tbody class="divide-y divide-border text-fg">
        <template v-if="loading">
          <tr v-for="r in skeletonRows" :key="`skel-${r}`" aria-busy="true">
            <td
              v-for="c in skeletonCols"
              :key="`skel-${r}-${c}`"
              class="text-fg-dim skel-pulse select-none"
            >
              {{ c === 1 ? '████████████' : '████████' }}
            </td>
          </tr>
        </template>

        <template v-else-if="rows && $slots.row">
          <tr
            v-for="(row, idx) in rows"
            :key="getRowKey(row, idx)"
            :class="[
              currentActiveIndex === idx ? 'bg-bg-selected d1-row-selected' : '',
              selectedKeys.includes(getRowKey(row, idx)) ? 'bg-bg-selected/70' : ''
            ]"
            @click="setActiveIndex(idx)"
          >
            <td v-if="selectable" class="w-12 text-center" @click.stop>
              <div class="inline-flex items-center justify-center">
                <AppCheckbox
                  :model-value="selectedKeys.includes(getRowKey(row, idx))"
                  :aria-label="`Chọn dòng ${idx + 1}`"
                  @update:model-value="toggleSelectRow(getRowKey(row, idx))"
                />
              </div>
            </td>
            <td v-if="keyboardNav" class="w-5 px-1 text-center select-none">
              <span
                v-if="currentActiveIndex === idx"
                class="text-phosphor font-bold caret-blink"
                aria-hidden="true"
              >▌</span>
            </td>
            <slot name="row" :row="row" :index="idx" :active="currentActiveIndex === idx" />
          </tr>
        </template>

        <slot v-else />
      </tbody>
    </table>
  </div>
</template>

<style scoped>
.d1-table-normal :deep(thead tr) {
  height: 32px;
}
.d1-table-normal :deep(tbody tr) {
  height: 36px;
  transition: background-color var(--dur-snap, 80ms) var(--ease-snap, cubic-bezier(0.2, 0, 0, 1));
}
.d1-table-compact :deep(thead tr) {
  height: 28px;
}
.d1-table-compact :deep(tbody tr) {
  height: 30px;
  transition: background-color var(--dur-snap, 80ms) var(--ease-snap, cubic-bezier(0.2, 0, 0, 1));
}
:deep(tbody tr:hover) {
  background-color: var(--bg-hover);
}
:deep(tbody tr.d1-row-selected) {
  background-color: var(--bg-selected);
}
:deep(th),
:deep(td) {
  padding: 4px 8px;
  vertical-align: middle;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
:deep(.row-actions) {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  opacity: 0.7;
  transition: opacity var(--dur-snap, 80ms) var(--ease-snap, cubic-bezier(0.2, 0, 0, 1));
}
:deep(tbody tr:hover .row-actions),
:deep(tbody tr.d1-row-selected .row-actions),
:deep(tbody tr:focus-within .row-actions) {
  opacity: 1;
}
</style>
