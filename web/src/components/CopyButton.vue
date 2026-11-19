<script setup lang="ts">
import { ref } from 'vue'
import { Copy, Check } from 'lucide-vue-next'
import { useToast } from '../composables/useToast'

const props = withDefaults(
  defineProps<{
    value: string
    label?: string
    ariaLabel?: string
    size?: 'xs' | 'sm'
    silent?: boolean
  }>(),
  {
    label: '',
    ariaLabel: 'Sao chép',
    size: 'xs',
    silent: false
  }
)

const emit = defineEmits<{
  (e: 'copied', value: string): void
}>()

const toast = useToast()
const copied = ref(false)
let resetTimer: ReturnType<typeof setTimeout> | null = null

async function handleCopy() {
  if (!props.value) return
  try {
    await navigator.clipboard.writeText(props.value)
    copied.value = true
    emit('copied', props.value)
    if (!props.silent) {
      toast.success('Đã sao chép vào bộ nhớ tạm')
    }
    if (resetTimer) clearTimeout(resetTimer)
    resetTimer = setTimeout(() => {
      copied.value = false
    }, 1800)
  } catch {
    if (!props.silent) {
      toast.error('Không thể sao chép vào bộ nhớ tạm')
    }
  }
}
</script>

<template>
  <button
    type="button"
    :aria-label="ariaLabel"
    :title="copied ? 'Đã sao chép' : ariaLabel"
    :class="[
      'inline-flex items-center justify-center gap-1 rounded-[2px] border border-border bg-bg-surface text-fg-muted hover:text-fg-strong hover:bg-bg-hover hover:border-border-strong transition-colors cursor-pointer whitespace-nowrap shrink-0 focus-visible:outline-2 focus-visible:outline-border-focus',
      size === 'xs' ? 'h-[22px] px-1.5 text-[11px]' : 'h-7 px-2 text-xs'
    ]"
    data-testid="copy-button"
    @click.stop="handleCopy"
  >
    <Check v-if="copied" class="w-3 h-3 text-phosphor shrink-0" aria-hidden="true" />
    <Copy v-else class="w-3 h-3 shrink-0" aria-hidden="true" />
    <span v-if="label">{{ copied ? 'Đã chép' : label }}</span>
  </button>
</template>
