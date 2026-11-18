<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import {
  Boxes,
  Plus,
  Pencil,
  Trash2,
  Play,
  GitBranch
} from 'lucide-vue-next'
import AppLayout from '../../layouts/AppLayout.vue'
import AppTable from '../../components/AppTable.vue'
import AppButton from '../../components/AppButton.vue'
import ConfirmDialog from '../../components/ConfirmDialog.vue'
import EmptyState from '../../components/EmptyState.vue'
import StatusBadge from '../../components/StatusBadge.vue'
import { useJobsStore } from '../../stores/jobs'
import { useToast } from '../../composables/useToast'
import type { Job } from '../../api/types'
import { ApiError } from '../../api/client'

const router = useRouter()
const store = useJobsStore()
const toast = useToast()

const isConfirmOpen = ref(false)
const deletingJob = ref<Job | null>(null)
const deleteLoading = ref(false)
const triggeringJobId = ref<number | null>(null)

onMounted(async () => {
  try {
    await store.fetchJobs()
  } catch (err: any) {
    toast.error(err.message || 'Không thể tải danh sách Job')
  }
})

function navigateToNew() {
  router.push('/jobs/new')
}

function navigateToEdit(id: number) {
  router.push(`/jobs/${id}`)
}

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
  <AppLayout>
    <div class="space-y-6">
      <!-- Page Header -->
      <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
        <div>
          <h1 class="text-2xl font-bold tracking-tight text-fg flex items-center gap-2.5">
            <Boxes class="w-6 h-6 text-primary" aria-hidden="true" />
            <span>Job</span>
          </h1>
          <p class="text-sm text-muted-fg mt-1">
            Danh sách job chạy tác vụ một lần trên Kubernetes.
          </p>
        </div>

        <AppButton
          variant="primary"
          aria-label="Thêm Job mới"
          @click="navigateToNew"
        >
          <Plus class="w-4 h-4 mr-1.5" aria-hidden="true" />
          <span>Thêm Job</span>
        </AppButton>
      </div>

      <!-- Content: Table or Empty State -->
      <div v-if="store.loading && store.jobs.length === 0" class="py-16 text-center text-muted-fg">
        Đang tải danh sách Job...
      </div>

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

      <AppTable v-else>
        <template #header>
          <tr>
            <th scope="col" class="text-left py-3 px-4 text-xs font-semibold uppercase tracking-wider text-muted-fg whitespace-nowrap">
              Tên
            </th>
            <th scope="col" class="text-left py-3 px-4 text-xs font-semibold uppercase tracking-wider text-muted-fg whitespace-nowrap">
              Kho mã / Nhánh
            </th>
            <th scope="col" class="text-left py-3 px-4 text-xs font-semibold uppercase tracking-wider text-muted-fg whitespace-nowrap">
              Namespace
            </th>
            <th scope="col" class="text-center py-3 px-4 text-xs font-semibold uppercase tracking-wider text-muted-fg whitespace-nowrap">
              Số tác vụ
            </th>
            <th scope="col" class="text-left py-3 px-4 text-xs font-semibold uppercase tracking-wider text-muted-fg whitespace-nowrap">
              Lượt chạy gần nhất
            </th>
            <th scope="col" class="text-right py-3 px-4 text-xs font-semibold uppercase tracking-wider text-muted-fg whitespace-nowrap">
              Thao tác
            </th>
          </tr>
        </template>

        <tr
          v-for="job in store.jobs"
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
                variant="ghost"
                size="sm"
                :loading="triggeringJobId === job.id"
                :disabled="triggeringJobId !== null || deleteLoading"
                aria-label="Chạy ngay"
                title="Chạy ngay"
                @click="handleTriggerRun(job)"
              >
                <Play class="w-3.5 h-3.5 text-primary" aria-hidden="true" />
                <span class="hidden sm:inline">Chạy ngay</span>
              </AppButton>

              <AppButton
                variant="ghost"
                size="sm"
                aria-label="Sửa Job"
                title="Chỉnh sửa cấu hình Job"
                @click="navigateToEdit(job.id)"
              >
                <Pencil class="w-3.5 h-3.5" aria-hidden="true" />
                <span class="hidden sm:inline">Sửa</span>
              </AppButton>

              <AppButton
                variant="ghost"
                size="sm"
                aria-label="Xoá Job"
                title="Xoá Job"
                class="hover:text-danger"
                @click="openDeleteConfirm(job)"
              >
                <Trash2 class="w-3.5 h-3.5 text-danger" aria-hidden="true" />
                <span class="hidden sm:inline">Xoá</span>
              </AppButton>
            </div>
          </td>
        </tr>
      </AppTable>

      <!-- Delete Confirmation Dialog -->
      <ConfirmDialog
        v-model="isConfirmOpen"
        title="Xoá Job"
        :message="`Xoá Job &quot;${deletingJob?.name}&quot;? Thao tác này không thể hoàn tác.`"
        confirm-text="Xoá Job"
        cancel-text="Huỷ"
        :loading="deleteLoading"
        :danger="true"
        @confirm="handleDelete"
      />
    </div>
  </AppLayout>
</template>
