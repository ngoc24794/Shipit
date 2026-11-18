<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { Loader2, Search, X } from 'lucide-vue-next'
import { useSearchStore, type PaletteItem } from '../../stores/search'
import SearchResults from './SearchResults.vue'

const router = useRouter()
const route = useRoute()
const searchStore = useSearchStore()

const inputRef = ref<HTMLInputElement | null>(null)
const activeIndex = ref(0)

const flatItems = computed<PaletteItem[]>(() =>
  searchStore.displayGroups.flatMap((g) => g.items)
)

const groupStartIndices = computed<number[]>(() => {
  const starts: number[] = []
  let cursor = 0
  for (const group of searchStore.displayGroups) {
    if (group.items.length > 0) {
      starts.push(cursor)
      cursor += group.items.length
    }
  }
  return starts
})

watch(
  () => searchStore.displayGroups,
  () => {
    activeIndex.value = 0
  }
)

watch(
  () => searchStore.isOpen,
  async (open) => {
    if (open) {
      activeIndex.value = 0
      await nextTick()
      inputRef.value?.focus()
    }
  }
)

watch(
  () => searchStore.unauthorized,
  (isUnauthorized) => {
    if (isUnauthorized) {
      searchStore.close()
      router.push({
        name: 'login',
        query: route.fullPath && route.fullPath !== '/' ? { redirect: route.fullPath } : undefined
      })
    }
  }
)

function isTypingTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false
  if (target.isContentEditable) return true
  const tag = target.tagName.toLowerCase()
  return tag === 'input' || tag === 'textarea' || tag === 'select'
}

function handleGlobalKeydown(e: KeyboardEvent): void {
  const key = e.key.toLowerCase()

  if ((e.ctrlKey || e.metaKey) && key === 'k') {
    e.preventDefault()
    searchStore.toggle()
    return
  }

  if (e.key === '/' && !e.ctrlKey && !e.metaKey && !e.altKey && !searchStore.isOpen) {
    if (!isTypingTarget(e.target)) {
      e.preventDefault()
      searchStore.open()
    }
    return
  }

  if (e.key === 'Escape' && searchStore.isOpen) {
    e.preventDefault()
    searchStore.close()
  }
}

function handlePaletteKeydown(e: KeyboardEvent): void {
  if (e.key === 'Escape') {
    e.preventDefault()
    searchStore.close()
    return
  }

  const total = flatItems.value.length
  if (total === 0) return

  if (e.key === 'ArrowDown') {
    e.preventDefault()
    activeIndex.value = (activeIndex.value + 1) % total
    return
  }

  if (e.key === 'ArrowUp') {
    e.preventDefault()
    activeIndex.value = (activeIndex.value - 1 + total) % total
    return
  }

  if (e.key === 'Tab') {
    const starts = groupStartIndices.value
    if (starts.length <= 1) return
    e.preventDefault()

    let currentGroupIdx = 0
    for (let i = 0; i < starts.length; i += 1) {
      if (starts[i] <= activeIndex.value) {
        currentGroupIdx = i
      }
    }

    const nextGroupIdx = e.shiftKey
      ? (currentGroupIdx - 1 + starts.length) % starts.length
      : (currentGroupIdx + 1) % starts.length
    activeIndex.value = starts[nextGroupIdx]
    return
  }

  if (e.key === 'Enter') {
    e.preventDefault()
    const item = flatItems.value[activeIndex.value]
    if (item) {
      selectItem(item)
    }
  }
}

function selectItem(item: PaletteItem): void {
  searchStore.addRecent(item)
  searchStore.close()
  router.push(item.path)
}

function handleInput(e: Event): void {
  const value = (e.target as HTMLInputElement).value
  searchStore.setQuery(value)
}

function clearQuery(): void {
  searchStore.setQuery('')
  inputRef.value?.focus()
}

onMounted(() => {
  window.addEventListener('keydown', handleGlobalKeydown)
})

onUnmounted(() => {
  window.removeEventListener('keydown', handleGlobalKeydown)
})
</script>

<template>
  <div class="flex-1 flex justify-center px-4 min-w-0">
    <button
      type="button"
      class="w-[400px] shrink-0 h-8 flex items-center justify-between gap-2 px-3 rounded-[6px] bg-muted/70 hover:bg-muted border border-border text-sm text-muted-fg focus-visible:outline-2 focus-visible:outline-ring cursor-pointer transition-colors whitespace-nowrap"
      aria-label="Tìm pipeline, job, lượt chạy... (Ctrl K)"
      title="Tìm pipeline, job, lượt chạy... (Ctrl K)"
      data-testid="global-search-trigger"
      @click="searchStore.open()"
    >
      <span class="flex items-center gap-2 min-w-0">
        <Search class="w-4 h-4 shrink-0 text-muted-fg" aria-hidden="true" />
        <span class="truncate">Tìm pipeline, job, lượt chạy...</span>
      </span>
      <kbd class="shrink-0 text-[11px] font-mono px-1.5 py-0.5 rounded bg-card border border-border text-muted-fg">
        Ctrl K
      </kbd>
    </button>

    <Teleport to="body">
      <div
        v-if="searchStore.isOpen"
        class="fixed inset-0 z-50 bg-black/50 flex items-start justify-center pt-[12%]"
        data-testid="search-backdrop"
        @mousedown.self="searchStore.close()"
      >
        <div
          class="w-[640px] max-h-[60%] bg-card border border-border rounded-[8px] shadow-xl flex flex-col overflow-hidden text-fg"
          role="dialog"
          aria-modal="true"
          aria-label="Tìm kiếm toàn cục"
          data-testid="search-dialog"
          @keydown="handlePaletteKeydown"
        >
          <div class="flex items-center gap-2.5 px-3.5 h-12 border-b border-border shrink-0">
            <Search class="w-4 h-4 shrink-0 text-muted-fg" aria-hidden="true" />
            <input
              ref="inputRef"
              type="text"
              :value="searchStore.query"
              placeholder="Tìm pipeline, job, lượt chạy... (Ctrl K)"
              aria-label="Tìm pipeline, job, lượt chạy"
              class="flex-1 min-w-0 bg-transparent text-sm text-fg placeholder:text-muted-fg focus:outline-none"
              data-testid="search-input"
              @input="handleInput"
            />
            <Loader2
              v-if="searchStore.loading"
              class="w-4 h-4 shrink-0 animate-spin text-primary"
              aria-hidden="true"
            />
            <button
              v-if="searchStore.query"
              type="button"
              aria-label="Xoá từ khoá"
              class="p-1 rounded-[4px] text-muted-fg hover:text-fg hover:bg-muted cursor-pointer"
              data-testid="search-clear"
              @click="clearQuery"
            >
              <X class="w-3.5 h-3.5" aria-hidden="true" />
            </button>
            <kbd class="shrink-0 text-[11px] font-mono px-1.5 py-0.5 rounded bg-muted border border-border text-muted-fg">
              Esc
            </kbd>
          </div>

          <SearchResults
            :groups="searchStore.displayGroups"
            :query="searchStore.query"
            :active-index="activeIndex"
            :loading="searchStore.loading"
            :error="searchStore.error"
            @select="selectItem"
            @hover="(idx) => (activeIndex = idx)"
          />

          <div class="px-3.5 py-2 border-t border-border bg-muted/40 text-[11px] text-muted-fg flex items-center justify-between shrink-0 whitespace-nowrap">
            <div class="flex items-center gap-3">
              <span>↑↓ di chuyển</span>
              <span>Tab đổi nhóm</span>
              <span>Enter mở</span>
              <span>Esc đóng</span>
            </div>
            <span>/ để mở nhanh</span>
          </div>
        </div>
      </div>
    </Teleport>
  </div>
</template>
