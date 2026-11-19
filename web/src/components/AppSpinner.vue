<script setup lang="ts">
import { ref, onMounted, onBeforeUnmount, computed } from 'vue'

const props = withDefaults(
  defineProps<{
    size?: 'sm' | 'md' | 'lg'
    label?: string
  }>(),
  {
    size: 'md',
    label: undefined
  }
)

const FRAMES = ['⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏']
const frameIdx = ref(0)
let timer: ReturnType<typeof setInterval> | null = null

onMounted(() => {
  const reduced =
    typeof window !== 'undefined' &&
    window.matchMedia &&
    window.matchMedia('(prefers-reduced-motion: reduce)').matches
  if (!reduced) {
    timer = setInterval(() => {
      frameIdx.value = (frameIdx.value + 1) % FRAMES.length
    }, 80)
  }
})

onBeforeUnmount(() => {
  if (timer) clearInterval(timer)
})

const sizeClass = computed(() => {
  switch (props.size) {
    case 'sm':
      return 'text-xs leading-none'
    case 'lg':
      return 'text-base leading-none'
    case 'md':
    default:
      return 'text-sm leading-none'
  }
})
</script>

<template>
  <span
    :role="label ? 'status' : undefined"
    :aria-label="label"
    :aria-hidden="label ? undefined : 'true'"
    :class="['inline-flex items-center justify-center font-mono select-none text-current', sizeClass]"
  >
    {{ FRAMES[frameIdx] }}
  </span>
</template>
