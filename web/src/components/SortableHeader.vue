<script setup lang="ts">
import { ArrowUpDown, ArrowUp, ArrowDown } from 'lucide-vue-next'
import type { SortDirection } from '../composables/useDataTable'

withDefaults(
  defineProps<{
    label: string
    sortKey: string
    sortBy: string | null
    sortDir: SortDirection
    align?: 'left' | 'center' | 'right'
  }>(),
  {
    align: 'left'
  }
)

const emit = defineEmits<{
  (e: 'sort', key: string): void
}>()
</script>

<template>
  <th
    scope="col"
    :aria-sort="sortBy === sortKey && sortDir ? (sortDir === 'asc' ? 'ascending' : 'descending') : 'none'"
    :class="[
      'py-1.5 whitespace-nowrap select-none font-mono',
      align === 'right' ? 'text-right' : align === 'center' ? 'text-center' : 'text-left'
    ]"
  >
    <button
      type="button"
      class="inline-flex items-center gap-1 font-bold text-[11px] uppercase tracking-[0.04em] text-fg-muted hover:text-fg-strong transition-colors duration-[80ms] cursor-pointer focus-visible:outline-2 focus-visible:outline-border-focus rounded-[2px]"
      @click="emit('sort', sortKey)"
    >
      <span>{{ label }}</span>
      <ArrowUp
        v-if="sortBy === sortKey && sortDir === 'asc'"
        class="w-3 h-3 text-phosphor shrink-0"
        aria-hidden="true"
      />
      <ArrowDown
        v-else-if="sortBy === sortKey && sortDir === 'desc'"
        class="w-3 h-3 text-phosphor shrink-0"
        aria-hidden="true"
      />
      <ArrowUpDown
        v-else
        class="w-3 h-3 opacity-45 shrink-0"
        aria-hidden="true"
      />
    </button>
  </th>
</template>
