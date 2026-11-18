<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import {
  Workflow,
  Plus,
  Pencil,
  Trash2,
  Play,
  AlertCircle
} from 'lucide-vue-next'
import AppLayout from '../../layouts/AppLayout.vue'
import AppTable from '../../components/AppTable.vue'
import AppButton from '../../components/AppButton.vue'
import ConfirmDialog from '../../components/ConfirmDialog.vue'
import EmptyState from '../../components/EmptyState.vue'
import StatusBadge from '../../components/StatusBadge.vue'
import DataToolbar, { type DataToolbarFilter } from '../../components/DataToolbar.vue'
import DataPagination from '../../components/DataPagination.vue'
import SortableHeader from '../../components/SortableHeader.vue'
import { useConfigPipelinesStore } from '../../stores/configPipelines'
import { useConfigSlavesStore } from '../../stores/configSlaves'
import { useToast } from '../../composables/useToast'
import { useDataTable } from '../../composables/useDataTable'
import type { Pipeline } from '../../api/types'
import { ApiError } from '../../api/client'

const router = useRouter()
const store = useConfigPipelinesStore()
const slaveStore = useConfigSlavesStore()
const toast = useToast()

const isConfirmOpen = ref(false)
const deletingPipeline = ref<Pipeline | null>(null)
const deleteLoading = ref(false)

function getSlaveName(slaveId: number): string {
  const slave = slaveStore.slaves.find((s) => s.id === slaveId)
  return slave ? slave.name : `#${slaveId}`
}

const table = useDataTable<Pipeline>({
  mode: 'client',
  items: computed(() => store.pipelines),
  defaultFilters: {
    status: '',
    namespace: ''
  },
  searchFields: [
    'name',
    'gitUrl',
    'gitBranch',
    'imageName',
    'registryHost',
    'deployNamespace',
    'deployDeployment',
    (p) => getSlaveName(p.slaveId)
  ],
  filterFns: {
    status: (p, val) => {
      if (val === 'none') return !p.lastRun
      return p.lastRun?.status === val
    },
    namespace: (p, val) => p.deployNamespace === val
  },
  sortFns: {
    name: (a, b) => a.name.localeCompare(b.name, 'vi'),
    slave: (a, b) => getSlaveName(a.slaveId).localeCompare(getSlaveName(b.slaveId), 'vi'),
    deploy: (a, b) =>
      `${a.deployNamespace}/${a.deployDeployment}`.localeCompare(
        `${b.deployNamespace}/${b.deployDeployment}`,
        'vi'
      ),
    lastRun: (a, b) => (a.lastRun?.status ?? '').localeCompare(b.lastRun?.status ?? '')
  }
})

const toolbarFilters = computed<DataToolbarFilter[]>(() => {
  const namespaces = Array.from(
    new Set(store.pipelines.map((p) => p.deployNamespace).filter(Boolean))
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

function navigateToNew() {
  router.push('/pipelines/new')
}

function navigateToEdit(pipeline: Pipeline) {
  router.push(`/pipelines/${pipeline.id}/edit`)
}

function promptDelete(pipeline: Pipeline) {
  deletingPipeline.value = pipeline
  isConfirmOpen.value = true
}

async function handleDelete() {
  if (!deletingPipeline.value) return

  deleteLoading.value = true
  try {
    await store.deletePipeline(deletingPipeline.value.id)
    toast.success('Đã xoá pipeline thành công')
    isConfirmOpen.value = false
    deletingPipeline.value = null
  } catch (err: any) {
    if (err instanceof ApiError && err.status === 409) {
      toast.error('Không thể xoá pipeline khi đang có lượt chạy hoạt động')
    } else {
      toast.error(err.message || 'Xoá pipeline thất bại')
    }
  } finally {
    deleteLoading.value = false
  }
}

async function handleTriggerRun(pipeline: Pipeline) {
  try {
    const run = await store.triggerRun(pipeline.id)
    toast.success(`Đã kích hoạt lượt chạy #${run.id}`)
    router.push(`/runs/${run.id}`)
  } catch (err: any) {
    if (err instanceof ApiError && err.status === 409) {
      toast.error('Pipeline đang có lượt chạy')
    } else {
      toast.error(err.message || 'Kích hoạt lượt chạy thất bại')
    }
  }
}

onMounted(() => {
  store.fetchPipelines().catch(() => {})
  slaveStore.fetchSlaves().catch(() => {})
})
</script>

<template>
  <AppLayout>
    <div class="flex-1 min-h-0 flex flex-col gap-4">
      <!-- Header -->
      <div class="shrink-0 flex flex-row items-center justify-between gap-4 flex-nowrap">
        <div class="min-w-0">
          <h1 class="text-2xl font-semibold text-fg tracking-tight truncate">Cấu hình Pipeline</h1>
          <p class="text-sm text-muted-fg mt-1 truncate">
            Danh sách pipeline build từ Git và deploy lên Kubernetes.
          </p>
        </div>
        <AppButton class="shrink-0 whitespace-nowrap" @click="navigateToNew">
          <Plus class="w-4 h-4" aria-hidden="true" />
          <span>Thêm pipeline</span>
        </AppButton>
      </div>

      <!-- Error message banner -->
      <div
        v-if="store.error"
        role="alert"
        class="shrink-0 flex items-center gap-3 p-4 rounded-[8px] bg-danger/10 border border-danger/20 text-danger text-sm"
      >
        <AlertCircle class="w-5 h-5 flex-shrink-0" />
        <span>{{ store.error }}</span>
      </div>

      <!-- Loading state -->
      <div
        v-if="store.loading && store.pipelines.length === 0"
        class="flex items-center justify-center p-12 text-muted-fg text-sm"
      >
        <div class="flex items-center gap-2">
          <div class="w-4 h-4 border-2 border-primary border-t-transparent rounded-full animate-spin" />
          <span>Đang tải danh sách pipeline...</span>
        </div>
      </div>

      <!-- Empty state (no pipelines configured) -->
      <EmptyState
        v-else-if="!store.loading && store.pipelines.length === 0"
        title="Chưa có pipeline"
        description="Chưa có pipeline nào được cấu hình."
        :icon="Workflow"
      >
        <template #action>
          <AppButton @click="navigateToNew">
            <Plus class="w-4 h-4" aria-hidden="true" />
            <span>Thêm pipeline</span>
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
          search-placeholder="Tìm tên, git, image, đích deploy..."
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
                label="Tên pipeline"
                sort-key="name"
                :sort-by="table.sortBy.value"
                :sort-dir="table.sortDir.value"
                class="!px-3"
                @sort="table.toggleSort"
              />
              <th scope="col" class="py-3 !px-3 whitespace-nowrap">Nguồn Git</th>
              <SortableHeader
                label="Máy build"
                sort-key="slave"
                :sort-by="table.sortBy.value"
                :sort-dir="table.sortDir.value"
                class="!px-3"
                @sort="table.toggleSort"
              />
              <th scope="col" class="py-3 !px-3 whitespace-nowrap">Image Docker</th>
              <SortableHeader
                label="Đích Deploy"
                sort-key="deploy"
                :sort-by="table.sortBy.value"
                :sort-dir="table.sortDir.value"
                class="!px-3"
                @sort="table.toggleSort"
              />
              <SortableHeader
                label="Lượt chạy gần nhất"
                sort-key="lastRun"
                :sort-by="table.sortBy.value"
                :sort-dir="table.sortDir.value"
                class="!px-3"
                @sort="table.toggleSort"
              />
              <th scope="col" class="py-3 !px-3 text-right whitespace-nowrap">Thao tác</th>
            </tr>
          </template>

          <tr v-for="pipeline in table.items.value" :key="pipeline.id">
            <td class="!px-3 font-medium text-fg min-w-[160px]">
              <div class="flex items-center gap-2">
                <Workflow class="w-4 h-4 text-muted-fg flex-shrink-0" aria-hidden="true" />
                <span class="break-words">{{ pipeline.name }}</span>
              </div>
            </td>
            <td
              class="!px-3 font-mono text-xs text-muted-fg max-w-[135px]"
              :title="`${pipeline.gitUrl}@${pipeline.gitBranch || 'main'}`"
            >
              <div class="truncate">
                <span class="text-fg">{{ pipeline.gitUrl }}</span>
                <span class="text-primary font-semibold">@{{ pipeline.gitBranch || 'main' }}</span>
              </div>
            </td>
            <td
              class="!px-3 text-xs text-fg whitespace-nowrap max-w-[85px] truncate"
              :title="getSlaveName(pipeline.slaveId)"
            >
              {{ getSlaveName(pipeline.slaveId) }}
            </td>
            <td
              class="!px-3 font-mono text-xs text-fg max-w-[120px] truncate"
              :title="`${pipeline.registryHost}/${pipeline.imageName}`"
            >
              {{ pipeline.registryHost }}/{{ pipeline.imageName }}
            </td>
            <td
              class="!px-3 font-mono text-xs text-muted-fg max-w-[95px] truncate"
              :title="`${pipeline.deployNamespace}/${pipeline.deployDeployment}`"
            >
              <span class="text-fg">{{ pipeline.deployNamespace }}</span>/{{ pipeline.deployDeployment }}
            </td>
            <td class="!px-3 whitespace-nowrap">
              <StatusBadge
                v-if="pipeline.lastRun"
                :status="pipeline.lastRun.status"
                size="sm"
              />
              <span v-else class="text-muted-fg text-xs">—</span>
            </td>
            <td class="!px-3 text-right whitespace-nowrap">
              <div class="inline-flex items-center justify-end gap-1 whitespace-nowrap">
                <AppButton
                  variant="primary"
                  size="sm"
                  class="whitespace-nowrap shrink-0"
                  :loading="store.triggeringId === pipeline.id"
                  aria-label="Chạy ngay"
                  title="Kích hoạt chạy pipeline ngay"
                  @click="handleTriggerRun(pipeline)"
                >
                  <Play class="w-3.5 h-3.5 fill-current" aria-hidden="true" />
                  <span>Chạy ngay</span>
                </AppButton>
                <AppButton
                  variant="ghost"
                  size="sm"
                  class="whitespace-nowrap shrink-0"
                  aria-label="Sửa pipeline"
                  @click="navigateToEdit(pipeline)"
                >
                  <Pencil class="w-3.5 h-3.5" aria-hidden="true" />
                  <span>Sửa</span>
                </AppButton>
                <AppButton
                  variant="ghost"
                  size="sm"
                  class="text-muted-fg hover:text-danger hover:bg-danger/10 focus-visible:text-danger whitespace-nowrap shrink-0"
                  aria-label="Xoá pipeline"
                  @click="promptDelete(pipeline)"
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

    <!-- Confirm Delete Dialog -->
    <ConfirmDialog
      v-model="isConfirmOpen"
      title="Xoá pipeline"
      :message="`Bạn có chắc chắn muốn xoá pipeline &quot;${deletingPipeline?.name}&quot;? Hành động này không thể hoàn tác.`"
      confirm-text="Xoá pipeline"
      cancel-text="Huỷ"
      :loading="deleteLoading"
      :danger="true"
      @confirm="handleDelete"
      @cancel="isConfirmOpen = false"
    />
  </AppLayout>
</template>
