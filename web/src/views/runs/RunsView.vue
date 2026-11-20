<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount, watch } from 'vue'
import { useRouter } from 'vue-router'
import { Play, RotateCw, PlayCircle, Search, Check, ChevronDown } from 'lucide-vue-next'
import AppLayout from '../../layouts/AppLayout.vue'
import AppTable from '../../components/AppTable.vue'
import AppButton from '../../components/AppButton.vue'
import AppDialog from '../../components/AppDialog.vue'
import AppField from '../../components/AppField.vue'
import DataToolbar, { type DataToolbarFilter } from '../../components/DataToolbar.vue'
import DataPagination from '../../components/DataPagination.vue'
import SortableHeader from '../../components/SortableHeader.vue'
import StatusBadge from '../../components/StatusBadge.vue'
import EmptyState from '../../components/EmptyState.vue'
import { useRunsStore } from '../../stores/runs'
import { useToast } from '../../composables/useToast'
import { useDataTable } from '../../composables/useDataTable'
import { useShortcuts } from '../../composables/useShortcuts'
import { formatDateTime, formatDuration } from './formatters'
import { ApiError } from '../../api/client'
import type { GetRunsParams, Run, RunStatus } from '../../api/types'

const router = useRouter()
const runsStore = useRunsStore()
const toast = useToast()
const { registerMany } = useShortcuts()
const selectedIndex = ref(0)

const showTriggerDialog = ref(false)
const triggerKind = ref<'pipeline' | 'job'>('pipeline')
const triggerTargetId = ref<number | null>(null)
const triggerSearchQuery = ref('')
const comboboxOpen = ref(false)
const isTriggering = ref(false)
const triggerError = ref<string | null>(null)
const currentTime = ref(Date.now())

let pollInterval: ReturnType<typeof setInterval> | null = null
let secondInterval: ReturnType<typeof setInterval> | null = null

function buildRunsParams(params: {
  q: string
  filters: Record<string, string>
  page: number
  pageSize: number
}): GetRunsParams {
  const apiParams: GetRunsParams = {
    page: params.page,
    pageSize: params.pageSize
  }
  if (params.q) {
    apiParams.q = params.q
  }
  if (params.filters.kind === 'pipeline' || params.filters.kind === 'job') {
    apiParams.kind = params.filters.kind
  }
  if (params.filters.status) {
    apiParams.status = params.filters.status as RunStatus
  }
  if (params.filters.target) {
    const [tKind, tId] = params.filters.target.split(':')
    const parsedId = Number(tId)
    if (!Number.isNaN(parsedId) && parsedId > 0) {
      if (tKind === 'pipeline') apiParams.pipelineId = parsedId
      if (tKind === 'job') apiParams.jobId = parsedId
    }
  }
  return apiParams
}

const table = useDataTable<Run>({
  mode: 'server',
  defaultFilters: {
    kind: '',
    status: '',
    target: ''
  },
  defaultPageSize: 20,
  fetchFn: async (params) => {
    const apiParams = buildRunsParams(params)
    return await runsStore.fetchRunsPaged(apiParams)
  }
})

const toolbarFilters = computed<DataToolbarFilter[]>(() => {
  const targetOptions: Array<{ label: string; value: string }> = [
    { label: 'Tất cả Pipeline / Job', value: '' }
  ]
  if (table.filters.kind !== 'job') {
    for (const p of runsStore.pipelines) {
      targetOptions.push({ label: `Pipeline: ${p.name}`, value: `pipeline:${p.id}` })
    }
  }
  if (table.filters.kind !== 'pipeline') {
    for (const j of runsStore.jobs) {
      targetOptions.push({ label: `Job: ${j.name}`, value: `job:${j.id}` })
    }
  }

  return [
    {
      key: 'kind',
      label: 'Loại lượt chạy',
      type: 'segmented',
      options: [
        { label: 'Tất cả', value: '' },
        { label: 'Pipeline', value: 'pipeline' },
        { label: 'Job', value: 'job' }
      ]
    },
    {
      key: 'status',
      label: 'Trạng thái',
      type: 'select',
      options: [
        { label: 'Tất cả trạng thái', value: '' },
        { label: 'Đang chờ', value: 'queued' },
        { label: 'Đang chạy', value: 'running' },
        { label: 'Thành công', value: 'success' },
        { label: 'Thất bại', value: 'failed' },
        { label: 'Đã huỷ', value: 'cancelled' }
      ]
    },
    {
      key: 'target',
      label: 'Pipeline / Job',
      type: 'select',
      options: targetOptions
    }
  ]
})

function getRunTargetName(run: Run): string {
  return run.kind === 'job'
    ? runsStore.getJobName(run.jobId)
    : runsStore.getPipelineName(run.pipelineId || 0)
}

const displayedRuns = computed<Run[]>(() => {
  const list = [...table.items.value]
  if (!table.sortBy.value || !table.sortDir.value) return list
  const dir = table.sortDir.value === 'asc' ? 1 : -1
  const key = table.sortBy.value
  list.sort((a, b) => {
    if (key === 'id') return (a.id - b.id) * dir
    if (key === 'kind') {
      const ka = a.kind === 'job' ? 'job' : 'pipeline'
      const kb = b.kind === 'job' ? 'job' : 'pipeline'
      return ka.localeCompare(kb) * dir
    }
    if (key === 'name') {
      return getRunTargetName(a).localeCompare(getRunTargetName(b), 'vi') * dir
    }
    if (key === 'status') {
      return a.status.localeCompare(b.status) * dir
    }
    if (key === 'startedAt') {
      const ta = new Date(a.startedAt || a.createdAt).getTime()
      const tb = new Date(b.startedAt || b.createdAt).getTime()
      return (ta - tb) * dir
    }
    return 0
  })
  return list
})

const triggerOptions = computed(() => {
  if (triggerKind.value === 'job') {
    return runsStore.jobs.map((j) => ({ id: j.id, name: j.name }))
  }
  return runsStore.pipelines.map((p) => ({ id: p.id, name: p.name }))
})

const filteredTriggerOptions = computed(() => {
  const q = triggerSearchQuery.value.trim().toLowerCase()
  if (!q) return triggerOptions.value
  return triggerOptions.value.filter(
    (opt) => opt.name.toLowerCase().includes(q) || `#${opt.id}`.includes(q)
  )
})

const selectedTriggerOption = computed(() =>
  triggerOptions.value.find((opt) => opt.id === triggerTargetId.value) || null
)

const hasActiveRuns = computed(() =>
  table.items.value.some((r) => r.status === 'queued' || r.status === 'running')
)

function currentFetchParams(): GetRunsParams {
  return buildRunsParams({
    q: table.q.value.trim(),
    filters: { ...table.filters },
    page: table.page.value,
    pageSize: table.pageSize.value
  })
}

function startPolling() {
  if (pollInterval) return
  pollInterval = setInterval(async () => {
    await table.load({ silent: true }).catch(() => {})
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

// If kind changes and target doesn't match kind, clear target
watch(
  () => table.filters.kind,
  (newKind) => {
    if (newKind === 'pipeline' && table.filters.target.startsWith('job:')) {
      table.filters.target = ''
    } else if (newKind === 'job' && table.filters.target.startsWith('pipeline:')) {
      table.filters.target = ''
    }
  }
)

watch(triggerKind, (nextKind) => {
  triggerError.value = null
  triggerSearchQuery.value = ''
  const list = nextKind === 'job' ? runsStore.jobs : runsStore.pipelines
  triggerTargetId.value = list.length > 0 ? list[0].id : null
})

onMounted(async () => {
  secondInterval = setInterval(() => {
    currentTime.value = Date.now()
  }, 1000)

  await Promise.all([
    runsStore.fetchPipelines(),
    runsStore.fetchJobs(),
    table.load().catch(() => {})
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
  triggerSearchQuery.value = ''
  comboboxOpen.value = false
  if (table.filters.target.startsWith('job:')) {
    triggerKind.value = 'job'
    triggerTargetId.value = Number(table.filters.target.split(':')[1]) || null
  } else if (table.filters.target.startsWith('pipeline:')) {
    triggerKind.value = 'pipeline'
    triggerTargetId.value = Number(table.filters.target.split(':')[1]) || null
  } else if (table.filters.kind === 'job') {
    triggerKind.value = 'job'
    triggerTargetId.value = runsStore.jobs[0]?.id ?? null
  } else {
    triggerKind.value = 'pipeline'
    triggerTargetId.value = runsStore.pipelines[0]?.id ?? null
  }
  showTriggerDialog.value = true
}

function selectTriggerOption(opt: { id: number; name: string }) {
  triggerTargetId.value = opt.id
  triggerSearchQuery.value = ''
  comboboxOpen.value = false
  triggerError.value = null
}

async function handleTriggerRun() {
  if (!triggerTargetId.value) return
  const targetId = Number(triggerTargetId.value)
  isTriggering.value = true
  triggerError.value = null

  try {
    const newRun =
      triggerKind.value === 'job'
        ? await runsStore.runJob(targetId)
        : await runsStore.triggerRun(targetId)
    toast.success(`Đã kích hoạt lượt chạy #${newRun.id}`)
    showTriggerDialog.value = false
    router.push(`/runs/${newRun.id}`)
  } catch (err: any) {
    if (err instanceof ApiError && err.status === 409) {
      const msg =
        triggerKind.value === 'job'
          ? 'Job đang có lượt chạy'
          : 'Pipeline đang có lượt chạy'
      triggerError.value = msg
      toast.error(msg)
    } else {
      triggerError.value = err.message || 'Không thể kích hoạt lượt chạy'
      toast.error(triggerError.value || 'Lỗi khi kích hoạt lượt chạy')
    }
  } finally {
    isTriggering.value = false
  }
}

async function manualRefresh() {
  await Promise.all([
    table.load().catch(() => {}),
    runsStore.fetchJobs()
  ])
}

function navigateToRun(id: number) {
  router.push(`/runs/${id}`)
}

function moveRunSelection(delta: number) {
  const len = table.items.value.length
  if (len === 0) return
  selectedIndex.value = (selectedIndex.value + delta + len) % len
}

function openSelectedRun() {
  const item = table.items.value[selectedIndex.value]
  if (item) navigateToRun(item.id)
}

registerMany([
  { id: 'runs-new', keys: 'n', label: 'Mở hộp thoại Chạy ngay', scope: 'Danh sách lượt chạy', handler: openTriggerDialog },
  { id: 'runs-next', keys: 'j', label: 'Chọn dòng tiếp theo', scope: 'Danh sách lượt chạy', handler: () => moveRunSelection(1) },
  { id: 'runs-prev', keys: 'k', label: 'Chọn dòng trước đó', scope: 'Danh sách lượt chạy', handler: () => moveRunSelection(-1) },
  { id: 'runs-open', keys: 'Enter', label: 'Mở lượt chạy đang chọn', scope: 'Danh sách lượt chạy', when: () => !showTriggerDialog.value, handler: openSelectedRun }
])
</script>

<template>
  <AppLayout>
    <div class="flex-1 min-h-0 flex flex-col gap-4 w-full">
      <!-- Header -->
      <div class="shrink-0 flex flex-row items-center justify-between gap-4 flex-nowrap">
        <div class="min-w-0">
          <h1 class="text-2xl font-semibold text-fg tracking-tight truncate">Lượt chạy</h1>
          <p class="text-sm text-muted-fg mt-0.5 truncate">
            Lịch sử và trạng thái các lượt chạy pipeline và job.
          </p>
        </div>

        <div class="flex items-center gap-2 shrink-0 whitespace-nowrap">
          <AppButton
            variant="primary"
            class="whitespace-nowrap"
            @click="openTriggerDialog"
          >
            <Play class="w-4 h-4 fill-current" aria-hidden="true" />
            <span>Chạy ngay</span>
          </AppButton>
        </div>
      </div>

      <!-- Loading State (Skeleton) on initial load -->
      <div
        v-if="runsStore.loading && table.total.value === 0 && !table.hasActiveFilters.value"
        class="rounded-[8px] border border-border bg-card p-4 space-y-3"
        aria-busy="true"
        aria-label="Đang tải danh sách lượt chạy"
      >
        <div v-for="i in 5" :key="i" class="h-9 rounded bg-muted/70 animate-pulse" />
      </div>

      <!-- Error State -->
      <div
        v-else-if="runsStore.error && table.total.value === 0"
        role="alert"
        class="p-4 rounded-[8px] border border-danger/40 bg-danger/10 text-danger text-sm flex items-center justify-between"
      >
        <span>{{ runsStore.error }}</span>
        <AppButton variant="secondary" size="sm" @click="manualRefresh">Thử lại</AppButton>
      </div>

      <!-- Empty State (no runs at all) -->
      <EmptyState
        v-else-if="!runsStore.loading && table.total.value === 0 && !table.hasActiveFilters.value"
        title="Chưa có lượt chạy nào"
        description="Chưa có lượt chạy nào được kích hoạt."
        :icon="PlayCircle"
      >
        <template #action>
          <AppButton variant="primary" @click="openTriggerDialog">
            <Play class="w-4 h-4 fill-current" aria-hidden="true" />
            <span>Chạy ngay</span>
          </AppButton>
        </template>
      </EmptyState>

      <!-- Table Panel -->
      <div
        v-else
        class="w-full flex-1 min-h-0 flex flex-col rounded-[8px] border border-border bg-card overflow-hidden"
      >
        <DataToolbar
          :q="table.q.value"
          search-placeholder="Tìm tên, tag..."
          :filters="toolbarFilters"
          :filter-values="table.filters"
          :start-item="table.startItem.value"
          :end-item="table.endItem.value"
          :total="table.total.value"
          :has-active-filters="table.hasActiveFilters.value"
          @update:q="(v) => (table.q.value = v)"
          @update:filter="(k, v) => (table.filters[k] = v)"
          @clear="table.clearFilters()"
        >
          <template #actions>
            <AppButton
              variant="ghost"
              size="sm"
              :loading="runsStore.loading"
              aria-label="Làm mới danh sách"
              @click="manualRefresh"
            >
              <RotateCw class="w-4 h-4" aria-hidden="true" />
              <span>Làm mới</span>
            </AppButton>
          </template>
        </DataToolbar>

        <!-- No results matching filter -->
        <div
          v-if="displayedRuns.length === 0"
          class="flex-1 min-h-0 flex flex-col items-center justify-center p-8 text-center"
        >
          <p class="text-sm font-medium text-fg mb-1">Không có kết quả phù hợp</p>
          <p class="text-xs text-muted-fg mb-4">Thử đổi từ khoá tìm kiếm hoặc xoá bộ lọc hiện tại.</p>
          <AppButton variant="secondary" size="sm" @click="table.clearFilters()">
            Xoá bộ lọc
          </AppButton>
        </div>

        <AppTable v-else :bordered="false">
          <template #header>
            <tr>
              <SortableHeader
                label="Mã"
                sort-key="id"
                :sort-by="table.sortBy.value"
                :sort-dir="table.sortDir.value"
                class="w-20"
                @sort="table.toggleSort"
              />
              <SortableHeader
                label="Loại"
                sort-key="kind"
                :sort-by="table.sortBy.value"
                :sort-dir="table.sortDir.value"
                @sort="table.toggleSort"
              />
              <SortableHeader
                label="Tên"
                sort-key="name"
                :sort-by="table.sortBy.value"
                :sort-dir="table.sortDir.value"
                @sort="table.toggleSort"
              />
              <SortableHeader
                label="Trạng thái"
                sort-key="status"
                :sort-by="table.sortBy.value"
                :sort-dir="table.sortDir.value"
                @sort="table.toggleSort"
              />
              <th scope="col" class="whitespace-nowrap">Tag (SHA)</th>
              <th scope="col" class="whitespace-nowrap">Image</th>
              <SortableHeader
                label="Bắt đầu lúc"
                sort-key="startedAt"
                :sort-by="table.sortBy.value"
                :sort-dir="table.sortDir.value"
                @sort="table.toggleSort"
              />
              <th scope="col" class="whitespace-nowrap">Thời lượng</th>
            </tr>
          </template>

          <tr
            v-for="run in displayedRuns"
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

            <!-- Loại (Kind) -->
            <td class="whitespace-nowrap">
              <span
                class="px-2 py-0.5 text-xs rounded font-medium border"
                :class="run.kind === 'job'
                  ? 'bg-info/10 text-info border-info/30'
                  : 'bg-muted text-muted-fg border-border'"
              >
                {{ run.kind === 'job' ? 'Job' : 'Pipeline' }}
              </span>
            </td>

            <!-- Pipeline / Job Name with link -->
            <td class="font-medium text-fg whitespace-nowrap">
              <router-link
                v-if="run.kind === 'job'"
                :to="`/jobs/${run.jobId}`"
                class="hover:underline text-fg font-medium"
                @click.stop
              >
                {{ runsStore.getJobName(run.jobId) }}
              </router-link>
              <router-link
                v-else
                to="/pipelines"
                class="hover:underline text-fg font-medium"
                @click.stop
              >
                {{ runsStore.getPipelineName(run.pipelineId || 0) }}
              </router-link>
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

        <DataPagination
          :page="table.page.value"
          :page-size="table.pageSize.value"
          :total="table.total.value"
          :total-pages="table.totalPages.value"
          :loading="runsStore.loading"
          @update:page="table.setPage"
          @update:page-size="table.setPageSize"
        />
      </div>

      <!-- Trigger Run Dialog (2 fields: Kind segmented + Searchable Combobox) -->
      <AppDialog
        v-model="showTriggerDialog"
        title="Kích hoạt lượt chạy mới"
      >
        <div class="space-y-4 py-2">
          <!-- Field 1: Kind -->
          <AppField label="Loại" required>
            <div
              role="group"
              aria-label="Loại đối tượng chạy"
              class="inline-flex w-full p-1 rounded-[6px] bg-muted border border-border"
            >
              <button
                type="button"
                :aria-pressed="triggerKind === 'pipeline'"
                :class="[
                  'flex-1 py-1.5 text-sm font-medium rounded-[4px] transition-colors cursor-pointer',
                  triggerKind === 'pipeline'
                    ? 'bg-primary text-on-primary shadow-xs'
                    : 'text-muted-fg hover:text-fg'
                ]"
                @click="triggerKind = 'pipeline'"
              >
                Pipeline
              </button>
              <button
                type="button"
                :aria-pressed="triggerKind === 'job'"
                :class="[
                  'flex-1 py-1.5 text-sm font-medium rounded-[4px] transition-colors cursor-pointer',
                  triggerKind === 'job'
                    ? 'bg-primary text-on-primary shadow-xs'
                    : 'text-muted-fg hover:text-fg'
                ]"
                @click="triggerKind = 'job'"
              >
                Job
              </button>
            </div>
          </AppField>

          <!-- Field 2: Searchable Combobox -->
          <AppField
            :label="triggerKind === 'job' ? 'Chọn Job' : 'Chọn Pipeline'"
            required
            description="Lượt chạy được xếp hàng và chạy khi máy build sẵn sàng."
            :error="triggerError"
          >
            <template #default="{ id, ariaDescribedby }">
              <div class="relative">
                <button
                  :id="id"
                  type="button"
                  role="combobox"
                  :aria-expanded="comboboxOpen"
                  :aria-describedby="ariaDescribedby"
                  class="w-full h-10 px-3 text-sm rounded-[6px] bg-card text-fg border border-border flex items-center justify-between gap-2 cursor-pointer focus-visible:outline-2 focus-visible:outline-ring focus-visible:border-primary"
                  @click="comboboxOpen = !comboboxOpen"
                >
                  <span class="truncate">
                    {{
                      selectedTriggerOption
                        ? `${selectedTriggerOption.name} (#${selectedTriggerOption.id})`
                        : triggerKind === 'job'
                          ? 'Chọn job...'
                          : 'Chọn pipeline...'
                    }}
                  </span>
                  <ChevronDown class="w-4 h-4 text-muted-fg shrink-0" aria-hidden="true" />
                </button>

                <div
                  v-if="comboboxOpen"
                  class="mt-1 w-full rounded-[6px] border border-border bg-card shadow-lg overflow-hidden"
                >
                  <div class="p-2 border-b border-border bg-muted/40 relative">
                    <Search
                      class="w-3.5 h-3.5 text-muted-fg absolute left-4 top-1/2 -translate-y-1/2 pointer-events-none"
                      aria-hidden="true"
                    />
                    <input
                      v-model="triggerSearchQuery"
                      type="text"
                      :placeholder="triggerKind === 'job' ? 'Tìm job...' : 'Tìm pipeline...'"
                      aria-label="Lọc danh sách"
                      class="w-full h-8 pl-7 pr-2 text-xs rounded-[4px] bg-bg text-fg border border-border focus-visible:outline-2 focus-visible:outline-ring"
                    />
                  </div>
                  <ul
                    role="listbox"
                    class="max-h-48 overflow-auto divide-y divide-border/50"
                  >
                    <li
                      v-if="filteredTriggerOptions.length === 0"
                      class="px-3 py-2.5 text-xs text-muted-fg text-center"
                    >
                      Không tìm thấy kết quả
                    </li>
                    <li
                      v-for="opt in filteredTriggerOptions"
                      :key="opt.id"
                      role="option"
                      :aria-selected="opt.id === triggerTargetId"
                      :class="[
                        'px-3 py-2 text-sm flex items-center justify-between cursor-pointer transition-colors',
                        opt.id === triggerTargetId
                          ? 'bg-primary/10 text-primary font-medium'
                          : 'text-fg hover:bg-muted'
                      ]"
                      @click="selectTriggerOption(opt)"
                    >
                      <span class="truncate">{{ opt.name }} (#{{ opt.id }})</span>
                      <Check
                        v-if="opt.id === triggerTargetId"
                        class="w-4 h-4 text-primary shrink-0"
                        aria-hidden="true"
                      />
                    </li>
                  </ul>
                </div>
              </div>
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
            :disabled="!triggerTargetId"
            @click="handleTriggerRun"
          >
            Chạy ngay
          </AppButton>
        </template>
      </AppDialog>
    </div>
  </AppLayout>
</template>
