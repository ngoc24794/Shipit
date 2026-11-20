<script setup lang="ts">
import { ref, computed, onMounted, onActivated } from 'vue'
import { useRouter } from 'vue-router'
import {
  Boxes,
  Plus,
  Pencil,
  Trash2,
  Play,
  GitBranch
} from 'lucide-vue-next'
import AppTable from '../../components/AppTable.vue'

defineOptions({ name: 'JobsView' })
import AppButton from '../../components/AppButton.vue'
import ConfirmDialog from '../../components/ConfirmDialog.vue'
import EmptyState from '../../components/EmptyState.vue'
import StatusBadge from '../../components/StatusBadge.vue'
import DataToolbar, { type DataToolbarFilter } from '../../components/DataToolbar.vue'
import DataPagination from '../../components/DataPagination.vue'
import SortableHeader from '../../components/SortableHeader.vue'
import { useJobsStore } from '../../stores/jobs'
import { useToast } from '../../composables/useToast'
import { useDataTable } from '../../composables/useDataTable'
import { useShortcuts } from '../../composables/useShortcuts'
import type { Job } from '../../api/types'
import { ApiError } from '../../api/client'

const router = useRouter()
const store = useJobsStore()
const toast = useToast()
const { registerMany } = useShortcuts()
const selectedIndex = ref(0)

const isConfirmOpen = ref(false)
const deletingJob = ref<Job | null>(null)
const deleteLoading = ref(false)
const triggeringJobId = ref<number | null>(null)

const table = useDataTable<Job>({
  mode: 'client',
  items: computed(() => store.jobs),
  defaultFilters: {
    status: '',
    namespace: ''
  },
  searchFields: ['name', 'gitUrl', 'gitBranch', 'namespace', 'imageName'],
  filterFns: {
    status: (j, val) => {
      if (val === 'none') return !j.lastRun
      return j.lastRun?.status === val
    },
    namespace: (j, val) => j.namespace === val
  },
  sortFns: {
    name: (a, b) => a.name.localeCompare(b.name, 'vi'),
    namespace: (a, b) => a.namespace.localeCompare(b.namespace, 'vi'),
    tasksCount: (a, b) => a.tasks.length - b.tasks.length,
    lastRun: (a, b) => (a.lastRun?.status ?? '').localeCompare(b.lastRun?.status ?? '')
  }
})

const toolbarFilters = computed<DataToolbarFilter[]>(() => {
  const namespaces = Array.from(
    new Set(store.jobs.map((j) => j.namespace).filter(Boolean))
  ).sort((a, b) => a.localeCompare(b))

  return [
    {
      key: 'status',
      label: 'Trạng thái lần chạy gần nhất',
      type: 'select',
      options: [
        { label: 'Tất cả trạng thái', value: '' },
        { label: 'Thành công', value: 'success' },
        { label: 'Thất bại', value: 'failed' },
        { label: 'Đang chạy', value: 'running' },
        { label: 'Đang chờ', value: 'queued' },
        { label: 'Đã huỷ', value: 'cancelled' },
        { label: 'Chưa chạy', value: 'none' }
      ]
    },
    {
      key: 'namespace',
      label: 'Namespace',
      type: 'select',
      options: [
        { label: 'Tất cả namespace', value: '' },
        ...namespaces.map((ns) => ({ label: ns, value: ns }))
      ]
    }
  ]
})

let hasMountedJobs = false
onMounted(async () => {
  hasMountedJobs = true
  try {
    await store.fetchJobs()
  } catch (err: any) {
    toast.error(err.message || 'Không thể tải danh sách Job')
  }
})

onActivated(() => {
  if (!hasMountedJobs) return
  store.fetchJobs({ silent: true }).catch(() => {})
})

function navigateToNew() {
  router.push('/jobs/new')
}

function navigateToEdit(id: number) {
  router.push(`/jobs/${id}`)
}

function moveJobSelection(delta: number) {
  const len = table.items.value.length
  if (len === 0) return
  selectedIndex.value = (selectedIndex.value + delta + len) % len
}

function openSelectedJob() {
  const item = table.items.value[selectedIndex.value]
  if (item) navigateToEdit(item.id)
}

registerMany([
  { id: 'jobs-new', keys: 'n', label: 'Tạo Job mới', scope: 'Danh sách Job', handler: navigateToNew },
  { id: 'jobs-next', keys: 'j', label: 'Chọn dòng tiếp theo', scope: 'Danh sách Job', handler: () => moveJobSelection(1) },
  { id: 'jobs-prev', keys: 'k', label: 'Chọn dòng trước đó', scope: 'Danh sách Job', handler: () => moveJobSelection(-1) },
  { id: 'jobs-open', keys: 'Enter', label: 'Mở Job đang chọn', scope: 'Danh sách Job', when: () => !isConfirmOpen.value, handler: openSelectedJob }
])

async function handleTriggerRun(job: Job) {
  triggeringJobId.value = job.id
  try {
    const run = await store.triggerJobRun(job.id)
    toast.success(`Đã kích hoạt lượt chạy #${run.id} cho Job "${job.name}"`)
    router.push(`/runs/${run.id}`)
  } catch (err: any) {
    if (err instanceof ApiError && err.status === 409) {
      toast.error('Job đang có lượt chạy hoạt động')
    } else {
      toast.error(err.message || 'Không thể kích hoạt lượt chạy')
    }
  } finally {
    triggeringJobId.value = null
  }
}

function openDeleteConfirm(job: Job) {
  deletingJob.value = job
  isConfirmOpen.value = true
}

async function handleDelete() {
  if (!deletingJob.value) return
  deleteLoading.value = true
  try {
    await store.deleteJob(deletingJob.value.id)
    toast.success(`Đã xoá Job "${deletingJob.value.name}"`)
    isConfirmOpen.value = false
    deletingJob.value = null
  } catch (err: any) {
    if (err instanceof ApiError && err.status === 409) {
      toast.error('Không thể xoá Job khi đang có lượt chạy hoạt động')
    } else {
      toast.error(err.problem?.detail || err.message || 'Không thể xoá Job')
    }
  } finally {
    deleteLoading.value = false
  }
}
</script>

<template>
  <div class="flex-1 min-h-0 flex flex-col">
    <div class="flex-1 min-h-0 flex flex-col gap-4">
      <!-- Page Header -->
      <div class="shrink-0 flex flex-row items-center justify-between gap-4 flex-nowrap">
        <div class="min-w-0">
          <h1 class="text-2xl font-bold tracking-tight text-fg flex items-center gap-2.5 whitespace-nowrap">
            <Boxes class="w-6 h-6 text-primary shrink-0" aria-hidden="true" />
            <span>Job</span>
          </h1>
          <p class="text-sm text-muted-fg mt-1 truncate">
            Danh sách job chạy tác vụ một lần trên Kubernetes.
          </p>
        </div>

        <AppButton
          variant="primary"
          class="shrink-0 whitespace-nowrap"
          aria-label="Thêm Job mới"
          @click="navigateToNew"
        >
          <Plus class="w-4 h-4 mr-1.5" aria-hidden="true" />
          <span>Thêm Job</span>
        </AppButton>
      </div>

      <!-- Loading -->
      <div v-if="store.loading && store.jobs.length === 0" class="py-16 text-center text-muted-fg">
        Đang tải danh sách Job...
      </div>

      <!-- Empty State -->
      <EmptyState
        v-else-if="store.jobs.length === 0"
        title="Chưa có Job nào"
        description="Chưa có Job nào được cấu hình."
        action-label="Thêm Job"
        @action="navigateToNew"
      >
        <template #icon>
          <Boxes class="w-10 h-10 text-muted-fg" aria-hidden="true" />
        </template>
      </EmptyState>

      <!-- Table Panel -->
      <div
        v-else
        class="w-full flex-1 min-h-0 flex flex-col rounded-[8px] border border-border bg-card overflow-hidden"
      >
        <DataToolbar
          :q="table.q.value"
          search-placeholder="Tìm tên job, kho mã, namespace..."
          :filters="toolbarFilters"
          :filter-values="table.filters"
          :start-item="table.startItem.value"
          :end-item="table.endItem.value"
          :total="table.total.value"
          :has-active-filters="table.hasActiveFilters.value"
          @update:q="(v) => (table.q.value = v)"
          @update:filter="(k, v) => (table.filters[k] = v)"
          @clear="table.clearFilters()"
        />

        <div
          v-if="table.items.value.length === 0"
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
                label="Tên"
                sort-key="name"
                :sort-by="table.sortBy.value"
                :sort-dir="table.sortDir.value"
                @sort="table.toggleSort"
              />
              <th scope="col" class="text-left py-3 px-4 text-xs font-semibold text-muted-fg whitespace-nowrap">
                Kho mã / Nhánh
              </th>
              <SortableHeader
                label="Namespace"
                sort-key="namespace"
                :sort-by="table.sortBy.value"
                :sort-dir="table.sortDir.value"
                class="w-[135px]"
                @sort="table.toggleSort"
              />
              <SortableHeader
                label="Số tác vụ"
                sort-key="tasksCount"
                align="center"
                :sort-by="table.sortBy.value"
                :sort-dir="table.sortDir.value"
                class="w-[105px]"
                @sort="table.toggleSort"
              />
              <SortableHeader
                label="Lượt chạy gần nhất"
                sort-key="lastRun"
                :sort-by="table.sortBy.value"
                :sort-dir="table.sortDir.value"
                class="w-[172px]"
                @sort="table.toggleSort"
              />
              <th scope="col" class="w-[258px] text-right py-3 px-4 text-xs font-semibold text-muted-fg whitespace-nowrap">
                Thao tác
              </th>
            </tr>
          </template>

          <tr
            v-for="job in table.items.value"
            :key="job.id"
            class="hover:bg-muted/40 transition-colors border-b border-border/60 last:border-b-0"
          >
            <!-- Name -->
            <td class="py-3 px-4 whitespace-nowrap">
              <router-link
                :to="`/jobs/${job.id}`"
                class="font-semibold text-fg hover:text-primary transition-colors focus-visible:outline-2 focus-visible:outline-ring rounded-[2px]"
              >
                {{ job.name }}
              </router-link>
            </td>

            <!-- Repo / Branch -->
            <td class="py-3 px-4 text-xs font-mono text-muted-fg max-w-[260px]" :title="`${job.gitUrl} (${job.gitBranch})`">
              <div class="flex items-center gap-1.5 min-w-0">
                <GitBranch class="w-3.5 h-3.5 flex-shrink-0 text-muted-fg" aria-hidden="true" />
                <span class="truncate" :title="job.gitUrl">{{ job.gitUrl }}</span>
                <span class="text-fg font-medium flex-shrink-0">({{ job.gitBranch }})</span>
              </div>
            </td>

            <!-- Namespace -->
            <td class="py-3 px-4 text-xs font-mono text-fg whitespace-nowrap">
              {{ job.namespace }}
            </td>

            <!-- Number of tasks -->
            <td class="py-3 px-4 text-center whitespace-nowrap">
              <span class="inline-flex items-center justify-center px-2 py-0.5 rounded text-xs font-semibold bg-muted text-fg border border-border/80">
                {{ job.tasks.length }}
              </span>
            </td>

            <!-- Last Run -->
            <td class="py-3 px-4 text-xs whitespace-nowrap">
              <div v-if="job.lastRun" class="flex items-center gap-2">
                <StatusBadge :status="job.lastRun.status" size="sm" />
                <router-link
                  :to="`/runs/${job.lastRun.id}`"
                  class="font-mono text-primary hover:underline"
                >
                  #{{ job.lastRun.id }}
                </router-link>
              </div>
              <span v-else class="text-muted-fg italic">Chưa chạy</span>
            </td>

            <!-- Actions -->
            <td class="py-3 px-4 text-right whitespace-nowrap">
              <div class="inline-flex items-center justify-end gap-1.5 whitespace-nowrap">
                <AppButton
                  variant="primary"
                  size="sm"
                  class="whitespace-nowrap shrink-0"
                  :loading="triggeringJobId === job.id"
                  :disabled="triggeringJobId !== null || deleteLoading"
                  aria-label="Chạy ngay"
                  title="Chạy ngay"
                  @click="handleTriggerRun(job)"
                >
                  <Play class="w-3.5 h-3.5 fill-current" aria-hidden="true" />
                  <span>Chạy ngay</span>
                </AppButton>

                <AppButton
                  variant="ghost"
                  size="sm"
                  class="whitespace-nowrap shrink-0"
                  aria-label="Sửa Job"
                  title="Chỉnh sửa cấu hình Job"
                  @click="navigateToEdit(job.id)"
                >
                  <Pencil class="w-3.5 h-3.5" aria-hidden="true" />
                  <span>Sửa</span>
                </AppButton>

                <AppButton
                  variant="ghost"
                  size="sm"
                  aria-label="Xoá Job"
                  title="Xoá Job"
                  class="text-muted-fg hover:text-danger hover:bg-danger/10 focus-visible:text-danger whitespace-nowrap shrink-0"
                  @click="openDeleteConfirm(job)"
                >
                  <Trash2 class="w-3.5 h-3.5" aria-hidden="true" />
                  <span>Xoá</span>
                </AppButton>
              </div>
            </td>
          </tr>
        </AppTable>

        <DataPagination
          :page="table.page.value"
          :page-size="table.pageSize.value"
          :total="table.total.value"
          :total-pages="table.totalPages.value"
          :loading="store.loading"
          @update:page="table.setPage"
          @update:page-size="table.setPageSize"
        />
      </div>
    </div>

    <!-- Delete Confirmation Dialog -->
    <ConfirmDialog
      v-model="isConfirmOpen"
      title="Xoá Job"
      :message="`Bạn có chắc chắn muốn xoá Job &quot;${deletingJob?.name}&quot;? Mọi lịch sử cấu hình của Job này sẽ bị xoá vĩnh viễn.`"
      confirm-text="Xoá Job"
      cancel-text="Huỷ"
      :loading="deleteLoading"
      :danger="true"
      @confirm="handleDelete"
    />
  </div>
</template>
