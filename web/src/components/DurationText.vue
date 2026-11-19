<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { formatDuration } from '../views/runs/formatters'

const props = withDefaults(
  defineProps<{
    startedAt?: string | null
    finishedAt?: string | null
    running?: boolean
  }>(),
  {
    startedAt: null,
    finishedAt: null,
    running: false
  }
)

const now = ref(Date.now())
let timer: ReturnType<typeof setInterval> | null = null

function syncTimer() {
  if (timer) {
    clearInterval(timer)
    timer = null
  }
  if (props.running && props.startedAt && !props.finishedAt) {
    timer = setInterval(() => {
      now.value = Date.now()
    }, 1000)
  }
}

onMounted(syncTimer)
watch(() => [props.running, props.startedAt, props.finishedAt], syncTimer)
onBeforeUnmount(() => {
  if (timer) clearInterval(timer)
})

const label = computed(() => formatDuration(props.startedAt, props.finishedAt, now.value))
</script>

<template>
  <span class="font-mono tabular-nums whitespace-nowrap" data-testid="duration-text">
    {{ label }}
  </span>
</template>
