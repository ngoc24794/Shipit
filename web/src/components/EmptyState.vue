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
  <div class="flex flex-col items-center justify-center p-8 text-center rounded-[8px] border border-dashed border-border bg-card/50 my-4">
    <div class="flex items-center justify-center w-12 h-12 rounded-full bg-muted text-muted-fg mb-4">
      <slot name="icon">
        <component :is="icon || Inbox" class="w-6 h-6" aria-hidden="true" />
      </slot>
    </div>

    <h3 class="text-base font-semibold text-fg mb-1">
      <slot name="title">{{ title }}</slot>
    </h3>

    <p v-if="description || $slots.description" class="text-sm text-muted-fg max-w-sm mb-6">
      <slot name="description">{{ description }}</slot>
    </p>

    <div v-if="$slots.action" class="flex items-center gap-3">
      <slot name="action" />
    </div>
  </div>
</template>
