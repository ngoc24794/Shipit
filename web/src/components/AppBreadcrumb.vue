<script setup lang="ts">
import type { Component } from 'vue'

export interface BreadcrumbItem {
  label: string
  to?: string | Record<string, unknown>
  icon?: Component
}

defineProps<{
  items: BreadcrumbItem[]
}>()
</script>

<template>
  <nav aria-label="Breadcrumb" class="flex items-center gap-1.5 text-[12px] font-mono text-fg-muted whitespace-nowrap min-w-0">
    <template v-for="(item, idx) in items" :key="`${item.label}-${idx}`">
      <span v-if="idx > 0" class="text-fg-dim select-none" aria-hidden="true">/</span>

      <router-link
        v-if="item.to && idx < items.length - 1"
        :to="item.to"
        class="inline-flex items-center gap-1 hover:text-fg-strong hover:underline truncate focus-visible:outline-2 focus-visible:outline-border-focus rounded-[2px]"
      >
        <component :is="item.icon" v-if="item.icon" class="w-3.5 h-3.5 shrink-0" aria-hidden="true" />
        <span class="truncate">{{ item.label }}</span>
      </router-link>

      <span
        v-else
        :aria-current="idx === items.length - 1 ? 'page' : undefined"
        :class="[
          'inline-flex items-center gap-1 truncate',
          idx === items.length - 1 ? 'text-fg-strong font-semibold' : ''
        ]"
      >
        <component :is="item.icon" v-if="item.icon" class="w-3.5 h-3.5 shrink-0" aria-hidden="true" />
        <span class="truncate">{{ item.label }}</span>
      </span>
    </template>
  </nav>
</template>
