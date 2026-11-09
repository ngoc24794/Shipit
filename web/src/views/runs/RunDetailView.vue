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
  CircleStop
} from 'lucide-vue-next'
import AppLayout from '../../layouts/AppLayout.vue'
import AppButton from '../../components/AppButton.vue'
import ConfirmDialog from '../../components/ConfirmDialog.vue'
import StatusBadge from '../../components/StatusBadge.vue'
import StepTimeline from '../../components/StepTimeline.vue'
import LogViewer from '../../components/LogViewer.vue'
import EmptyState from '../../components/EmptyState.vue'
import { useApi, ApiError } from '../../api'
import type { RunDetail, LogLine, Pipeline, RunStatus } from '../../api/types'
import { useToast } from '../../composables/useToast'
import { formatDateTime, formatDuration, formatFailureReason } from './formatters'

const route = useRoute()
const router = useRouter()
const api = useApi()
const toast = useToast()

const MAX_LOG_LINES = 5000

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

const seenSeqs = new Set<number>()

let cleanupStream: (() => void) | null = null
let pollInterval: ReturnType<typeof setInterval> | null = null
let secondInterval: ReturnType<typeof setInterval> | null = null

const pipelineName = computed(() => {
  if (pipeline.value) return pipeline.value.name
  if (run.value) return `Pipeline #${run.value.pipelineId}`
  return 'Lượt chạy'
})

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

  try {
    const runData = await api.getRun(id)
    run.value = runData

    // Fetch pipeline info and initial logs in parallel
    const [pipelineRes, logsRes] = await Promise.allSettled([
      api.getPipeline(runData.pipelineId),
      api.getRunLogs(id, 0)
    ])

    if (pipelineRes.status === 'fulfilled') {
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
    const newRun = await api.triggerRun(run.value.pipelineId)
    toast.success(`Đã kích hoạt lượt chạy mới #${newRun.id}`)
    router.push(`/runs/${newRun.id}`)
  } catch (err: any) {
    if (err instanceof ApiError && err.status === 409) {
      toast.error('Pipeline đang có lượt chạy')
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

    <div class="space-y-6 max-w-[1280px] mx-auto w-full">
      <!-- Back Button -->
      <div>
        <button
          type="button"
          class="inline-flex items-center gap-1.5 text-sm font-medium text-muted-fg hover:text-fg transition-colors cursor-pointer focus-visible:outline-2 focus-visible:outline-ring rounded-[4px] px-1 -mx-1"
          @click="router.push('/')"
        >
          <ArrowLeft class="w-4 h-4" aria-hidden="true" />
          <span>Quay lại danh sách</span>
        </button>
      </div>

      <!-- Loading State -->
      <div v-if="loading" class="py-16 flex flex-col items-center justify-center text-muted-fg gap-3">
        <RotateCw class="w-8 h-8 animate-spin" aria-hidden="true" />
        <span class="text-sm">Đang tải thông tin lượt chạy...</span>
      </div>

      <!-- 404 Not Found State -->
      <EmptyState
        v-else-if="notFound"
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

      <!-- Error State -->
      <div
        v-else-if="errorMessage"
        role="alert"
        class="p-4 rounded-[8px] border border-danger/40 bg-danger/10 text-danger text-sm flex items-center justify-between"
      >
        <span>{{ errorMessage }}</span>
        <AppButton variant="secondary" size="sm" @click="loadRunData(Number(route.params.id))">
          Thử lại
        </AppButton>
      </div>

      <!-- Main Run Detail Content -->
      <div v-else-if="run" class="space-y-6">
        <!-- Header Banner -->
        <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 pb-4 border-b border-border">
          <div class="space-y-1">
            <div class="flex items-center gap-3 flex-wrap">
              <h1 class="text-2xl font-semibold text-fg tracking-tight">
                {{ pipelineName }}
              </h1>
              <span class="font-mono text-xl text-muted-fg font-medium">
                #{{ run.id }}
              </span>
              <StatusBadge :status="run.status" size="md" />
            </div>
            <p class="text-xs text-muted-fg flex items-center gap-1.5">
              <Calendar class="w-3.5 h-3.5" aria-hidden="true" />
              <span>Tạo lúc: {{ formatDateTime(run.createdAt) }}</span>
            </p>
          </div>

          <div class="flex items-center gap-2">
            <AppButton
              v-if="isRunActive || isCancelling"
              variant="secondary"
              :loading="isCancelling"
              :disabled="isCancelling"
              :aria-label="isCancelling ? 'Đang dừng lượt chạy' : 'Dừng lượt chạy'"
              @click="showCancelDialog = true"
            >
              <CircleStop class="w-4 h-4 text-warn" aria-hidden="true" />
              <span>{{ isCancelling ? 'Đang dừng…' : 'Dừng' }}</span>
            </AppButton>

            <AppButton
              variant="secondary"
              :loading="isTriggering"
              aria-label="Chạy lại pipeline này"
              @click="handleRerun"
            >
              <RotateCw class="w-4 h-4" aria-hidden="true" />
              <span>Chạy lại</span>
            </AppButton>
          </div>
        </div>

        <!-- Failure Reason Alert -->
        <div
          v-if="run.status === 'failed' && run.failureReason"
          role="alert"
          class="flex items-start gap-3 p-4 rounded-[8px] border border-danger/30 bg-danger/10 text-danger text-sm"
        >
          <AlertTriangle class="w-5 h-5 shrink-0 mt-0.5" aria-hidden="true" />
          <div class="space-y-1">
            <h4 class="font-semibold">Lý do thất bại</h4>
            <p class="text-fg/90 leading-relaxed">
              {{ formatFailureReason(run.failureReason) }}
            </p>
          </div>
        </div>

        <!-- Cancelled Reason Alert -->
        <div
          v-if="run.status === 'cancelled' && run.failureReason"
          role="note"
          class="flex items-start gap-3 p-4 rounded-[8px] border border-warn/30 bg-warn/10 text-fg text-sm"
        >
          <CircleStop class="w-5 h-5 shrink-0 mt-0.5 text-warn" aria-hidden="true" />
          <div class="space-y-1">
            <h4 class="font-semibold text-warn">Lý do dừng</h4>
            <p class="text-fg/90 leading-relaxed">
              {{ formatFailureReason(run.failureReason) }}
            </p>
          </div>
        </div>

        <!-- Info Grid -->
        <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
          <!-- Tag / SHA -->
          <div class="p-3.5 rounded-[8px] bg-card border border-border flex items-start gap-3">
            <div class="p-2 rounded bg-muted text-muted-fg shrink-0">
              <GitCommit class="w-4 h-4" aria-hidden="true" />
            </div>
            <div class="min-w-0 flex-1">
              <div class="text-xs text-muted-fg font-medium">Tag (Git SHA)</div>
              <div class="mt-1 font-mono text-sm text-fg truncate">
                <span
                  v-if="run.tag"
                  class="px-1.5 py-0.5 rounded bg-muted border border-border/60 text-xs"
                >
                  {{ run.tag }}
                </span>
                <span v-else class="text-muted-fg font-sans text-xs">Chưa có</span>
              </div>
            </div>
          </div>

          <!-- Image -->
          <div class="p-3.5 rounded-[8px] bg-card border border-border flex items-start gap-3">
            <div class="p-2 rounded bg-muted text-muted-fg shrink-0">
              <Layers class="w-4 h-4" aria-hidden="true" />
            </div>
            <div class="min-w-0 flex-1">
              <div class="text-xs text-muted-fg font-medium">Container Image</div>
              <div
                class="mt-1 font-mono text-xs text-fg break-all line-clamp-2"
                :title="run.image || ''"
              >
                {{ run.image || 'Chưa tạo' }}
              </div>
            </div>
          </div>

          <!-- Started At -->
          <div class="p-3.5 rounded-[8px] bg-card border border-border flex items-start gap-3">
            <div class="p-2 rounded bg-muted text-muted-fg shrink-0">
              <Calendar class="w-4 h-4" aria-hidden="true" />
            </div>
            <div class="min-w-0 flex-1">
              <div class="text-xs text-muted-fg font-medium">Bắt đầu lúc</div>
              <div class="mt-1 text-xs text-fg font-medium tabular-nums">
                {{ formatDateTime(run.startedAt) }}
              </div>
            </div>
          </div>

          <!-- Duration -->
          <div class="p-3.5 rounded-[8px] bg-card border border-border flex items-start gap-3">
            <div class="p-2 rounded bg-muted text-muted-fg shrink-0">
              <Clock class="w-4 h-4" aria-hidden="true" />
            </div>
            <div class="min-w-0 flex-1">
              <div class="text-xs text-muted-fg font-medium">Thời lượng</div>
              <div class="mt-1 font-mono text-sm font-semibold text-fg tabular-nums">
                {{ formatDuration(run.startedAt, run.finishedAt, currentTime) }}
              </div>
            </div>
          </div>
        </div>

        <!-- Step Timeline Card -->
        <div class="p-4 sm:p-6 rounded-[8px] bg-card border border-border">
          <h2 class="text-sm font-semibold text-fg mb-2">Tiến trình các bước</h2>
          <StepTimeline :steps="run.steps" />
        </div>

        <!-- Log Viewer Section -->
        <div class="space-y-2">
          <div
            v-if="logTruncated"
            role="note"
            class="text-xs px-3 py-1.5 rounded-[6px] bg-warn/10 text-warn border border-warn/30"
          >
            Nhật ký vượt quá giới hạn, chỉ hiển thị {{ MAX_LOG_LINES.toLocaleString() }} dòng mới nhất.
          </div>

          <LogViewer
            :lines="lines"
            :status="run.status"
            :run-id="run.id"
          />
        </div>
      </div>
    </div>

    <!-- Confirm Cancel Dialog -->
    <ConfirmDialog
      v-model="showCancelDialog"
      :title="`Dừng lần chạy #${run?.id}?`"
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
