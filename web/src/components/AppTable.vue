<script setup lang="ts">
// Reusable table wrapper with sticky header, 40px rows, tabular-nums, overflow-x-auto
</script>

<template>
  <div class="w-full flex-1 min-h-0 max-h-full overflow-auto rounded-[8px] border border-border bg-card">
    <table class="w-full text-left text-sm tabular-nums border-collapse">
      <thead class="sticky top-0 z-10 bg-muted/80 backdrop-blur-xs text-xs font-semibold text-muted-fg border-b border-border">
        <slot name="header" />
      </thead>
      <tbody class="divide-y divide-border text-fg">
        <slot />
      </tbody>
    </table>
  </div>
</template>

<style scoped>
:deep(thead tr) {
  height: 40px;
}
:deep(tbody tr) {
  height: 46px;
  transition: background-color 150ms ease-out;
}
:deep(tbody tr:hover) {
  background-color: var(--color-muted, rgba(39, 47, 66, 0.5));
}
:deep(th),
:deep(td) {
  padding-left: 1rem;
  padding-right: 1rem;
  vertical-align: middle;
}
</style>
