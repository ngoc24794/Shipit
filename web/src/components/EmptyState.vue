<script setup lang="ts">
import { Inbox } from 'lucide-vue-next'

withDefaults(
  defineProps<{
    title?: string
    description?: string
    icon?: any
  }>(),
  {
    title: 'Chưa có dữ liệu',
    description: undefined,
    icon: undefined
  }
)
</script>

<template>
  <div class="flex flex-col items-center justify-center p-6 text-center rounded-[2px] border border-dashed border-border bg-bg-elevated/60 my-2 font-mono">
    <div class="flex items-center justify-center w-9 h-9 rounded-[2px] bg-bg-surface border border-border text-fg-dim mb-3">
      <slot name="icon">
        <component :is="icon || Inbox" class="w-4 h-4" aria-hidden="true" />
      </slot>
    </div>

    <h3 class="text-[13px] font-bold text-fg-strong mb-1">
      <slot name="title">{{ title }}</slot>
    </h3>

    <p v-if="description || $slots.description" class="prose text-[12.5px] text-fg-muted max-w-md mb-4">
      <slot name="description">{{ description }}</slot>
    </p>

    <div v-if="$slots.action" class="flex items-center gap-2">
      <slot name="action" />
    </div>
  </div>
</template>
