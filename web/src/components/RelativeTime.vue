<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { formatDateTime } from '../views/runs/formatters'

const props = withDefaults(
  defineProps<{
    value?: string | null
    live?: boolean
  }>(),
  {
    value: null,
    live: true
  }
)

const now = ref(Date.now())
let timer: ReturnType<typeof setInterval> | null = null

onMounted(() => {
  if (props.live) {
    timer = setInterval(() => {
      now.value = Date.now()
    }, 30_000)
  }
})

onBeforeUnmount(() => {
  if (timer) clearInterval(timer)
})

const fullTitle = computed(() => formatDateTime(props.value))

const relativeLabel = computed(() => {
  if (!props.value) return '-'
  const ts = new Date(props.value).getTime()
  if (Number.isNaN(ts)) return '-'
  const diffSec = Math.floor((now.value - ts) / 1000)
  if (diffSec < 10) return 'vừa xong'
  if (diffSec < 60) return `${diffSec} giây trước`
  const diffMin = Math.floor(diffSec / 60)
  if (diffMin < 60) return `${diffMin} phút trước`
  const diffHr = Math.floor(diffMin / 60)
  if (diffHr < 24) return `${diffHr} giờ trước`
  const diffDay = Math.floor(diffHr / 24)
  if (diffDay < 30) return `${diffDay} ngày trước`
  return fullTitle.value
})
</script>

<template>
  <time
    :datetime="value || undefined"
    :title="fullTitle"
    class="font-mono tabular-nums whitespace-nowrap"
    data-testid="relative-time"
  >
    {{ relativeLabel }}
  </time>
</template>
