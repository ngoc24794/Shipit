<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { Copy, Check, FileCode, Loader2 } from 'lucide-vue-next'
import type { JobPreviewTask } from '../../api/types'
import { useToast } from '../../composables/useToast'

const props = withDefaults(
  defineProps<{
    tasks: JobPreviewTask[]
    loading?: boolean
    errors?: Record<string, string[]>
  }>(),
  {
    loading: false,
    errors: () => ({})
  }
)

const toast = useToast()
const activeIndex = ref(0)
const copied = ref(false)

// Keep active index in bounds when tasks change
watch(
  () => props.tasks,
  (newTasks) => {
    if (activeIndex.value >= newTasks.length) {
      activeIndex.value = Math.max(0, newTasks.length - 1)
    }
  },
  { deep: true }
)

const currentTask = computed(() => {
  return props.tasks[activeIndex.value] || null
})

const currentYaml = computed(() => {
  return currentTask.value?.yaml || ''
})

async function copyYaml() {
  if (!currentYaml.value) return
  try {
    await navigator.clipboard.writeText(currentYaml.value)
    copied.value = true
    toast.success('Đã sao chép YAML vào bộ nhớ tạm')
    setTimeout(() => {
      copied.value = false
    }, 2000)
  } catch {
    toast.error('Không thể sao chép vào bộ nhớ tạm')
  }
}
</script>

<template>
  <div class="rounded-[2px] border border-border bg-bg-elevated overflow-hidden flex flex-col">
    <!-- Top toolbar: Tabs & Copy button -->
    <div class="px-3 py-2 bg-bg-surface border-b border-border flex flex-nowrap items-center justify-between gap-2 select-none">
      <!-- Tabs per task -->
      <div v-if="tasks.length > 0" class="flex items-center gap-1 overflow-x-auto max-w-full">
        <button
          v-for="(t, idx) in tasks"
          :key="t.position"
          type="button"
          :class="[
            'px-2.5 py-1 text-xs font-mono rounded-[2px] border transition-colors cursor-pointer flex items-center gap-1.5 whitespace-nowrap',
            'focus-visible:outline-2 focus-visible:outline-border-focus',
            activeIndex === idx
              ? 'bg-phosphor-bg text-phosphor font-semibold border-phosphor'
              : 'bg-bg-elevated text-fg-muted border-border hover:text-fg-strong hover:bg-bg-hover'
          ]"
          @click="activeIndex = idx"
        >
          <FileCode class="w-3.5 h-3.5" aria-hidden="true" />
          <span>{{ t.label || `Tác vụ #${t.position}` }}</span>
        </button>
      </div>

      <div v-else class="text-xs text-fg-muted">
        Bản xem trước YAML
      </div>

      <!-- Copy Button -->
      <button
        v-if="tasks.length > 0 && currentYaml"
        type="button"
        :aria-label="copied ? 'Đã sao chép' : 'Sao chép YAML vào bộ nhớ tạm'"
        class="inline-flex items-center gap-1.5 h-7 px-2.5 text-xs font-medium rounded-[2px] border border-border bg-bg-elevated text-fg hover:bg-bg-hover transition-colors cursor-pointer focus-visible:outline-2 focus-visible:outline-border-focus"
        @click="copyYaml"
      >
        <Check v-if="copied" class="w-3.5 h-3.5 text-phosphor" aria-hidden="true" />
        <Copy v-else class="w-3.5 h-3.5 text-fg-muted" aria-hidden="true" />
        <span>{{ copied ? 'Đã sao chép' : 'Sao chép' }}</span>
      </button>
    </div>

    <!-- Content area: Code preview -->
    <div class="relative flex-1 min-h-[180px] overflow-auto bg-bg-input p-3 font-mono text-xs text-phosphor">
      <div v-if="loading" class="flex items-center justify-center py-12 text-fg-muted gap-2">
        <Loader2 class="w-4 h-4 animate-spin" aria-hidden="true" />
        <span>Đang tạo bản xem trước YAML...</span>
      </div>

      <div v-else-if="tasks.length === 0" class="flex flex-col items-center justify-center py-12 text-fg-muted gap-2 text-center">
        <FileCode class="w-7 h-7 opacity-40" aria-hidden="true" />
        <span>Chưa có tác vụ nào để tạo bản xem trước.</span>
      </div>

      <pre v-else class="whitespace-pre overflow-x-auto select-text leading-relaxed"><code>{{ currentYaml }}</code></pre>
    </div>
  </div>
</template>
