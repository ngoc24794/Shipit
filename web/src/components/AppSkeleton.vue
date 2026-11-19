<script setup lang="ts">
export type SkeletonVariant = 'line' | 'rows' | 'table' | 'card'

withDefaults(
  defineProps<{
    variant?: SkeletonVariant
    count?: number
    columns?: number
  }>(),
  {
    variant: 'line',
    count: 3,
    columns: 4
  }
)

const blockPatterns = [
  '████████████████████',
  '██████████████░░░░░░',
  '██████████████████░░',
  '████████████░░░░░░░░'
]
</script>

<template>
  <div
    aria-busy="true"
    aria-label="Đang tải..."
    class="font-mono text-fg-dim select-none"
    :data-skeleton-variant="variant"
  >
    <!-- Single line -->
    <div v-if="variant === 'line'" class="skel-pulse tracking-[0.05em] text-[12px] truncate">
      ████████████████████████████░░░░░░
    </div>

    <!-- Multiple rows -->
    <div v-else-if="variant === 'rows'" class="space-y-2">
      <div
        v-for="i in count"
        :key="i"
        class="skel-pulse tracking-[0.05em] text-[12px] truncate"
      >
        {{ blockPatterns[(i - 1) % blockPatterns.length] }}
      </div>
    </div>

    <!-- Table skeleton -->
    <div v-else-if="variant === 'table'" class="border border-border rounded-[2px] bg-bg-elevated overflow-hidden">
      <div class="px-2 py-1.5 bg-bg-surface border-b border-border-strong flex items-center gap-4">
        <span
          v-for="c in columns"
          :key="c"
          class="flex-1 text-[11px] text-fg-dim skel-pulse truncate"
        >
          ███████
        </span>
      </div>
      <div class="divide-y divide-border">
        <div
          v-for="r in count"
          :key="r"
          class="px-2 py-2 flex items-center gap-4"
        >
          <span
            v-for="c in columns"
            :key="c"
            class="flex-1 text-[12px] text-fg-dim skel-pulse truncate"
          >
            {{ c === 1 ? '████████████' : '████████' }}
          </span>
        </div>
      </div>
    </div>

    <!-- Card skeleton -->
    <div
      v-else-if="variant === 'card'"
      class="border border-border rounded-[2px] bg-bg-elevated p-3 space-y-2"
    >
      <div class="text-[13px] font-bold text-fg-dim skel-pulse truncate">
        ██████████████
      </div>
      <div
        v-for="i in count"
        :key="i"
        class="text-[12px] text-fg-dim skel-pulse truncate"
      >
        {{ blockPatterns[(i - 1) % blockPatterns.length] }}
      </div>
    </div>
  </div>
</template>
