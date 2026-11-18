<script setup lang="ts">
import { ref, onMounted } from 'vue'
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
import { useConfigPipelinesStore } from '../../stores/configPipelines'
import { useConfigSlavesStore } from '../../stores/configSlaves'
import { useToast } from '../../composables/useToast'
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
    <div class="space-y-6">
      <!-- Header -->
      <div class="flex flex-row items-center justify-between gap-4 flex-nowrap">
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
        class="flex items-center gap-3 p-4 rounded-[8px] bg-danger/10 border border-danger/20 text-danger text-sm"
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

      <!-- Empty state -->
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

      <!-- Table -->
      <AppTable v-else>
        <template #header>
          <tr>
            <th scope="col" class="py-3 !px-3 whitespace-nowrap">Tên pipeline</th>
            <th scope="col" class="py-3 !px-3 whitespace-nowrap">Nguồn Git</th>
            <th scope="col" class="py-3 !px-3 whitespace-nowrap">Máy build</th>
            <th scope="col" class="py-3 !px-3 whitespace-nowrap">Image Docker</th>
            <th scope="col" class="py-3 !px-3 whitespace-nowrap">Đích Deploy</th>
            <th scope="col" class="py-3 !px-3 whitespace-nowrap">Lượt chạy gần nhất</th>
            <th scope="col" class="py-3 !px-3 text-right whitespace-nowrap">Thao tác</th>
          </tr>
        </template>

        <tr v-for="pipeline in store.pipelines" :key="pipeline.id">
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
