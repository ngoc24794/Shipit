<script setup lang="ts">
import { ref, watch, nextTick, onMounted, computed } from 'vue'
import { Copy, Download, Radio, Check } from 'lucide-vue-next'
import type { LogLine, RunStatus } from '../api/types'
import { useToast } from '../composables/useToast'

const props = withDefaults(
  defineProps<{
    lines: LogLine[]
    status?: RunStatus | null
    runId?: number | string
  }>(),
  {
    status: null,
    runId: undefined
  }
)

const emit = defineEmits<{
  (e: 'toggleFollow', following: boolean): void
}>()

const scrollContainer = ref<HTMLElement | null>(null)
const autoScroll = ref(true)
const copied = ref(false)
const toast = useToast()

function isAtBottom(): boolean {
  if (!scrollContainer.value) return true
  const { scrollTop, scrollHeight, clientHeight } = scrollContainer.value
  return scrollHeight - scrollTop - clientHeight <= 25
}

function scrollToBottom() {
  if (scrollContainer.value) {
    scrollContainer.value.scrollTop = scrollContainer.value.scrollHeight
  }
}

function handleScroll() {
  if (!scrollContainer.value) return
  const atBottom = isAtBottom()
  if (autoScroll.value && !atBottom) {
    autoScroll.value = false
    emit('toggleFollow', false)
  } else if (!autoScroll.value && atBottom) {
    autoScroll.value = true
    emit('toggleFollow', true)
  }
}

function toggleAutoScroll() {
  autoScroll.value = !autoScroll.value
  emit('toggleFollow', autoScroll.value)
  if (autoScroll.value) {
    scrollToBottom()
  }
}

watch(
  () => props.lines.length,
  async () => {
    if (autoScroll.value) {
      await nextTick()
      scrollToBottom()
    }
  }
)

onMounted(() => {
  if (autoScroll.value) {
    scrollToBottom()
  }
})

async function copyLogs() {
  const text = props.lines.map((l) => `[${l.stream.toUpperCase()}] ${l.text}`).join('\n')
  try {
    if (navigator?.clipboard?.writeText) {
      await navigator.clipboard.writeText(text)
    }
    copied.value = true
    toast.success('Đã sao chép nhật ký vào bộ nhớ tạm')
    setTimeout(() => {
      copied.value = false
    }, 2000)
  } catch {
    toast.error('Không thể sao chép nhật ký')
  }
}

function downloadLogs() {
  const text = props.lines.map((l) => `[${l.at}] [${l.step}] [${l.stream.toUpperCase()}] ${l.text}`).join('\n')
  const blob = new Blob([text], { type: 'text/plain;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `shipit-run-${props.runId || 'logs'}.txt`
  document.body.appendChild(a)
  a.click()
  document.body.removeChild(a)
  URL.revokeObjectURL(url)
  toast.success('Đã bắt đầu tải xuống tệp nhật ký')
}

const statusMessage = computed(() => {
  if (props.status === 'success') {
    return 'Run đã kết thúc: thành công'
  }
  if (props.status === 'failed') {
    return 'Run đã kết thúc: thất bại'
  }
  if (props.status === 'running') {
    return 'Đang ghi nhận nhật ký...'
  }
  if (props.status === 'queued') {
    return 'Đang chờ thực thi...'
  }
  return null
})
</script>

<template>
  <div class="flex flex-col w-full rounded-[8px] overflow-hidden border border-border bg-[var(--log-bg,#090D16)] text-slate-200">
    <!-- Toolbar -->
    <div class="flex items-center justify-between px-4 py-2 bg-slate-900/90 border-b border-slate-800 text-xs select-none">
      <div class="flex items-center gap-2">
        <span class="font-mono text-slate-400">Nhật ký thực thi ({{ lines.length }} dòng)</span>
        <!-- Status Notification for Screen Readers and UI -->
        <span
          v-if="statusMessage"
          role="status"
          class="ml-2 px-2 py-0.5 rounded text-[11px] font-medium"
          :class="[
            status === 'success' ? 'bg-primary/20 text-primary border border-primary/40' : '',
            status === 'failed' ? 'bg-danger/20 text-danger border border-danger/40' : '',
            status === 'running' ? 'bg-info/20 text-info border border-info/40' : 'text-slate-400'
          ]"
        >
          {{ statusMessage }}
        </span>
      </div>

      <div class="flex items-center gap-1.5">
        <!-- Follow / Auto-scroll Toggle Button -->
        <button
          type="button"
          :class="[
            'inline-flex items-center gap-1.5 px-2.5 py-1 rounded-[6px] text-xs font-medium transition-colors cursor-pointer',
            autoScroll
              ? 'bg-primary/20 text-primary border border-primary/40 hover:bg-primary/30'
              : 'bg-slate-800 text-slate-400 hover:text-slate-200 hover:bg-slate-700'
          ]"
          :aria-pressed="autoScroll ? 'true' : 'false'"
          @click="toggleAutoScroll"
        >
          <Radio class="w-3.5 h-3.5" :class="autoScroll ? 'animate-pulse' : ''" aria-hidden="true" />
          <span>Theo dõi</span>
        </button>

        <!-- Copy Button -->
        <button
          type="button"
          aria-label="Sao chép nhật ký"
          class="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-[6px] text-xs font-medium bg-slate-800 text-slate-300 hover:text-white hover:bg-slate-700 transition-colors cursor-pointer"
          @click="copyLogs"
        >
          <Check v-if="copied" class="w-3.5 h-3.5 text-primary" aria-hidden="true" />
          <Copy v-else class="w-3.5 h-3.5" aria-hidden="true" />
          <span>Sao chép</span>
        </button>

        <!-- Download Button -->
        <button
          type="button"
          aria-label="Tải xuống nhật ký"
          class="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-[6px] text-xs font-medium bg-slate-800 text-slate-300 hover:text-white hover:bg-slate-700 transition-colors cursor-pointer"
          @click="downloadLogs"
        >
          <Download class="w-3.5 h-3.5" aria-hidden="true" />
          <span>Tải xuống</span>
        </button>
      </div>
    </div>

    <!-- Log Lines Container -->
    <div
      ref="scrollContainer"
      role="log"
      aria-live="off"
      tabindex="0"
      class="h-[480px] overflow-y-auto overflow-x-auto p-4 font-mono text-[13px] leading-relaxed select-text focus-visible:outline-2 focus-visible:outline-ring"
      @scroll="handleScroll"
    >
      <div v-if="lines.length === 0" class="text-slate-500 italic py-8 text-center">
        Chưa có dữ liệu nhật ký...
      </div>

      <div
        v-for="line in lines"
        :key="line.seq"
        class="flex items-start hover:bg-slate-800/40 rounded px-1 -mx-1"
      >
        <!-- Sequence number -->
        <span class="w-12 flex-shrink-0 text-slate-600 select-none text-right pr-4 tabular-nums text-xs">
          {{ line.seq }}
        </span>

        <!-- Stream indicator tag (optional short badge for stderr/system) -->
        <span
          v-if="line.stream === 'stderr'"
          class="text-xs uppercase font-semibold text-rose-400 mr-2 select-none"
        >
          [err]
        </span>

        <!-- Line content -->
        <span
          class="flex-1 whitespace-pre-wrap break-all"
          :class="[
            line.stream === 'stdout' ? 'text-slate-200' : '',
            line.stream === 'stderr' ? 'text-rose-400 font-medium' : '',
            line.stream === 'system' ? 'text-sky-400 italic' : ''
          ]"
        >
          {{ line.text }}
        </span>
      </div>
    </div>
  </div>
</template>
