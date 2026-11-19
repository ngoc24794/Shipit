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
    class="flex-1 min-h-0 overflow-y-auto p-2 space-y-2.5 bg-bg-elevated"
    role="listbox"
    aria-label="Kết quả tìm kiếm"
    data-testid="search-results"
  >
    <div
      v-if="error"
      class="flex items-center gap-2 px-2.5 py-2 rounded-[2px] bg-danger-bg border border-danger/30 text-danger text-xs"
      role="alert"
      data-testid="search-error"
    >
      <AlertCircle class="w-4 h-4 shrink-0" aria-hidden="true" />
      <span class="truncate" :title="error">{{ error }}</span>
    </div>

    <div
      v-else-if="loading && groups.length === 0"
      class="flex items-center justify-center gap-2 py-8 text-xs text-fg-muted"
      data-testid="search-loading"
    >
      <Loader2 class="w-4 h-4 animate-spin text-phosphor" aria-hidden="true" />
      <span>Đang tìm kiếm...</span>
    </div>

    <div
      v-else-if="query.trim() && groups.length === 0"
      class="flex flex-col items-center justify-center gap-2 py-8 text-xs text-fg-muted"
      data-testid="search-empty"
    >
      <SearchX class="w-5 h-5 text-fg-dim" aria-hidden="true" />
      <span>Không tìm thấy kết quả phù hợp</span>
    </div>

    <template v-else>
      <div
        v-for="group in indexedGroups"
        :key="group.key"
        class="space-y-0.5"
        :data-testid="`search-group-${group.key}`"
      >
        <div class="px-2 py-1 text-[10px] font-bold uppercase tracking-wider text-fg-dim select-none">
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
            'w-full flex items-center gap-2.5 px-2.5 py-1.5 rounded-[2px] border-l-2 text-left transition-colors cursor-pointer',
            activeIndex === flatIndex
              ? 'bg-bg-selected text-phosphor border-phosphor'
              : 'border-transparent text-fg hover:bg-bg-hover'
          ]"
          @mouseenter="emit('hover', flatIndex)"
          @click="emit('select', item)"
        >
          <span
            class="w-2.5 text-[11px] font-bold shrink-0 text-phosphor"
            aria-hidden="true"
          >
            {{ activeIndex === flatIndex ? '▌' : '' }}
          </span>
          <component
            :is="iconForKind(item.kind)"
            :class="[
              'w-3.5 h-3.5 shrink-0',
              activeIndex === flatIndex ? 'text-phosphor' : 'text-fg-muted'
            ]"
            aria-hidden="true"
          />

          <div class="flex-1 min-w-0 flex items-center justify-between gap-3">
            <span class="text-xs font-medium truncate" :title="item.title">
              <template v-for="(seg, sIdx) in splitHighlight(item.title, query)" :key="sIdx">
                <mark
                  v-if="seg.match"
                  class="bg-phosphor-bg text-phosphor rounded-[2px] px-0.5 font-semibold"
                >{{ seg.text }}</mark>
                <span v-else>{{ seg.text }}</span>
              </template>
            </span>

            <span
              v-if="item.subtitle"
              class="text-[11px] text-fg-muted truncate max-w-[240px] shrink-0"
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
