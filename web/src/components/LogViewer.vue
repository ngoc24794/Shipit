<script setup lang="ts">
import { ref, watch, nextTick, onMounted, onBeforeUnmount, computed } from 'vue'
import { Copy, Download, Radio, Check, ChevronDown, ChevronRight, Filter, Search, ChevronUp, X } from 'lucide-vue-next'
import type { LogLine, RunStatus, RunTask } from '../api/types'
import { useToast } from '../composables/useToast'
import { classifyLogLine, type LogSeverity } from '../utils/logSeverity'
import StatusBadge from './StatusBadge.vue'
import AppSelect from './AppSelect.vue'

const props = withDefaults(
  defineProps<{
    lines: LogLine[]
    status?: RunStatus | null
    runId?: number | string
    tasks?: RunTask[]
    isJob?: boolean
  }>(),
  {
    status: null,
    runId: undefined,
    tasks: () => [],
    isJob: false
  }
)

const emit = defineEmits<{
  (e: 'toggleFollow', following: boolean): void
  (e: 'update:selectedTask', value: string | number): void
}>()

const scrollContainer = ref<HTMLElement | null>(null)
const toolbarRef = ref<HTMLElement | null>(null)
const searchInputRef = ref<HTMLInputElement | null>(null)
const toolbarWidth = ref(900)
const autoScroll = ref(true)
const copied = ref(false)
const selectedTaskFilter = ref<string | number>('all')
const collapsedGroups = ref<Set<string | number>>(new Set())
const searchQuery = ref('')
const activeMatchIndex = ref(0)
const toast = useToast()

const isCompactToolbar = computed(() => toolbarWidth.value < 900)
const isWrapToolbar = computed(() => toolbarWidth.value < 620)

let toolbarObserver: ResizeObserver | null = null

function focusSearch() {
  searchInputRef.value?.focus()
  searchInputRef.value?.select()
}

watch(selectedTaskFilter, (val) => {
  emit('update:selectedTask', val)
})

const isGrouped = computed(() => {
  return props.isJob || (props.tasks && props.tasks.length > 0)
})

function isGroupExpanded(key: string | number): boolean {
  return !collapsedGroups.value.has(key)
}

function toggleGroup(key: string | number) {
  if (collapsedGroups.value.has(key)) {
    collapsedGroups.value.delete(key)
  } else {
    collapsedGroups.value.add(key)
  }
}

function focusTask(position: number) {
  collapsedGroups.value.delete(position)
  selectedTaskFilter.value = position
  nextTick(() => {
    const el = document.getElementById(`task-group-${position}`)
    if (el && typeof el.scrollIntoView === 'function') {
      const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches
      el.scrollIntoView({ behavior: prefersReducedMotion ? 'auto' : 'smooth', block: 'start' })
    }
  })
}

defineExpose({
  focusTask,
  focusSearch,
  scrollToBottom,
  selectedTaskFilter
})

const prepLines = computed(() => {
  return props.lines.filter((l) => l.taskPosition === undefined || l.taskPosition === null)
})

const taskGroups = computed(() => {
  return (props.tasks || []).map((task) => ({
    task,
    lines: props.lines.filter((l) => l.taskPosition === task.position)
  }))
})

const visibleLinesForSearch = computed<LogLine[]>(() => {
  if (!isGrouped.value) return props.lines
  if (selectedTaskFilter.value === 'prep') return prepLines.value
  if (selectedTaskFilter.value === 'all') {
    const ordered: LogLine[] = [...prepLines.value]
    for (const g of taskGroups.value) {
      ordered.push(...g.lines)
    }
    return ordered
  }
  const pos = Number(selectedTaskFilter.value)
  return props.lines.filter((l) => l.taskPosition === pos)
})

const normalizedQuery = computed(() => searchQuery.value.trim().toLowerCase())

const matchedLineSeqs = computed<number[]>(() => {
  const q = normalizedQuery.value
  if (!q) return []
  return visibleLinesForSearch.value
    .filter((l) => l.text.toLowerCase().includes(q))
    .map((l) => l.seq)
})

const matchedSeqSet = computed(() => new Set(matchedLineSeqs.value))

const activeMatchSeq = computed<number | null>(() => {
  if (matchedLineSeqs.value.length === 0) return null
  const idx = Math.min(activeMatchIndex.value, matchedLineSeqs.value.length - 1)
  return matchedLineSeqs.value[idx] ?? null
})

function scrollToSeq(seq: number) {
  const line = props.lines.find((l) => l.seq === seq)
  if (line && isGrouped.value) {
    const groupKey = line.taskPosition === undefined || line.taskPosition === null ? 'prep' : line.taskPosition
    collapsedGroups.value.delete(groupKey)
  }
  nextTick(() => {
    const el = scrollContainer.value?.querySelector(`[data-log-seq="${seq}"]`) as HTMLElement | null
    if (el && typeof el.scrollIntoView === 'function') {
      const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches
      el.scrollIntoView({ behavior: prefersReducedMotion ? 'auto' : 'smooth', block: 'center' })
    }
  })
}

watch(normalizedQuery, () => {
  activeMatchIndex.value = 0
  if (matchedLineSeqs.value.length > 0) {
    autoScroll.value = false
    scrollToSeq(matchedLineSeqs.value[0])
  }
})

watch(selectedTaskFilter, () => {
  activeMatchIndex.value = 0
  if (matchedLineSeqs.value.length > 0) {
    scrollToSeq(matchedLineSeqs.value[0])
  }
})

function goToNextMatch() {
  const total = matchedLineSeqs.value.length
  if (total === 0) return
  activeMatchIndex.value = (activeMatchIndex.value + 1) % total
  autoScroll.value = false
  scrollToSeq(matchedLineSeqs.value[activeMatchIndex.value])
}

function goToPrevMatch() {
  const total = matchedLineSeqs.value.length
  if (total === 0) return
  activeMatchIndex.value = (activeMatchIndex.value - 1 + total) % total
  autoScroll.value = false
  scrollToSeq(matchedLineSeqs.value[activeMatchIndex.value])
}

function clearSearch() {
  searchQuery.value = ''
  activeMatchIndex.value = 0
}

function splitHighlight(text: string): { text: string; match: boolean }[] {
  const q = normalizedQuery.value
  if (!q) return [{ text, match: false }]
  const lower = text.toLowerCase()
  const parts: { text: string; match: boolean }[] = []
  let start = 0
  let idx = lower.indexOf(q, start)
  while (idx !== -1) {
    if (idx > start) {
      parts.push({ text: text.slice(start, idx), match: false })
    }
    parts.push({ text: text.slice(idx, idx + q.length), match: true })
    start = idx + q.length
    idx = lower.indexOf(q, start)
  }
  if (start < text.length) {
    parts.push({ text: text.slice(start), match: false })
  }
  return parts.length > 0 ? parts : [{ text, match: false }]
}

function getSeverity(line: LogLine): LogSeverity {
  return classifyLogLine(line)
}

function getLineClass(severity: LogSeverity): string {
  switch (severity) {
    case 'error':
      return 'log-err text-danger font-medium'
    case 'warning':
      return 'log-warn text-warn font-medium'
    case 'system-error':
      return 'log-err text-danger font-bold'
    case 'system':
      return 'log-sys text-phosphor italic'
    case 'muted':
      return 'text-fg-muted'
    case 'normal':
    default:
      return 'text-fg'
  }
}

function getBadge(severity: LogSeverity): { text: string; class: string } | null {
  if (severity === 'error') {
    return { text: 'ERR', class: 'text-danger font-bold' }
  }
  if (severity === 'warning') {
    return { text: 'WARN', class: 'text-warn font-semibold' }
  }
  return null
}

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
  if (typeof ResizeObserver !== 'undefined' && toolbarRef.value) {
    toolbarWidth.value = toolbarRef.value.clientWidth || 900
    toolbarObserver = new ResizeObserver((entries) => {
      for (const entry of entries) {
        toolbarWidth.value = entry.contentRect.width
      }
    })
    toolbarObserver.observe(toolbarRef.value)
  }
})

onBeforeUnmount(() => {
  if (toolbarObserver) {
    toolbarObserver.disconnect()
    toolbarObserver = null
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

const shortStatusMessage = computed(() => {
  if (props.status === 'success') return 'Thành công'
  if (props.status === 'failed') return 'Thất bại'
  if (props.status === 'running') return 'Đang chạy'
  if (props.status === 'queued') return 'Đang chờ'
  return null
})
</script>

<template>
  <div class="flex flex-col flex-1 min-h-0 w-full rounded-[2px] overflow-hidden border border-border bg-bg-input text-fg font-mono">
    <!-- Toolbar (container-width responsive via ResizeObserver) -->
    <div
      ref="toolbarRef"
      data-testid="log-viewer-toolbar"
      :data-compact="isCompactToolbar ? 'true' : 'false'"
      :class="[
        'shrink-0 flex items-center justify-between gap-2 px-2.5 py-1.5 bg-bg-surface border-b border-border text-xs select-none',
        isWrapToolbar ? 'flex-wrap' : 'flex-nowrap'
      ]"
    >
      <div class="flex items-center gap-1.5 flex-nowrap min-w-0 shrink">
        <span class="font-mono text-fg-muted whitespace-nowrap shrink-0">Nhật ký ({{ lines.length }} dòng)</span>

        <!-- Task Filter Dropdown for Grouped Job Mode -->
        <div v-if="isGrouped && tasks.length > 0" class="flex items-center gap-1 ml-0.5 shrink-0">
          <Filter class="w-3.5 h-3.5 text-fg-muted shrink-0" aria-hidden="true" />
          <div class="w-[145px] shrink-0">
            <AppSelect
              id="task-filter"
              v-model="selectedTaskFilter"
              aria-label="Lọc theo tác vụ"
              size="sm"
              :options="[
                { label: 'Tất cả tác vụ', value: 'all' },
                { label: 'Chuẩn bị', value: 'prep' },
                ...tasks.map((t) => ({ label: `Tác vụ ${t.position + 1}: ${t.label}`, value: t.position }))
              ]"
            />
          </div>
        </div>

        <!-- Status Notification for Screen Readers and UI -->
        <span
          v-if="statusMessage"
          role="status"
          :title="statusMessage"
          class="px-1.5 py-0.5 rounded-[2px] text-[11px] font-medium whitespace-nowrap shrink-0"
          :class="[
            status === 'success' ? 'bg-phosphor-bg text-phosphor border border-phosphor/30' : '',
            status === 'failed' ? 'bg-danger-bg text-danger border border-danger/30' : '',
            status === 'running' ? 'bg-accent-bg text-accent border border-accent/30' : 'text-fg-muted'
          ]"
        >
          {{ isCompactToolbar ? shortStatusMessage : statusMessage }}
        </span>
      </div>

      <div class="flex items-center gap-1.5 flex-nowrap min-w-0 shrink-0">
        <!-- Search in Log -->
        <div class="flex items-center gap-1 bg-bg-input border border-border focus-within:border-border-focus rounded-[2px] px-1.5 h-7 min-w-[140px] flex-1 max-w-[240px]">
          <Search class="w-3.5 h-3.5 text-fg-dim shrink-0" aria-hidden="true" />
          <input
            ref="searchInputRef"
            v-model="searchQuery"
            type="text"
            aria-label="Tìm trong nhật ký"
            placeholder="Tìm log..."
            class="flex-1 min-w-0 bg-transparent text-fg-strong placeholder:text-fg-dim text-xs focus:outline-none"
            @keydown.enter.exact.prevent="goToNextMatch"
            @keydown.enter.shift.prevent="goToPrevMatch"
          />
          <button
            v-if="searchQuery"
            type="button"
            aria-label="Xoá tìm kiếm"
            class="p-0.5 text-fg-muted hover:text-fg-strong cursor-pointer shrink-0"
            @click="clearSearch"
          >
            <X class="w-3 h-3" aria-hidden="true" />
          </button>
          <span
            v-if="normalizedQuery"
            data-testid="log-search-count"
            class="text-[11px] font-mono tabular-nums text-fg-muted whitespace-nowrap pl-1 border-l border-border shrink-0"
          >
            {{ matchedLineSeqs.length > 0 ? `${activeMatchIndex + 1}/${matchedLineSeqs.length}` : '0/0' }}
          </span>
          <button
            v-if="normalizedQuery"
            type="button"
            aria-label="Kết quả trước"
            title="Kết quả trước"
            :disabled="matchedLineSeqs.length === 0"
            class="p-0.5 rounded-[2px] text-fg-muted hover:text-fg-strong hover:bg-bg-hover disabled:opacity-40 cursor-pointer disabled:cursor-default shrink-0"
            @click="goToPrevMatch"
          >
            <ChevronUp class="w-3.5 h-3.5" aria-hidden="true" />
          </button>
          <button
            v-if="normalizedQuery"
            type="button"
            aria-label="Kết quả sau"
            title="Kết quả sau"
            :disabled="matchedLineSeqs.length === 0"
            class="p-0.5 rounded-[2px] text-fg-muted hover:text-fg-strong hover:bg-bg-hover disabled:opacity-40 cursor-pointer disabled:cursor-default shrink-0"
            @click="goToNextMatch"
          >
            <ChevronDown class="w-3.5 h-3.5" aria-hidden="true" />
          </button>
        </div>

        <!-- Follow / Auto-scroll Toggle Button -->
        <button
          type="button"
          :aria-label="autoScroll ? 'Tắt tự động cuộn theo dõi' : 'Bật tự động cuộn theo dõi'"
          title="Theo dõi nhật ký (cuộn tự động)"
          :class="[
            'inline-flex items-center justify-center gap-1 h-7 rounded-[2px] text-xs font-medium border transition-colors cursor-pointer whitespace-nowrap shrink-0',
            isCompactToolbar ? 'w-7 px-0' : 'px-2',
            autoScroll
              ? 'bg-phosphor-bg text-phosphor border-phosphor/50 hover:bg-phosphor hover:text-bg'
              : 'bg-bg-elevated text-fg-muted border-border hover:text-fg-strong hover:bg-bg-hover'
          ]"
          :aria-pressed="autoScroll ? 'true' : 'false'"
          @click="toggleAutoScroll"
        >
          <Radio class="w-3.5 h-3.5 shrink-0" :class="autoScroll ? 'live-pulse' : ''" aria-hidden="true" />
          <span v-if="!isCompactToolbar">Theo dõi</span>
        </button>

        <!-- Copy Button -->
        <button
          type="button"
          aria-label="Sao chép nhật ký"
          title="Sao chép nhật ký"
          :class="[
            'inline-flex items-center justify-center gap-1 h-7 rounded-[2px] text-xs font-medium border border-border bg-bg-elevated text-fg-muted hover:text-fg-strong hover:bg-bg-hover transition-colors cursor-pointer whitespace-nowrap shrink-0',
            isCompactToolbar ? 'w-7 px-0' : 'px-2'
          ]"
          @click="copyLogs"
        >
          <Check v-if="copied" class="w-3.5 h-3.5 text-phosphor shrink-0" aria-hidden="true" />
          <Copy v-else class="w-3.5 h-3.5 shrink-0" aria-hidden="true" />
          <span v-if="!isCompactToolbar">Sao chép</span>
        </button>

        <!-- Download Button -->
        <button
          type="button"
          aria-label="Tải xuống nhật ký"
          title="Tải xuống nhật ký"
          :class="[
            'inline-flex items-center justify-center gap-1 h-7 rounded-[2px] text-xs font-medium border border-border bg-bg-elevated text-fg-muted hover:text-fg-strong hover:bg-bg-hover transition-colors cursor-pointer whitespace-nowrap shrink-0',
            isCompactToolbar ? 'w-7 px-0' : 'px-2'
          ]"
          @click="downloadLogs"
        >
          <Download class="w-3.5 h-3.5 shrink-0" aria-hidden="true" />
          <span v-if="!isCompactToolbar">Tải xuống</span>
        </button>
      </div>
    </div>

    <!-- Log Lines Container -->
    <div
      ref="scrollContainer"
      role="log"
      aria-live="off"
      tabindex="0"
      class="flex-1 min-h-0 overflow-auto p-2.5 font-mono text-[12px] leading-[1.55] tabular-nums select-text focus-visible:outline-2 focus-visible:outline-border-focus"
      @scroll="handleScroll"
    >
      <!-- Grouped View (For Job Runs) -->
      <template v-if="isGrouped">
        <!-- Group: Chuẩn bị (lines with taskPosition == null) -->
        <div
          v-show="selectedTaskFilter === 'all' || selectedTaskFilter === 'prep'"
          id="task-group-prep"
          class="mb-3 rounded-[2px] border border-border bg-bg-elevated overflow-hidden"
        >
          <button
            type="button"
            class="w-full flex items-center justify-between px-2.5 py-1.5 bg-bg-surface hover:bg-bg-hover text-left transition-colors cursor-pointer focus-visible:outline-2 focus-visible:outline-border-focus"
            :aria-expanded="isGroupExpanded('prep')"
            aria-label="Thu gọn hoặc mở rộng nhóm Chuẩn bị"
            @click="toggleGroup('prep')"
          >
            <div class="flex items-center gap-2">
              <component
                :is="isGroupExpanded('prep') ? ChevronDown : ChevronRight"
                class="w-3.5 h-3.5 text-fg-muted"
                aria-hidden="true"
              />
              <span class="font-semibold text-xs text-fg-strong">Chuẩn bị</span>
              <span class="text-[11px] text-fg-muted font-mono">({{ prepLines.length }} dòng)</span>
            </div>
          </button>

          <div v-show="isGroupExpanded('prep')" class="p-2 bg-bg-input space-y-0.5 overflow-x-auto">
            <div v-if="prepLines.length === 0" class="text-fg-dim italic py-2 px-3 text-xs">
              Chưa có dữ liệu nhật ký chuẩn bị...
            </div>
            <div
              v-for="line in prepLines"
              :key="line.seq"
              :data-log-seq="line.seq"
              :title="line.stream"
              :class="[
                'flex items-start hover:bg-bg-hover rounded-[1px] px-1 -mx-1 min-w-max',
                activeMatchSeq === line.seq ? 'bg-warn-bg ring-1 ring-inset ring-warn' : matchedSeqSet.has(line.seq) ? 'bg-warn-bg/50' : ''
              ]"
            >
              <span class="w-12 flex-shrink-0 text-fg-dim select-none text-right pr-3 tabular-nums text-xs">
                {{ line.seq }}
              </span>
              <span class="inline-block w-10 flex-shrink-0 select-none text-xs text-center mr-2">
                <span v-if="getBadge(getSeverity(line))" :class="getBadge(getSeverity(line))!.class">
                  {{ getBadge(getSeverity(line))!.text }}
                </span>
              </span>
              <span class="whitespace-pre" :class="getLineClass(getSeverity(line))">
                <template v-for="(part, pIdx) in splitHighlight(line.text)" :key="pIdx">
                  <mark
                    v-if="part.match"
                    :class="activeMatchSeq === line.seq ? 'bg-warn text-bg font-semibold rounded-[1px] px-0.5' : 'bg-warn-bg text-warn rounded-[1px] px-0.5'"
                  >{{ part.text }}</mark>
                  <template v-else>{{ part.text }}</template>
                </template>
              </span>
            </div>
          </div>
        </div>

        <!-- Task Groups -->
        <div
          v-for="group in taskGroups"
          v-show="selectedTaskFilter === 'all' || String(selectedTaskFilter) === String(group.task.position)"
          :id="`task-group-${group.task.position}`"
          :key="group.task.position"
          class="mb-3 rounded-[2px] border border-border bg-bg-elevated overflow-hidden"
        >
          <button
            type="button"
            class="w-full flex items-center justify-between px-2.5 py-1.5 bg-bg-surface hover:bg-bg-hover text-left transition-colors cursor-pointer focus-visible:outline-2 focus-visible:outline-border-focus"
            :aria-expanded="isGroupExpanded(group.task.position)"
            :aria-label="`Thu gọn hoặc mở rộng Tác vụ ${group.task.position + 1} — ${group.task.label}`"
            @click="toggleGroup(group.task.position)"
          >
            <div class="flex items-center gap-2 min-w-0">
              <component
                :is="isGroupExpanded(group.task.position) ? ChevronDown : ChevronRight"
                class="w-3.5 h-3.5 text-fg-muted shrink-0"
                aria-hidden="true"
              />
              <span class="font-semibold text-xs text-fg-strong truncate">
                Tác vụ {{ group.task.position + 1 }} — {{ group.task.label }}
              </span>
              <StatusBadge
                :status="group.task.status"
                size="sm"
                :label="group.task.status === 'pending' ? 'Đang chờ' : (group.task.status === 'cancelled' ? 'Đã huỷ' : undefined)"
              />
            </div>
            <span class="text-[11px] text-fg-muted font-mono shrink-0 ml-2">
              ({{ group.lines.length }} dòng)
            </span>
          </button>

          <div v-show="isGroupExpanded(group.task.position)" class="p-2 bg-bg-input space-y-0.5 overflow-x-auto">
            <div v-if="group.lines.length === 0" class="text-fg-dim italic py-2 px-3 text-xs">
              Chưa có dữ liệu nhật ký cho tác vụ này...
            </div>
            <div
              v-for="line in group.lines"
              :key="line.seq"
              :data-log-seq="line.seq"
              :title="line.stream"
              :class="[
                'flex items-start hover:bg-bg-hover rounded-[1px] px-1 -mx-1 min-w-max',
                activeMatchSeq === line.seq ? 'bg-warn-bg ring-1 ring-inset ring-warn' : matchedSeqSet.has(line.seq) ? 'bg-warn-bg/50' : ''
              ]"
            >
              <span class="w-12 flex-shrink-0 text-fg-dim select-none text-right pr-3 tabular-nums text-xs">
                {{ line.seq }}
              </span>
              <span class="inline-block w-10 flex-shrink-0 select-none text-xs text-center mr-2">
                <span v-if="getBadge(getSeverity(line))" :class="getBadge(getSeverity(line))!.class">
                  {{ getBadge(getSeverity(line))!.text }}
                </span>
              </span>
              <span class="whitespace-pre" :class="getLineClass(getSeverity(line))">
                <template v-for="(part, pIdx) in splitHighlight(line.text)" :key="pIdx">
                  <mark
                    v-if="part.match"
                    :class="activeMatchSeq === line.seq ? 'bg-warn text-bg font-semibold rounded-[1px] px-0.5' : 'bg-warn-bg text-warn rounded-[1px] px-0.5'"
                  >{{ part.text }}</mark>
                  <template v-else>{{ part.text }}</template>
                </template>
              </span>
            </div>
          </div>
        </div>
      </template>

      <!-- Flat View (For Standard Pipeline Runs) -->
      <template v-else>
        <div v-if="lines.length === 0" class="text-fg-dim italic py-8 text-center">
          Chưa có dữ liệu nhật ký...
        </div>

        <div
          v-for="line in lines"
          :key="line.seq"
          :data-log-seq="line.seq"
          :title="line.stream"
          :class="[
            'flex items-start hover:bg-bg-hover rounded-[1px] px-1 -mx-1 min-w-max',
            activeMatchSeq === line.seq ? 'bg-warn-bg ring-1 ring-inset ring-warn' : matchedSeqSet.has(line.seq) ? 'bg-warn-bg/50' : ''
          ]"
        >
          <!-- Sequence number -->
          <span class="w-12 flex-shrink-0 text-fg-dim select-none text-right pr-3 tabular-nums text-xs">
            {{ line.seq }}
          </span>

          <!-- Fixed-width badge column -->
          <span class="inline-block w-10 flex-shrink-0 select-none text-xs text-center mr-2">
            <span
              v-if="getBadge(getSeverity(line))"
              :class="getBadge(getSeverity(line))!.class"
            >
              {{ getBadge(getSeverity(line))!.text }}
            </span>
          </span>

          <!-- Line content -->
          <span
            class="whitespace-pre"
            :class="getLineClass(getSeverity(line))"
          >
            <template v-for="(part, pIdx) in splitHighlight(line.text)" :key="pIdx">
              <mark
                v-if="part.match"
                :class="activeMatchSeq === line.seq ? 'bg-warn text-bg font-semibold rounded-[1px] px-0.5' : 'bg-warn-bg text-warn rounded-[1px] px-0.5'"
              >{{ part.text }}</mark>
              <template v-else>{{ part.text }}</template>
            </template>
          </span>
        </div>
      </template>
    </div>
  </div>
</template>
