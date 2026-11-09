<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount, watch } from 'vue'
import { useRouter } from 'vue-router'
import { Play, RotateCw, PlayCircle, Filter } from 'lucide-vue-next'
import AppLayout from '../../layouts/AppLayout.vue'
import AppTable from '../../components/AppTable.vue'
import AppButton from '../../components/AppButton.vue'
import AppSelect from '../../components/AppSelect.vue'
import AppDialog from '../../components/AppDialog.vue'
import AppField from '../../components/AppField.vue'
import StatusBadge from '../../components/StatusBadge.vue'
import EmptyState from '../../components/EmptyState.vue'
import { useRunsStore } from '../../stores/runs'
import { useToast } from '../../composables/useToast'
import { formatDateTime, formatDuration } from './formatters'
import { ApiError } from '../../api/client'

const router = useRouter()
const runsStore = useRunsStore()
const toast = useToast()

const selectedPipelineFilter = ref<string | number>('')
const showTriggerDialog = ref(false)
const triggerPipelineId = ref<string | number>('')
const isTriggering = ref(false)
const triggerError = ref<string | null>(null)
const currentTime = ref(Date.now())

let pollInterval: ReturnType<typeof setInterval> | null = null
let secondInterval: ReturnType<typeof setInterval> | null = null

const pipelineFilterOptions = computed(() => [
  { label: 'Tất cả pipeline', value: '' },
  ...runsStore.pipelines.map((p) => ({
    label: p.name,
    value: p.id
  }))
])

const triggerPipelineOptions = computed(() =>
  runsStore.pipelines.map((p) => ({
    label: `${p.name} (#${p.id})`,
    value: p.id
  }))
)

const hasActiveRuns = computed(() =>
  runsStore.runs.some((r) => r.status === 'queued' || r.status === 'running')
)

function startPolling() {
  if (pollInterval) return
  pollInterval = setInterval(async () => {
    const pipelineId = selectedPipelineFilter.value ? Number(selectedPipelineFilter.value) : undefined
    await runsStore.refreshRuns({ pipelineId })
  }, 3000)
}

function stopPolling() {
  if (pollInterval) {
    clearInterval(pollInterval)
    pollInterval = null
  }
}

watch(hasActiveRuns, (active) => {
  if (active) {
    startPolling()
  } else {
    stopPolling()
  }
})

watch(selectedPipelineFilter, async (newVal) => {
  const pipelineId = newVal ? Number(newVal) : undefined
  await runsStore.fetchRuns({ pipelineId })
  if (hasActiveRuns.value) {
    startPolling()
  } else {
    stopPolling()
  }
})

onMounted(async () => {
  secondInterval = setInterval(() => {
    currentTime.value = Date.now()
  }, 1000)

  await Promise.all([
    runsStore.fetchPipelines(),
    runsStore.fetchRuns()
  ])

  if (hasActiveRuns.value) {
    startPolling()
  }
})

onBeforeUnmount(() => {
  stopPolling()
  if (secondInterval) {
    clearInterval(secondInterval)
    secondInterval = null
  }
})

function openTriggerDialog() {
  triggerError.value = null
  if (selectedPipelineFilter.value) {
    triggerPipelineId.value = selectedPipelineFilter.value
  } else if (runsStore.pipelines.length > 0) {
    triggerPipelineId.value = runsStore.pipelines[0].id
  } else {
    triggerPipelineId.value = ''
  }
  showTriggerDialog.value = true
}

async function handleTriggerRun() {
  if (!triggerPipelineId.value) return
  const pId = Number(triggerPipelineId.value)
  isTriggering.value = true
  triggerError.value = null

  try {
    const newRun = await runsStore.triggerRun(pId)
    toast.success(`Đã kích hoạt lượt chạy #${newRun.id}`)
    showTriggerDialog.value = false
    // Refresh to get latest state
    const pipelineId = selectedPipelineFilter.value ? Number(selectedPipelineFilter.value) : undefined
    await runsStore.refreshRuns({ pipelineId })
    if (hasActiveRuns.value) {
      startPolling()
    }
  } catch (err: any) {
    if (err instanceof ApiError && err.status === 409) {
      triggerError.value = 'Pipeline đang có lượt chạy'
      toast.error('Pipeline đang có lượt chạy')
    } else {
      triggerError.value = err.message || 'Không thể kích hoạt lượt chạy'
      toast.error(triggerError.value || 'Lỗi khi kích hoạt lượt chạy')
    }
  } finally {
    isTriggering.value = false
  }
}

async function manualRefresh() {
  const pipelineId = selectedPipelineFilter.value ? Number(selectedPipelineFilter.value) : undefined
  await runsStore.fetchRuns({ pipelineId })
}

function navigateToRun(id: number) {
  router.push(`/runs/${id}`)
}
</script>

<template>
  <AppLayout>
    <div class="space-y-6 max-w-[1280px] mx-auto w-full">
      <!-- Header -->
      <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
        <div>
          <h1 class="text-2xl font-semibold text-fg tracking-tight">Lượt chạy</h1>
          <p class="text-sm text-muted-fg mt-0.5">
            Lịch sử và tiến trình các lượt chạy CI/CD trong hệ thống
          </p>
        </div>

        <div class="flex items-center gap-2">
          <AppButton
            variant="primary"
            class="w-full sm:w-auto"
            @click="openTriggerDialog"
          >
            <Play class="w-4 h-4 fill-current" aria-hidden="true" />
            <span>Chạy ngay</span>
          </AppButton>
        </div>
      </div>

      <!-- Filters & Actions Bar -->
      <div class="flex flex-col sm:flex-row items-stretch sm:items-center justify-between gap-3 bg-card p-3 rounded-[8px] border border-border">
        <div class="flex items-center gap-2 max-w-xs w-full">
          <Filter class="w-4 h-4 text-muted-fg shrink-0" aria-hidden="true" />
          <AppSelect
            v-model="selectedPipelineFilter"
            :options="pipelineFilterOptions"
            aria-label="Lọc theo pipeline"
          />
        </div>

        <div class="flex items-center justify-end gap-2">
          <AppButton
            variant="ghost"
            size="sm"
            :loading="runsStore.loading"
            aria-label="Làm mới danh sách"
            @click="manualRefresh"
          >
            <RotateCw class="w-4 h-4" aria-hidden="true" />
            <span class="hidden sm:inline">Làm mới</span>
          </AppButton>
        </div>
      </div>

      <!-- Loading State -->
      <div v-if="runsStore.loading && runsStore.runs.length === 0" class="py-12 flex justify-center items-center text-muted-fg">
        <RotateCw class="w-6 h-6 animate-spin mr-2" aria-hidden="true" />
        <span>Đang tải danh sách lượt chạy...</span>
      </div>

      <!-- Error State -->
      <div
        v-else-if="runsStore.error && runsStore.runs.length === 0"
        role="alert"
        class="p-4 rounded-[8px] border border-danger/40 bg-danger/10 text-danger text-sm flex items-center justify-between"
      >
        <span>{{ runsStore.error }}</span>
        <AppButton variant="secondary" size="sm" @click="manualRefresh">Thử lại</AppButton>
      </div>

      <!-- Empty State -->
      <EmptyState
        v-else-if="runsStore.runs.length === 0"
        title="Chưa có lượt chạy nào"
        description="Chưa có lượt chạy nào được thực hiện hoặc không khớp với bộ lọc đã chọn."
        :icon="PlayCircle"
      >
        <template #action>
          <AppButton variant="primary" @click="openTriggerDialog">
            <Play class="w-4 h-4 fill-current" aria-hidden="true" />
            <span>Chạy ngay</span>
          </AppButton>
        </template>
      </EmptyState>

      <!-- Runs Table -->
      <div v-else class="w-full">
        <AppTable>
          <template #header>
            <tr>
              <th scope="col" class="w-20">Mã</th>
              <th scope="col">Pipeline</th>
              <th scope="col">Trạng thái</th>
              <th scope="col">Tag (SHA)</th>
              <th scope="col">Image</th>
              <th scope="col">Bắt đầu lúc</th>
              <th scope="col">Thời lượng</th>
            </tr>
          </template>

          <tr
            v-for="run in runsStore.runs"
            :key="run.id"
            :data-run-id="run.id"
            tabindex="0"
            role="button"
            class="cursor-pointer focus-visible:outline-2 focus-visible:outline-ring"
            @click="navigateToRun(run.id)"
            @keydown.enter="navigateToRun(run.id)"
          >
            <!-- ID -->
            <td class="font-mono font-semibold text-fg whitespace-nowrap">
              #{{ run.id }}
            </td>

            <!-- Pipeline Name -->
            <td class="font-medium text-fg whitespace-nowrap">
              {{ runsStore.getPipelineName(run.pipelineId) }}
            </td>

            <!-- Status -->
            <td class="whitespace-nowrap">
              <StatusBadge :status="run.status" size="sm" />
            </td>

            <!-- Tag (Git SHA) -->
            <td class="whitespace-nowrap">
              <span
                v-if="run.tag"
                class="font-mono text-xs px-2 py-0.5 rounded bg-muted text-fg border border-border/60"
              >
                {{ run.tag }}
              </span>
              <span v-else class="text-muted-fg">-</span>
            </td>

            <!-- Image -->
            <td class="max-w-[220px]">
              <span
                v-if="run.image"
                class="font-mono text-xs text-muted-fg truncate block"
                :title="run.image"
              >
                {{ run.image }}
              </span>
              <span v-else class="text-muted-fg">-</span>
            </td>

            <!-- Started At -->
            <td class="text-xs text-muted-fg whitespace-nowrap tabular-nums">
              {{ formatDateTime(run.startedAt || run.createdAt) }}
            </td>

            <!-- Duration -->
            <td class="text-xs font-mono text-fg whitespace-nowrap tabular-nums">
              {{ formatDuration(run.startedAt, run.finishedAt, currentTime) }}
            </td>
          </tr>
        </AppTable>
      </div>

      <!-- Trigger Run Dialog -->
      <AppDialog
        v-model="showTriggerDialog"
        title="Kích hoạt lượt chạy mới"
      >
        <div class="space-y-4 py-2">
          <AppField
            label="Chọn Pipeline"
            required
            description="Lượt chạy sẽ được xếp hàng và thực hiện ngay khi slave sẵn sàng"
            :error="triggerError"
          >
            <template #default="{ id, error, ariaDescribedby }">
              <AppSelect
                :id="id"
                v-model="triggerPipelineId"
                :options="triggerPipelineOptions"
                placeholder="-- Chọn một pipeline --"
                :error="error"
                :aria-describedby="ariaDescribedby"
              />
            </template>
          </AppField>
        </div>

        <template #footer>
          <AppButton
            variant="ghost"
            :disabled="isTriggering"
            @click="showTriggerDialog = false"
          >
            Hủy
          </AppButton>
          <AppButton
            variant="primary"
            :loading="isTriggering"
            :disabled="!triggerPipelineId"
            @click="handleTriggerRun"
          >
            Chạy ngay
          </AppButton>
        </template>
      </AppDialog>
    </div>
  </AppLayout>
</template>
