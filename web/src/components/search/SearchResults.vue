<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import {
  Play,
  Workflow,
  Boxes,
  Server,
  KeyRound,
  ShieldCheck,
  Plus,
  CornerDownRight,
  Loader2,
  AlertCircle,
  SearchX
} from 'lucide-vue-next'
import type { PaletteGroup, PaletteItem, SearchItemKind } from '../../stores/search'

const props = defineProps<{
  groups: PaletteGroup[]
  query: string
  activeIndex: number
  loading: boolean
  error: string | null
}>()

const emit = defineEmits<{
  (e: 'select', item: PaletteItem): void
  (e: 'hover', index: number): void
}>()

const containerRef = ref<HTMLElement | null>(null)
const itemRefs = ref<Record<number, HTMLElement | null>>({})

const indexedGroups = computed(() => {
  let cursor = 0
  return props.groups.map((group) => {
    const items = group.items.map((item) => {
      const flatIndex = cursor
      cursor += 1
      return { item, flatIndex }
    })
    return { ...group, items }
  })
})

function setItemRef(index: number, el: unknown): void {
  if (el instanceof HTMLElement) {
    itemRefs.value[index] = el
  } else {
    delete itemRefs.value[index]
  }
}

watch(
  () => props.activeIndex,
  async (idx) => {
    await nextTick()
    const el = itemRefs.value[idx]
    if (el && typeof el.scrollIntoView === 'function') {
      el.scrollIntoView({ block: 'nearest' })
    }
  }
)

function iconForKind(kind: SearchItemKind) {
  switch (kind) {
    case 'run':
      return Play
    case 'pipeline':
      return Workflow
    case 'job':
      return Boxes
    case 'slave':
      return Server
    case 'credential':
      return KeyRound
    case 'deploy-target':
    case 'job-allow':
      return ShieldCheck
    case 'action':
      return Plus
    default:
      return CornerDownRight
  }
}

interface TextSegment {
  text: string
  match: boolean
}

function splitHighlight(text: string, rawQuery: string): TextSegment[] {
  const q = rawQuery.trim()
  if (!q) return [{ text, match: false }]

  const escaped = q.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  const regex = new RegExp(`(${escaped})`, 'gi')
  const parts = text.split(regex)
  if (parts.length <= 1) return [{ text, match: false }]

  const lowerQ = q.toLowerCase()
  return parts
    .filter((part) => part.length > 0)
    .map((part) => ({
      text: part,
      match: part.toLowerCase() === lowerQ
    }))
}
</script>

<template>
  <div
    ref="containerRef"
    class="flex-1 min-h-0 overflow-y-auto p-2 space-y-3"
    role="listbox"
    aria-label="Kết quả tìm kiếm"
    data-testid="search-results"
  >
    <div
      v-if="error"
      class="flex items-center gap-2 px-3 py-2.5 rounded-[6px] bg-danger/10 text-danger text-sm"
      role="alert"
      data-testid="search-error"
    >
      <AlertCircle class="w-4 h-4 shrink-0" aria-hidden="true" />
      <span class="truncate" :title="error">{{ error }}</span>
    </div>

    <div
      v-else-if="loading && groups.length === 0"
      class="flex items-center justify-center gap-2 py-8 text-sm text-muted-fg"
      data-testid="search-loading"
    >
      <Loader2 class="w-4 h-4 animate-spin text-primary" aria-hidden="true" />
      <span>Đang tìm kiếm...</span>
    </div>

    <div
      v-else-if="query.trim() && groups.length === 0"
      class="flex flex-col items-center justify-center gap-2 py-8 text-sm text-muted-fg"
      data-testid="search-empty"
    >
      <SearchX class="w-5 h-5 text-muted-fg/80" aria-hidden="true" />
      <span>Không tìm thấy kết quả phù hợp</span>
    </div>

    <template v-else>
      <div
        v-for="group in indexedGroups"
        :key="group.key"
        class="space-y-1"
        :data-testid="`search-group-${group.key}`"
      >
        <div class="px-2.5 py-1 text-[11px] font-semibold uppercase tracking-wider text-muted-fg select-none">
          {{ group.label }}
        </div>

        <button
          v-for="{ item, flatIndex } in group.items"
          :key="`${group.key}-${item.id}`"
          :ref="(el) => setItemRef(flatIndex, el)"
          type="button"
          role="option"
          :aria-selected="activeIndex === flatIndex"
          :data-active="activeIndex === flatIndex ? 'true' : 'false'"
          :data-testid="`search-item-${flatIndex}`"
          :class="[
            'w-full flex items-center gap-3 px-2.5 py-2 rounded-[6px] text-left transition-colors cursor-pointer',
            activeIndex === flatIndex
              ? 'bg-primary/10 text-primary'
              : 'text-fg hover:bg-muted'
          ]"
          @mouseenter="emit('hover', flatIndex)"
          @click="emit('select', item)"
        >
          <component
            :is="iconForKind(item.kind)"
            :class="[
              'w-4 h-4 shrink-0',
              activeIndex === flatIndex ? 'text-primary' : 'text-muted-fg'
            ]"
            aria-hidden="true"
          />

          <div class="flex-1 min-w-0 flex items-center justify-between gap-3">
            <span class="text-sm font-medium truncate" :title="item.title">
              <template v-for="(seg, sIdx) in splitHighlight(item.title, query)" :key="sIdx">
                <mark
                  v-if="seg.match"
                  class="bg-primary/20 text-primary rounded-xs px-0.5 font-semibold"
                >{{ seg.text }}</mark>
                <span v-else>{{ seg.text }}</span>
              </template>
            </span>

            <span
              v-if="item.subtitle"
              class="text-xs text-muted-fg truncate max-w-[240px] shrink-0"
              :title="item.subtitle"
            >
              {{ item.subtitle }}
            </span>
          </div>
        </button>
      </div>
    </template>
  </div>
</template>
