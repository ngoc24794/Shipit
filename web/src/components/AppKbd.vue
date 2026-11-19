<script setup lang="ts">
withDefaults(
  defineProps<{
    keys?: string
  }>(),
  {
    keys: undefined
  }
)
</script>

<template>
  <kbd
    class="inline-flex items-center justify-center px-1.5 py-[1px] text-[10px] leading-tight font-mono font-medium text-fg-muted bg-bg-surface border border-border rounded-[2px] whitespace-nowrap select-none"
  >
    <slot>{{ keys }}</slot>
  </kbd>
</template>
