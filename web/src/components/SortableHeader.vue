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
      'py-3 whitespace-nowrap select-none',
      align === 'right' ? 'text-right' : align === 'center' ? 'text-center' : 'text-left'
    ]"
  >
    <button
      type="button"
      class="inline-flex items-center gap-1 font-semibold text-xs text-muted-fg hover:text-fg transition-colors cursor-pointer focus-visible:outline-2 focus-visible:outline-ring rounded-[4px]"
      @click="emit('sort', sortKey)"
    >
      <span>{{ label }}</span>
      <ArrowUp
        v-if="sortBy === sortKey && sortDir === 'asc'"
        class="w-3.5 h-3.5 text-primary shrink-0"
        aria-hidden="true"
      />
      <ArrowDown
        v-else-if="sortBy === sortKey && sortDir === 'desc'"
        class="w-3.5 h-3.5 text-primary shrink-0"
        aria-hidden="true"
      />
      <ArrowUpDown
        v-else
        class="w-3.5 h-3.5 opacity-45 shrink-0"
        aria-hidden="true"
      />
    </button>
  </th>
</template>
