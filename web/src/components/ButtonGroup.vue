<script setup lang="ts">
withDefaults(
  defineProps<{
    ariaLabel?: string
    attached?: boolean
  }>(),
  {
    ariaLabel: undefined,
    attached: true
  }
)
</script>

<template>
  <div
    role="group"
    :aria-label="ariaLabel"
    :class="[
      'inline-flex items-center',
      attached
        ? '[&>*]:rounded-none [&>*:first-child]:rounded-l-[2px] [&>*:last-child]:rounded-r-[2px] [&>*:not(:first-child)]:-ml-px'
        : 'gap-1'
    ]"
  >
    <slot />
  </div>
</template>
