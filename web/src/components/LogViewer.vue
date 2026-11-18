<script setup lang="ts">
import { ref, watch, nextTick, onMounted, computed } from 'vue'
import { Copy, Download, Radio, Check, ChevronDown, ChevronRight, Filter, Search, ChevronUp, X } from 'lucide-vue-next'
import type { LogLine, RunStatus, RunTask } from '../api/types'
import { useToast } from '../composables/useToast'
import { classifyLogLine, type LogSeverity } from '../utils/logSeverity'
import StatusBadge from './StatusBadge.vue'

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
const autoScroll = ref(true)
const copied = ref(false)
const selectedTaskFilter = ref<string | number>('all')
const collapsedGroups = ref<Set<string | number>>(new Set())
const searchQuery = ref('')
const activeMatchIndex = ref(0)
const toast = useToast()

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
      return 'text-rose-400 font-medium'
    case 'warning':
      return 'text-amber-400 font-medium'
    case 'system-error':
      return 'text-rose-400 font-bold'
    case 'system':
      return 'text-sky-400 italic'
    case 'muted':
      return 'text-slate-400'
    case 'normal':
    default:
      return 'text-slate-200'
  }
}

function getBadge(severity: LogSeverity): { text: string; class: string } | null {
  if (severity === 'error') {
    return { text: 'ERR', class: 'text-rose-400 font-bold' }
  }
  if (severity === 'warning') {
    return { text: 'WARN', class: 'text-amber-400 font-semibold' }
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
  <div class="flex flex-col flex-1 min-h-0 w-full rounded-[8px] overflow-hidden border border-border bg-[var(--log-bg,#090D16)] text-slate-200">
    <!-- Toolbar -->
    <div class="shrink-0 flex flex-nowrap items-center justify-between gap-2 px-3 py-1.5 bg-slate-900/90 border-b border-slate-800 text-xs select-none overflow-x-auto">
      <div class="flex items-center gap-2 flex-nowrap min-w-0">
        <span class="font-mono text-slate-400 whitespace-nowrap shrink-0">Nhật ký thực thi ({{ lines.length }} dòng)</span>

        <!-- Task Filter Dropdown for Grouped Job Mode -->
        <div v-if="isGrouped && tasks.length > 0" class="flex items-center gap-1 ml-1 shrink-0">
          <Filter class="w-3.5 h-3.5 text-slate-400 shrink-0" aria-hidden="true" />
          <select
            id="task-filter"
            v-model="selectedTaskFilter"
            aria-label="Lọc theo tác vụ"
            class="bg-slate-800 text-slate-200 border border-slate-700 rounded px-2 py-1 text-xs max-w-[180px] truncate focus:ring-1 focus:ring-primary focus:outline-none cursor-pointer"
          >
            <option value="all">Tất cả tác vụ</option>
            <option value="prep">Chuẩn bị</option>
            <option v-for="t in tasks" :key="t.position" :value="t.position">
              Tác vụ {{ t.position + 1 }}: {{ t.label }}
            </option>
          </select>
        </div>

        <!-- Status Notification for Screen Readers and UI -->
        <span
          v-if="statusMessage"
          role="status"
          class="px-2 py-0.5 rounded text-[11px] font-medium whitespace-nowrap shrink-0"
          :class="[
            status === 'success' ? 'bg-success/20 text-success border border-success/40' : '',
            status === 'failed' ? 'bg-danger/20 text-danger border border-danger/40' : '',
            status === 'running' ? 'bg-info/20 text-info border border-info/40' : 'text-slate-400'
          ]"
        >
          {{ statusMessage }}
        </span>
      </div>

      <div class="flex items-center gap-1.5 flex-nowrap shrink-0">
        <!-- Search in Log -->
        <div class="flex items-center gap-1 bg-slate-800/90 border border-slate-700 rounded-[6px] px-2 py-0.5">
          <Search class="w-3.5 h-3.5 text-slate-400 shrink-0" aria-hidden="true" />
          <input
            v-model="searchQuery"
            type="text"
            aria-label="Tìm trong nhật ký"
            placeholder="Tìm trong log..."
            class="w-32 bg-transparent text-slate-200 placeholder:text-slate-500 text-xs focus:outline-none"
            @keydown.enter.exact.prevent="goToNextMatch"
            @keydown.enter.shift.prevent="goToPrevMatch"
          />
          <button
            v-if="searchQuery"
            type="button"
            aria-label="Xoá tìm kiếm"
            class="p-0.5 text-slate-400 hover:text-slate-200 cursor-pointer"
            @click="clearSearch"
          >
            <X class="w-3 h-3" aria-hidden="true" />
          </button>
          <span
            v-if="normalizedQuery"
            data-testid="log-search-count"
            class="text-[11px] font-mono tabular-nums text-slate-400 whitespace-nowrap pl-1 border-l border-slate-700"
          >
            {{ matchedLineSeqs.length > 0 ? `${activeMatchIndex + 1}/${matchedLineSeqs.length}` : '0/0' }}
          </span>
          <button
            v-if="normalizedQuery"
            type="button"
            aria-label="Kết quả trước"
            title="Kết quả trước"
            :disabled="matchedLineSeqs.length === 0"
            class="p-0.5 rounded text-slate-300 hover:text-white hover:bg-slate-700 disabled:opacity-40 cursor-pointer disabled:cursor-default"
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
            class="p-0.5 rounded text-slate-300 hover:text-white hover:bg-slate-700 disabled:opacity-40 cursor-pointer disabled:cursor-default"
            @click="goToNextMatch"
          >
            <ChevronDown class="w-3.5 h-3.5" aria-hidden="true" />
          </button>
        </div>

        <!-- Follow / Auto-scroll Toggle Button -->
        <button
          type="button"
          :class="[
            'inline-flex items-center gap-1 px-2 py-1 rounded-[6px] text-xs font-medium transition-colors cursor-pointer whitespace-nowrap',
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
          class="inline-flex items-center gap-1 px-2 py-1 rounded-[6px] text-xs font-medium bg-slate-800 text-slate-300 hover:text-white hover:bg-slate-700 transition-colors cursor-pointer whitespace-nowrap"
          @click="copyLogs"
        >
          <Check v-if="copied" class="w-3.5 h-3.5 text-success" aria-hidden="true" />
          <Copy v-else class="w-3.5 h-3.5" aria-hidden="true" />
          <span>Sao chép</span>
        </button>

        <!-- Download Button -->
        <button
          type="button"
          aria-label="Tải xuống nhật ký"
          class="inline-flex items-center gap-1 px-2 py-1 rounded-[6px] text-xs font-medium bg-slate-800 text-slate-300 hover:text-white hover:bg-slate-700 transition-colors cursor-pointer whitespace-nowrap"
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
      class="flex-1 min-h-0 overflow-auto p-3 font-mono text-[13px] leading-relaxed select-text focus-visible:outline-2 focus-visible:outline-ring"
      @scroll="handleScroll"
    >
      <!-- Grouped View (For Job Runs) -->
      <template v-if="isGrouped">
        <!-- Group: Chuẩn bị (lines with taskPosition == null) -->
        <div
          v-show="selectedTaskFilter === 'all' || selectedTaskFilter === 'prep'"
          id="task-group-prep"
          class="mb-4 rounded-[6px] border border-slate-800 bg-slate-950/40 overflow-hidden"
        >
          <button
            type="button"
            class="w-full flex items-center justify-between px-3 py-2 bg-slate-900/80 hover:bg-slate-800/80 text-left transition-colors cursor-pointer focus-visible:outline-2 focus-visible:outline-ring"
            :aria-expanded="isGroupExpanded('prep')"
            aria-label="Thu gọn hoặc mở rộng nhóm Chuẩn bị"
            @click="toggleGroup('prep')"
          >
            <div class="flex items-center gap-2">
              <component
                :is="isGroupExpanded('prep') ? ChevronDown : ChevronRight"
                class="w-4 h-4 text-slate-400"
                aria-hidden="true"
              />
              <span class="font-sans font-semibold text-xs text-slate-200">Chuẩn bị</span>
              <span class="text-[11px] text-slate-400 font-mono">({{ prepLines.length }} dòng)</span>
            </div>
          </button>

          <div v-show="isGroupExpanded('prep')" class="p-2 space-y-0.5 overflow-x-auto">
            <div v-if="prepLines.length === 0" class="text-slate-500 italic py-2 px-3 text-xs">
              Chưa có dữ liệu nhật ký chuẩn bị...
            </div>
            <div
              v-for="line in prepLines"
              :key="line.seq"
              :data-log-seq="line.seq"
              :title="line.stream"
              :class="[
                'flex items-start hover:bg-slate-800/40 rounded px-1 -mx-1 min-w-max',
                activeMatchSeq === line.seq ? 'bg-primary/15 ring-1 ring-inset ring-primary/40' : matchedSeqSet.has(line.seq) ? 'bg-amber-400/5' : ''
              ]"
            >
              <span class="w-12 flex-shrink-0 text-slate-600 select-none text-right pr-4 tabular-nums text-xs">
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
                    :class="activeMatchSeq === line.seq ? 'bg-amber-400 text-slate-950 font-semibold rounded-[2px] px-0.5' : 'bg-amber-400/35 text-amber-100 rounded-[2px] px-0.5'"
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
          class="mb-4 rounded-[6px] border border-slate-800 bg-slate-950/40 overflow-hidden"
        >
          <button
            type="button"
            class="w-full flex items-center justify-between px-3 py-2 bg-slate-900/80 hover:bg-slate-800/80 text-left transition-colors cursor-pointer focus-visible:outline-2 focus-visible:outline-ring"
            :aria-expanded="isGroupExpanded(group.task.position)"
            :aria-label="`Thu gọn hoặc mở rộng Tác vụ ${group.task.position + 1} — ${group.task.label}`"
            @click="toggleGroup(group.task.position)"
          >
            <div class="flex items-center gap-2 min-w-0">
              <component
                :is="isGroupExpanded(group.task.position) ? ChevronDown : ChevronRight"
                class="w-4 h-4 text-slate-400 shrink-0"
                aria-hidden="true"
              />
              <span class="font-sans font-semibold text-xs text-slate-200 truncate">
                Tác vụ {{ group.task.position + 1 }} — {{ group.task.label }}
              </span>
              <StatusBadge
                :status="group.task.status"
                size="sm"
                :label="group.task.status === 'pending' ? 'Đang chờ' : (group.task.status === 'cancelled' ? 'Đã huỷ' : undefined)"
              />
            </div>
            <span class="text-[11px] text-slate-400 font-mono shrink-0 ml-2">
              ({{ group.lines.length }} dòng)
            </span>
          </button>

          <div v-show="isGroupExpanded(group.task.position)" class="p-2 space-y-0.5 overflow-x-auto">
            <div v-if="group.lines.length === 0" class="text-slate-500 italic py-2 px-3 text-xs">
              Chưa có dữ liệu nhật ký cho tác vụ này...
            </div>
            <div
              v-for="line in group.lines"
              :key="line.seq"
              :data-log-seq="line.seq"
              :title="line.stream"
              :class="[
                'flex items-start hover:bg-slate-800/40 rounded px-1 -mx-1 min-w-max',
                activeMatchSeq === line.seq ? 'bg-primary/15 ring-1 ring-inset ring-primary/40' : matchedSeqSet.has(line.seq) ? 'bg-amber-400/5' : ''
              ]"
            >
              <span class="w-12 flex-shrink-0 text-slate-600 select-none text-right pr-4 tabular-nums text-xs">
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
                    :class="activeMatchSeq === line.seq ? 'bg-amber-400 text-slate-950 font-semibold rounded-[2px] px-0.5' : 'bg-amber-400/35 text-amber-100 rounded-[2px] px-0.5'"
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
        <div v-if="lines.length === 0" class="text-slate-500 italic py-8 text-center">
          Chưa có dữ liệu nhật ký...
        </div>

        <div
          v-for="line in lines"
          :key="line.seq"
          :data-log-seq="line.seq"
          :title="line.stream"
          :class="[
            'flex items-start hover:bg-slate-800/40 rounded px-1 -mx-1 min-w-max',
            activeMatchSeq === line.seq ? 'bg-primary/15 ring-1 ring-inset ring-primary/40' : matchedSeqSet.has(line.seq) ? 'bg-amber-400/5' : ''
          ]"
        >
          <!-- Sequence number -->
          <span class="w-12 flex-shrink-0 text-slate-600 select-none text-right pr-4 tabular-nums text-xs">
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
                :class="activeMatchSeq === line.seq ? 'bg-amber-400 text-slate-950 font-semibold rounded-[2px] px-0.5' : 'bg-amber-400/35 text-amber-100 rounded-[2px] px-0.5'"
              >{{ part.text }}</mark>
              <template v-else>{{ part.text }}</template>
            </template>
          </span>
        </div>
      </template>
    </div>
  </div>
</template>
