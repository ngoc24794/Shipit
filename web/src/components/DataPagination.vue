<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import {
  ChevronsLeft,
  ChevronLeft,
  ChevronRight,
  ChevronsRight,
  Loader2
} from 'lucide-vue-next'
import AppSelect from './AppSelect.vue'

const props = withDefaults(
  defineProps<{
    page: number
    pageSize: number
    total: number
    totalPages: number
    loading?: boolean
    pageSizeOptions?: number[]
  }>(),
  {
    loading: false,
    pageSizeOptions: () => [10, 20, 50, 100]
  }
)

const emit = defineEmits<{
  (e: 'update:page', value: number): void
  (e: 'update:pageSize', value: number): void
}>()

const jumpInput = ref(String(props.page))

watch(
  () => props.page,
  (val) => {
    jumpInput.value = String(val)
  }
)

type PageToken = number | 'ellipsis-left' | 'ellipsis-right'

const pageTokens = computed<PageToken[]>(() => {
  const tp = Math.max(1, props.totalPages)
  const cur = Math.max(1, Math.min(tp, props.page))
  if (tp <= 7) {
    return Array.from({ length: tp }, (_, i) => i + 1)
  }

  const tokens: PageToken[] = [1]
  let start = Math.max(2, cur - 1)
  let end = Math.min(tp - 1, cur + 1)

  if (cur <= 3) {
    start = 2
    end = 4
  } else if (cur >= tp - 2) {
    start = tp - 3
    end = tp - 1
  }

  if (start > 2) {
    tokens.push('ellipsis-left')
  }
  for (let p = start; p <= end; p++) {
    tokens.push(p)
  }
  if (end < tp - 1) {
    tokens.push('ellipsis-right')
  }
  tokens.push(tp)

  return tokens
})

function goToPage(p: number) {
  if (props.loading) return
  const clamped = Math.max(1, Math.min(Math.max(1, props.totalPages), Math.trunc(p) || 1))
  if (clamped !== props.page) {
    emit('update:page', clamped)
  }
  jumpInput.value = String(clamped)
}

function handleJumpSubmit() {
  const parsed = Number.parseInt(jumpInput.value, 10)
  if (!Number.isNaN(parsed)) {
    goToPage(parsed)
  } else {
    jumpInput.value = String(props.page)
  }
}

function onPageSizeChange(val: string | number) {
  const num = Number(val)
  if (props.pageSizeOptions.includes(num)) {
    emit('update:pageSize', num)
  }
}
</script>

<template>
  <div
    class="shrink-0 h-[34px] flex flex-row items-center justify-between gap-3 flex-nowrap px-3 border-t border-border bg-bg-surface text-[11.5px] text-fg-muted font-mono overflow-x-auto"
    data-testid="data-pagination"
  >
    <!-- Left: Page size + Loading indicator -->
    <div class="flex flex-row items-center gap-2 shrink-0 whitespace-nowrap">
      <div class="inline-flex items-center gap-1.5 whitespace-nowrap">
        <span>Số dòng</span>
        <div class="w-[68px]">
          <AppSelect
            :model-value="pageSize"
            :options="pageSizeOptions.map((s) => ({ label: String(s), value: s }))"
            aria-label="Số dòng mỗi trang"
            :disabled="loading"
            size="sm"
            @update:model-value="onPageSizeChange"
          />
        </div>
      </div>

      <span
        v-if="loading"
        class="inline-flex items-center gap-1 text-accent whitespace-nowrap"
        aria-live="polite"
      >
        <Loader2 class="w-3 h-3 animate-spin" aria-hidden="true" />
        <span>Đang tải...</span>
      </span>
    </div>

    <!-- Center: Page navigation -->
    <nav aria-label="Phân trang" class="flex flex-row items-center gap-1 shrink-0 whitespace-nowrap">
      <button
        type="button"
        aria-label="Trang đầu"
        title="Trang đầu"
        :disabled="loading || page <= 1"
        class="inline-flex items-center justify-center w-6 h-6 rounded-[2px] border border-border bg-bg-elevated text-fg hover:bg-bg-hover disabled:opacity-40 disabled:cursor-not-allowed cursor-pointer transition-colors duration-[80ms]"
        @click="goToPage(1)"
      >
        <ChevronsLeft class="w-3.5 h-3.5" aria-hidden="true" />
      </button>

      <button
        type="button"
        aria-label="Trang trước"
        title="Trang trước"
        :disabled="loading || page <= 1"
        class="inline-flex items-center justify-center w-6 h-6 rounded-[2px] border border-border bg-bg-elevated text-fg hover:bg-bg-hover disabled:opacity-40 disabled:cursor-not-allowed cursor-pointer transition-colors duration-[80ms]"
        @click="goToPage(page - 1)"
      >
        <ChevronLeft class="w-3.5 h-3.5" aria-hidden="true" />
      </button>

      <template v-for="token in pageTokens" :key="token">
        <span
          v-if="typeof token === 'string'"
          class="px-1 text-fg-dim select-none"
          aria-hidden="true"
        >
          ...
        </span>
        <button
          v-else
          type="button"
          :aria-label="`Trang ${token}`"
          :aria-current="token === page ? 'page' : undefined"
          :disabled="loading"
          :class="[
            'inline-flex items-center justify-center min-w-6 h-6 px-1.5 rounded-[2px] text-[11.5px] font-medium tabular-nums transition-colors duration-[80ms] cursor-pointer disabled:opacity-50',
            token === page
              ? 'bg-[var(--phosphor-bg)] text-phosphor border border-phosphor font-bold'
              : 'border border-border bg-bg-elevated text-fg hover:bg-bg-hover'
          ]"
          @click="goToPage(token)"
        >
          {{ token }}
        </button>
      </template>

      <button
        type="button"
        aria-label="Trang sau"
        title="Trang sau"
        :disabled="loading || page >= totalPages"
        class="inline-flex items-center justify-center w-6 h-6 rounded-[2px] border border-border bg-bg-elevated text-fg hover:bg-bg-hover disabled:opacity-40 disabled:cursor-not-allowed cursor-pointer transition-colors duration-[80ms]"
        @click="goToPage(page + 1)"
      >
        <ChevronRight class="w-3.5 h-3.5" aria-hidden="true" />
      </button>

      <button
        type="button"
        aria-label="Trang cuối"
        title="Trang cuối"
        :disabled="loading || page >= totalPages"
        class="inline-flex items-center justify-center w-6 h-6 rounded-[2px] border border-border bg-bg-elevated text-fg hover:bg-bg-hover disabled:opacity-40 disabled:cursor-not-allowed cursor-pointer transition-colors duration-[80ms]"
        @click="goToPage(totalPages)"
      >
        <ChevronsRight class="w-3.5 h-3.5" aria-hidden="true" />
      </button>
    </nav>

    <!-- Right: Jump to page -->
    <form
      class="flex flex-row items-center gap-1.5 shrink-0 whitespace-nowrap"
      @submit.prevent="handleJumpSubmit"
    >
      <label class="inline-flex items-center gap-1.5 whitespace-nowrap">
        <span>Tới trang</span>
        <input
          v-model="jumpInput"
          type="number"
          min="1"
          :max="totalPages"
          aria-label="Nhảy tới trang"
          :disabled="loading"
          class="w-12 h-6 px-1.5 text-[11.5px] text-center tabular-nums rounded-[2px] bg-bg-input text-fg-strong border border-border focus:outline-none focus:border-border-focus disabled:opacity-50"
        />
      </label>
      <button
        type="submit"
        :disabled="loading"
        class="h-6 px-2 text-[11.5px] font-medium rounded-[2px] border border-border-strong bg-bg-elevated text-fg hover:bg-bg-hover transition-colors duration-[80ms] cursor-pointer disabled:opacity-50"
      >
        Đi
      </button>
    </form>
  </div>
</template>
