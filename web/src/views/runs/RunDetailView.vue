<script setup lang="ts">
import { ref, computed, watch, onMounted, onBeforeUnmount } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  ArrowLeft,
  RotateCw,
  AlertCircle,
  Clock,
  GitCommit,
  Layers,
  Calendar,
  AlertTriangle,
  CircleStop,
  Copy,
  Check
} from 'lucide-vue-next'
import AppLayout from '../../layouts/AppLayout.vue'
import AppButton from '../../components/AppButton.vue'
import ConfirmDialog from '../../components/ConfirmDialog.vue'
import StatusBadge from '../../components/StatusBadge.vue'
import StepTimeline from '../../components/StepTimeline.vue'
import LogViewer from '../../components/LogViewer.vue'
import TaskTable from '../../components/run/TaskTable.vue'
import EmptyState from '../../components/EmptyState.vue'
import { useApi, ApiError } from '../../api'
import { useRunsStore } from '../../stores/runs'
import type { RunDetail, LogLine, Pipeline, RunStatus } from '../../api/types'
import { useToast } from '../../composables/useToast'
import { formatDateTime, formatDuration, formatFailureReason } from './formatters'

const route = useRoute()
const router = useRouter()
const api = useApi()
const runsStore = useRunsStore()
const toast = useToast()
const copiedField = ref<'tag' | 'image' | null>(null)

async function copyInfoValue(field: 'tag' | 'image', value?: string | null) {
  if (!value) return
  try {
    await navigator.clipboard.writeText(value)
    copiedField.value = field
    toast.success('Đã sao chép vào bộ nhớ tạm')
    setTimeout(() => {
      if (copiedField.value === field) copiedField.value = null
    }, 2000)
  } catch {
    toast.error('Không thể sao chép vào bộ nhớ tạm')
  }
}

const MAX_LOG_LINES = 5000

const logViewerRef = ref<any>(null)
const run = ref<RunDetail | null>(null)
const pipeline = ref<Pipeline | null>(null)
const lines = ref<LogLine[]>([])
const loading = ref(true)
const notFound = ref(false)
const errorMessage = ref<string | null>(null)
const isTriggering = ref(false)
const showCancelDialog = ref(false)
const isCancelling = ref(false)
const logTruncated = ref(false)
const currentTime = ref(Date.now())
const selectedTaskPosition = ref<number | null>(null)
const leftWidth = ref(390)
let isResizing = false

const seenSeqs = new Set<number>()

let cleanupStream: (() => void) | null = null
let pollInterval: ReturnType<typeof setInterval> | null = null
let secondInterval: ReturnType<typeof setInterval> | null = null

const isJobRun = computed(() => {
  return run.value?.kind === 'job' || (run.value?.jobId != null) || (run.value?.steps.some((s) => s.name === 'runjob') ?? false)
})

const displayName = computed(() => {
  if (isJobRun.value) {
    return runsStore.getJobName(run.value?.jobId)
  }
  if (pipeline.value) return pipeline.value.name
  if (run.value?.pipelineId) return `Pipeline #${run.value.pipelineId}`
  return 'Lượt chạy'
})

const failedTask = computed(() => {
  if (!isJobRun.value || !run.value?.tasks) return null
  return run.value.tasks.find((t) => t.status === 'failed') || null
})

function scrollToTaskLog(position: number) {
  selectedTaskPosition.value = position
  if (logViewerRef.value?.focusTask) {
    logViewerRef.value.focusTask(position)
  } else {
    const el = document.getElementById(`task-group-${position}`)
    if (el) {
      const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches
      el.scrollIntoView({ behavior: prefersReducedMotion ? 'auto' : 'smooth', block: 'start' })
    }
  }
}

function handleSelectedTaskChange(value: string | number) {
  if (typeof value === 'number') {
    selectedTaskPosition.value = value
  } else if (typeof value === 'string' && value !== 'all' && value !== 'prep' && !isNaN(Number(value))) {
    selectedTaskPosition.value = Number(value)
  } else {
    selectedTaskPosition.value = null
  }
}

function startResize(e: MouseEvent) {
  e.preventDefault()
  isResizing = true
  const startX = e.clientX
  const startWidth = leftWidth.value

  function onMouseMove(moveEvent: MouseEvent) {
    if (!isResizing) return
    const nextWidth = Math.min(520, Math.max(320, startWidth + (moveEvent.clientX - startX)))
    leftWidth.value = nextWidth
  }

  function onMouseUp() {
    isResizing = false
    window.removeEventListener('mousemove', onMouseMove)
    window.removeEventListener('mouseup', onMouseUp)
  }

  window.addEventListener('mousemove', onMouseMove)
  window.addEventListener('mouseup', onMouseUp)
}

const isRunActive = computed(() => {
  return run.value?.status === 'queued' || run.value?.status === 'running'
})

const statusAnnouncement = computed(() => {
  if (run.value?.status === 'success') return 'Run đã kết thúc: thành công'
  if (run.value?.status === 'failed') return 'Run đã kết thúc: thất bại'
  if (run.value?.status === 'cancelled') return 'Run đã kết thúc: đã dừng'
  if (run.value?.status === 'running') return 'Lượt chạy đang chạy'
  if (run.value?.status === 'queued') return 'Lượt chạy đang chờ thực thi'
  return ''
})

function mergeLines(newLines: LogLine[]) {
  let added = false
  for (const line of newLines) {
    if (!seenSeqs.has(line.seq)) {
      seenSeqs.add(line.seq)
      lines.value.push(line)
      added = true
    }
  }

  if (added) {
    lines.value.sort((a, b) => a.seq - b.seq)
    if (lines.value.length > MAX_LOG_LINES) {
      lines.value = lines.value.slice(-MAX_LOG_LINES)
      logTruncated.value = true
    }
  }
}

function cleanupResources() {
  if (cleanupStream) {
    cleanupStream()
    cleanupStream = null
  }
  if (pollInterval) {
    clearInterval(pollInterval)
    pollInterval = null
  }
  if (secondInterval) {
    clearInterval(secondInterval)
    secondInterval = null
  }
}

function startPolling(runId: number) {
  if (pollInterval) return
  pollInterval = setInterval(async () => {
    try {
      const updated = await api.getRun(runId)
      run.value = updated
      if (updated.status !== 'queued' && updated.status !== 'running') {
        if (pollInterval) {
          clearInterval(pollInterval)
          pollInterval = null
        }
      }
    } catch {
      // Ignore polling errors
    }
  }, 3000)
}

async function loadRunData(id: number) {
  cleanupResources()
  loading.value = true
  notFound.value = false
  errorMessage.value = null
  run.value = null
  pipeline.value = null
  lines.value = []
  seenSeqs.clear()
  logTruncated.value = false
  selectedTaskPosition.value = null

  try {
    const runData = await api.getRun(id)
    runData.tasks = runData.tasks || []
    run.value = runData

    // Fetch pipeline info, job list (if needed), and initial logs in parallel
    const [logsRes, pipelineRes] = await Promise.allSettled([
      api.getRunLogs(id, 0),
      runData.pipelineId ? api.getPipeline(runData.pipelineId) : Promise.resolve(null),
      runData.jobId && !runsStore.jobs.some((j) => j.id === runData.jobId)
        ? runsStore.fetchJobs()
        : Promise.resolve(null)
    ])

    if (pipelineRes.status === 'fulfilled' && pipelineRes.value) {
      pipeline.value = pipelineRes.value
    }
    if (logsRes.status === 'fulfilled') {
      mergeLines(logsRes.value)
    }

    // Setup timer for duration tick
    secondInterval = setInterval(() => {
      currentTime.value = Date.now()
    }, 1000)

    // Stream logs and poll if run is active
    if (runData.status === 'queued' || runData.status === 'running') {
      startPolling(id)
      startStream(id)
    }
  } catch (err: any) {
    if (err instanceof ApiError && err.status === 404) {
      notFound.value = true
    } else {
      errorMessage.value = err.message || 'Không thể tải thông tin lượt chạy'
    }
  } finally {
    loading.value = false
  }
}

function startStream(runId: number) {
  if (cleanupStream) {
    cleanupStream()
    cleanupStream = null
  }

  const maxSeq = lines.value.length > 0 ? Math.max(...lines.value.map((l) => l.seq)) : 0

  cleanupStream = api.streamRunLogs(
    runId,
    maxSeq,
    (line: LogLine) => {
      mergeLines([line])
    },
    async (finalStatus: RunStatus) => {
      if (cleanupStream) {
        cleanupStream()
        cleanupStream = null
      }
      if (pollInterval) {
        clearInterval(pollInterval)
        pollInterval = null
      }
      // Re-fetch detail to get final step states and timestamps
      try {
        const finalDetail = await api.getRun(runId)
        finalDetail.tasks = finalDetail.tasks || []
        run.value = finalDetail
      } catch {
        if (run.value) {
          run.value.status = finalStatus
        }
      }
    },
    () => {
      // On stream error
    }
  )
}

async function handleRerun() {
  if (!run.value) return
  isTriggering.value = true

  try {
    let newRun: any
    if (isJobRun.value && run.value.jobId) {
      newRun = await runsStore.runJob(run.value.jobId)
    } else if (run.value.pipelineId) {
      newRun = await api.triggerRun(run.value.pipelineId)
    }
    if (newRun) {
      toast.success(`Đã kích hoạt lượt chạy mới #${newRun.id}`)
      router.push(`/runs/${newRun.id}`)
    }
  } catch (err: any) {
    if (err instanceof ApiError && err.status === 409) {
      toast.error(isJobRun.value ? 'Job đang có lượt chạy' : 'Pipeline đang có lượt chạy')
    } else {
      toast.error(err.message || 'Không thể kích hoạt lượt chạy')
    }
  } finally {
    isTriggering.value = false
  }
}

async function handleConfirmCancel() {
  if (!run.value) return
  showCancelDialog.value = false
  isCancelling.value = true

  try {
    const updated = await api.cancelRun(run.value.id)
    if (updated.status === 'cancelled') {
      run.value.status = 'cancelled'
      if (updated.failureReason) run.value.failureReason = updated.failureReason
      if (updated.finishedAt) run.value.finishedAt = updated.finishedAt
      isCancelling.value = false

      try {
        const fresh = await api.getRun(run.value.id)
        run.value = fresh
      } catch {
        // ignore
      }
      toast.success('Đã dừng lượt chạy thành công')
    } else {
      // Run was running; poll frequently until cancelled or finished
      const cancelPoll = setInterval(async () => {
        if (!isCancelling.value || !run.value) {
          clearInterval(cancelPoll)
          return
        }
        try {
          const fresh = await api.getRun(run.value.id)
          run.value = fresh
          if (fresh.status !== 'queued' && fresh.status !== 'running') {
            isCancelling.value = false
            clearInterval(cancelPoll)
          }
        } catch {
          // ignore
        }
      }, 500)
    }
  } catch (err: any) {
    isCancelling.value = false
    if (err instanceof ApiError && err.status === 409) {
      toast.error('Run đã kết thúc')
      try {
        const fresh = await api.getRun(run.value.id)
        run.value = fresh
      } catch {
        // ignore
      }
    } else {
      toast.error(err.message || 'Không thể dừng lượt chạy')
    }
  }
}

watch(
  () => run.value?.status,
  (newStatus) => {
    if (newStatus && newStatus !== 'queued' && newStatus !== 'running') {
      isCancelling.value = false
    }
  }
)

watch(
  () => route.params.id,
  (newId) => {
    if (newId) {
      const idNum = Number(newId)
      if (isNaN(idNum)) {
        notFound.value = true
        loading.value = false
      } else {
        loadRunData(idNum)
      }
    }
  }
)

onMounted(() => {
  const idNum = Number(route.params.id)
  if (isNaN(idNum)) {
    notFound.value = true
    loading.value = false
  } else {
    loadRunData(idNum)
  }
})

onBeforeUnmount(() => {
  cleanupResources()
})
</script>

<template>
  <AppLayout>
    <!-- Screen reader status announcement -->
    <div role="status" class="sr-only" aria-live="polite">
      {{ statusAnnouncement }}
    </div>

    <div class="flex-1 min-h-0 flex flex-col gap-2.5 w-full">
      <!-- Loading State -->
      <div v-if="loading" class="flex-1 flex flex-col items-center justify-center text-muted-fg gap-3">
        <RotateCw class="w-8 h-8 animate-spin" aria-hidden="true" />
        <span class="text-sm">Đang tải thông tin lượt chạy...</span>
      </div>

      <!-- 404 Not Found State -->
      <div v-else-if="notFound" class="flex-1 flex flex-col gap-3">
        <div class="shrink-0">
          <button
            type="button"
            class="inline-flex items-center gap-1.5 text-sm font-medium text-muted-fg hover:text-fg transition-colors cursor-pointer focus-visible:outline-2 focus-visible:outline-ring rounded-[4px] px-1 -mx-1"
            @click="router.push('/')"
          >
            <ArrowLeft class="w-4 h-4" aria-hidden="true" />
            <span>Quay lại danh sách</span>
          </button>
        </div>
        <EmptyState
          title="Không tìm thấy lượt chạy"
          :description="`Lượt chạy #${route.params.id} không tồn tại hoặc đã bị xoá.`"
          :icon="AlertCircle"
        >
          <template #action>
            <AppButton variant="primary" @click="router.push('/')">
              Quay lại danh sách
            </AppButton>
          </template>
        </EmptyState>
      </div>

      <!-- Error State -->
      <div v-else-if="errorMessage" class="flex-1 flex flex-col gap-3">
        <div class="shrink-0">
          <button
            type="button"
            class="inline-flex items-center gap-1.5 text-sm font-medium text-muted-fg hover:text-fg transition-colors cursor-pointer focus-visible:outline-2 focus-visible:outline-ring rounded-[4px] px-1 -mx-1"
            @click="router.push('/')"
          >
            <ArrowLeft class="w-4 h-4" aria-hidden="true" />
            <span>Quay lại danh sách</span>
          </button>
        </div>
        <div
          role="alert"
          class="p-4 rounded-[8px] border border-danger/40 bg-danger/10 text-danger text-sm flex items-center justify-between"
        >
          <span>{{ errorMessage }}</span>
          <AppButton variant="secondary" size="sm" @click="loadRunData(Number(route.params.id))">
            Thử lại
          </AppButton>
        </div>
      </div>

      <!-- Main Run Detail Content -->
      <div v-else-if="run" class="flex-1 min-h-0 flex flex-col gap-2">
        <!-- Compact Top Header Row (<= 56px) -->
        <div class="shrink-0 flex flex-row items-center justify-between gap-3 flex-nowrap pb-2 border-b border-border min-h-[44px]">
          <div class="flex items-center gap-2.5 min-w-0 flex-nowrap">
            <button
              type="button"
              class="inline-flex items-center gap-1 px-2 py-1 rounded-[6px] border border-border bg-card text-xs font-medium text-muted-fg hover:text-fg hover:bg-muted transition-colors cursor-pointer shrink-0 whitespace-nowrap focus-visible:outline-2 focus-visible:outline-ring"
              aria-label="Quay lại danh sách"
              title="Quay lại danh sách"
              @click="router.push('/')"
            >
              <ArrowLeft class="w-3.5 h-3.5" aria-hidden="true" />
              <span>Quay lại danh sách</span>
            </button>

            <h1 class="text-lg font-semibold text-fg tracking-tight truncate">
              {{ displayName }}
            </h1>
            <span class="font-mono text-base text-muted-fg font-medium whitespace-nowrap shrink-0">
              #{{ run.id }}
            </span>
            <StatusBadge :status="run.status" size="sm" />

            <span class="text-xs text-muted-fg flex items-center gap-1 whitespace-nowrap shrink-0 pl-2 border-l border-border">
              <Calendar class="w-3.5 h-3.5 shrink-0" aria-hidden="true" />
              <span>Tạo lúc: {{ formatDateTime(run.createdAt) }}</span>
            </span>
          </div>

          <div class="flex items-center gap-2 shrink-0 whitespace-nowrap">
            <AppButton
              v-if="isRunActive || isCancelling"
              variant="secondary"
              size="sm"
              :loading="isCancelling"
              :disabled="isCancelling"
              :aria-label="isCancelling ? 'Đang dừng lượt chạy' : 'Dừng lượt chạy'"
              @click="showCancelDialog = true"
            >
              <CircleStop class="w-3.5 h-3.5 text-warn" aria-hidden="true" />
              <span>{{ isCancelling ? 'Đang dừng…' : 'Dừng' }}</span>
            </AppButton>

            <AppButton
              variant="secondary"
              size="sm"
              :loading="isTriggering"
              :aria-label="isJobRun ? 'Chạy lại job này' : 'Chạy lại pipeline này'"
              @click="handleRerun"
            >
              <RotateCw class="w-3.5 h-3.5" aria-hidden="true" />
              <span>Chạy lại</span>
            </AppButton>
          </div>
        </div>

        <!-- Single-row Info Chip Strip -->
        <div class="shrink-0 flex items-center gap-2 flex-nowrap overflow-x-auto">
          <!-- Tag / SHA -->
          <div class="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-[6px] bg-card border border-border text-xs whitespace-nowrap shrink-0">
            <GitCommit class="w-3.5 h-3.5 text-muted-fg shrink-0" aria-hidden="true" />
            <span class="text-muted-fg">Tag SHA:</span>
            <span
              v-if="run.tag"
              class="font-mono text-fg truncate max-w-[120px]"
              :title="run.tag"
            >
              {{ run.tag }}
            </span>
            <span v-else class="text-muted-fg">Chưa có</span>
            <button
              v-if="run.tag"
              type="button"
              aria-label="Sao chép Tag (Git SHA)"
              title="Sao chép"
              class="p-0.5 rounded text-muted-fg hover:text-fg hover:bg-muted transition-colors cursor-pointer shrink-0 focus-visible:outline-2 focus-visible:outline-ring"
              @click="copyInfoValue('tag', run.tag)"
            >
              <Check v-if="copiedField === 'tag'" class="w-3 h-3 text-success" aria-hidden="true" />
              <Copy v-else class="w-3 h-3" aria-hidden="true" />
            </button>
          </div>

          <!-- Container Image -->
          <div class="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-[6px] bg-card border border-border text-xs whitespace-nowrap min-w-0">
            <Layers class="w-3.5 h-3.5 text-muted-fg shrink-0" aria-hidden="true" />
            <span class="text-muted-fg shrink-0">Image:</span>
            <span
              class="font-mono text-fg truncate max-w-[280px]"
              :title="run.image || 'Chưa tạo'"
            >
              {{ run.image || 'Chưa tạo' }}
            </span>
            <button
              v-if="run.image"
              type="button"
              aria-label="Sao chép Container Image"
              title="Sao chép"
              class="p-0.5 rounded text-muted-fg hover:text-fg hover:bg-muted transition-colors cursor-pointer shrink-0 focus-visible:outline-2 focus-visible:outline-ring"
              @click="copyInfoValue('image', run.image)"
            >
              <Check v-if="copiedField === 'image'" class="w-3 h-3 text-success" aria-hidden="true" />
              <Copy v-else class="w-3 h-3" aria-hidden="true" />
            </button>
          </div>

          <!-- Started At -->
          <div class="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-[6px] bg-card border border-border text-xs whitespace-nowrap shrink-0">
            <Calendar class="w-3.5 h-3.5 text-muted-fg shrink-0" aria-hidden="true" />
            <span class="text-muted-fg">Bắt đầu:</span>
            <span class="text-fg font-medium tabular-nums" :title="formatDateTime(run.startedAt)">
              {{ formatDateTime(run.startedAt) }}
            </span>
          </div>

          <!-- Duration -->
          <div class="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-[6px] bg-card border border-border text-xs whitespace-nowrap shrink-0">
            <Clock class="w-3.5 h-3.5 text-muted-fg shrink-0" aria-hidden="true" />
            <span class="text-muted-fg">Thời lượng:</span>
            <span class="font-mono font-semibold text-fg tabular-nums">
              {{ formatDuration(run.startedAt, run.finishedAt, currentTime) }}
            </span>
          </div>
        </div>

        <!-- Compact Failure Alert (Failed Task + Failure Reason) -->
        <div
          v-if="failedTask || (run.status === 'failed' && run.failureReason)"
          role="alert"
          class="shrink-0 px-3 py-1.5 rounded-[6px] border border-danger/40 border-l-4 border-l-danger bg-danger/10 text-danger text-xs flex items-center justify-between gap-3 flex-nowrap"
        >
          <div class="flex items-center gap-2 min-w-0 flex-nowrap">
            <AlertTriangle class="w-4 h-4 shrink-0" aria-hidden="true" />
            <div v-if="failedTask" class="font-semibold whitespace-nowrap shrink-0">
              <span>Tác vụ {{ failedTask.position + 1 }} thất bại (exit code {{ failedTask.exitCode ?? 1 }})</span>
              <span v-if="failedTask.label" class="ml-1.5 font-mono text-[11px] text-fg/80 font-normal">
                ({{ failedTask.label }})
              </span>
            </div>
            <span v-else class="font-semibold whitespace-nowrap shrink-0">Lý do thất bại</span>

            <span
              v-if="
                run.status === 'failed' &&
                run.failureReason &&
                (!failedTask || !formatFailureReason(run.failureReason).toLowerCase().includes(`tác vụ ${failedTask.position + 1}`))
              "
              class="text-fg/90 truncate"
              :title="formatFailureReason(run.failureReason)"
            >
              {{ formatFailureReason(run.failureReason) }}
            </span>
          </div>

          <button
            v-if="failedTask"
            type="button"
            class="text-xs font-semibold underline hover:text-danger/80 cursor-pointer focus-visible:outline-2 focus-visible:outline-ring rounded px-1.5 py-0.5 shrink-0 whitespace-nowrap"
            @click="scrollToTaskLog(failedTask.position)"
          >
            Xem log tác vụ
          </button>
        </div>

        <!-- Compact Cancelled Reason Alert -->
        <div
          v-if="run.status === 'cancelled' && run.failureReason"
          role="note"
          class="shrink-0 flex items-center gap-2 px-3 py-1.5 rounded-[6px] border border-warn/30 bg-warn/10 text-fg text-xs flex-nowrap"
        >
          <CircleStop class="w-4 h-4 shrink-0 text-warn" aria-hidden="true" />
          <span class="font-semibold text-warn whitespace-nowrap shrink-0">Lý do dừng:</span>
          <span class="text-fg/90 truncate" :title="formatFailureReason(run.failureReason)">
            {{ formatFailureReason(run.failureReason) }}
          </span>
        </div>

        <!-- Two-Column Workspace Body -->
        <div class="flex-1 min-h-0 flex flex-row gap-1.5">
          <!-- Left Column: Vertical StepTimeline + Task List -->
          <div
            data-testid="run-left-column"
            class="shrink-0 min-h-0 flex flex-col gap-2"
            :style="{ width: `${leftWidth}px` }"
          >
            <!-- Step Timeline Card -->
            <div class="shrink-0 p-2.5 rounded-[8px] bg-card border border-border">
              <h2 class="text-xs font-semibold text-muted-fg uppercase tracking-wide mb-1 whitespace-nowrap">
                Tiến trình
              </h2>
              <StepTimeline :steps="run.steps" :kind="isJobRun ? 'job' : 'pipeline'" orientation="vertical" />
            </div>

            <!-- Task Table Card (Job runs only) -->
            <div
              v-if="isJobRun"
              data-testid="run-task-panel"
              class="flex-1 min-h-0 p-2.5 rounded-[8px] bg-card border border-border flex flex-col gap-1.5"
            >
              <div class="shrink-0 flex items-center justify-between">
                <h2 class="text-xs font-semibold text-muted-fg uppercase tracking-wide whitespace-nowrap">
                  Danh sách tác vụ
                </h2>
                <span class="text-[11px] font-mono text-muted-fg tabular-nums">
                  {{ (run.tasks || []).length }} tác vụ
                </span>
              </div>
              <TaskTable
                :tasks="run.tasks || []"
                :selected-position="selectedTaskPosition"
                @view-log="scrollToTaskLog"
              />
            </div>
          </div>

          <!-- Resize Handle -->
          <div
            role="separator"
            aria-orientation="vertical"
            aria-label="Điều chỉnh độ rộng cột trái"
            class="w-1.5 shrink-0 cursor-col-resize rounded hover:bg-primary/30 active:bg-primary/50 transition-colors self-stretch"
            @mousedown="startResize"
          />

          <!-- Right Column: Full-height Log Viewer -->
          <div
            data-testid="run-right-column"
            class="flex-1 min-w-0 min-h-0 flex flex-col gap-2"
          >
            <div
              v-if="logTruncated"
              role="note"
              class="shrink-0 text-xs px-3 py-1 rounded-[6px] bg-warn/10 text-warn border border-warn/30"
            >
              Nhật ký vượt quá giới hạn, chỉ hiển thị {{ MAX_LOG_LINES.toLocaleString() }} dòng mới nhất.
            </div>

            <LogViewer
              ref="logViewerRef"
              :lines="lines"
              :status="run.status"
              :run-id="run.id"
              :tasks="run.tasks"
              :is-job="isJobRun"
              @update:selected-task="handleSelectedTaskChange"
            />
          </div>
        </div>
      </div>
    </div>

    <!-- Confirm Cancel Dialog -->
    <ConfirmDialog
      v-model="showCancelDialog"
      :title="`Dừng lượt chạy #${run?.id}?`"
      message="Các bước đang chạy sẽ bị ngắt và Shipit sẽ dọn dẹp máy build. Deployment đã được cập nhật trước đó sẽ không bị hoàn tác."
      confirm-text="Dừng"
      cancel-text="Huỷ"
      :loading="isCancelling"
      :danger="true"
      @confirm="handleConfirmCancel"
      @cancel="showCancelDialog = false"
    />
  </AppLayout>
</template>
