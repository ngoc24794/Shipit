<script setup lang="ts">
import { ref, onMounted } from 'vue'
import {
  ListChecks,
  Plus,
  Trash2,
  ShieldAlert
} from 'lucide-vue-next'
import AppLayout from '../../layouts/AppLayout.vue'
import AppTable from '../../components/AppTable.vue'
import AppButton from '../../components/AppButton.vue'
import AppInput from '../../components/AppInput.vue'
import AppSelect from '../../components/AppSelect.vue'
import AppField from '../../components/AppField.vue'
import ConfirmDialog from '../../components/ConfirmDialog.vue'
import EmptyState from '../../components/EmptyState.vue'
import { useJobsAllowStore } from '../../stores/jobsAllow'
import { useToast } from '../../composables/useToast'
import type { JobAllowEntry, JobAllowKind } from '../../api/types'
import { ApiError } from '../../api/client'

const store = useJobsAllowStore()
const toast = useToast()

const isConfirmOpen = ref(false)
const deletingEntry = ref<JobAllowEntry | null>(null)
const actionLoading = ref(false)
const deleteLoading = ref(false)

const form = ref<{
  namespace: string
  kind: JobAllowKind
  name: string
}>({
  namespace: '',
  kind: 'namespace',
  name: ''
})

const formErrors = ref<Record<string, string>>({})

const kindOptions = [
  { label: 'Namespace', value: 'namespace' },
  { label: 'ConfigMap', value: 'configmap' },
  { label: 'Secret', value: 'secret' },
  { label: 'pullSecret (ImagePullSecret)', value: 'pullsecret' }
]

function kindBadgeClass(kind: JobAllowKind): string {
  switch (kind) {
    case 'namespace':
      return 'bg-info/10 text-info border-info/30'
    case 'configmap':
      return 'bg-primary/10 text-primary border-primary/30'
    case 'secret':
      return 'bg-warn/10 text-warn border-warn/30'
    case 'pullsecret':
      return 'bg-purple-500/10 text-purple-400 border-purple-500/30'
    default:
      return 'bg-muted text-muted-fg border-border'
  }
}

function kindLabel(kind: JobAllowKind): string {
  switch (kind) {
    case 'namespace':
      return 'Namespace'
    case 'configmap':
      return 'ConfigMap'
    case 'secret':
      return 'Secret'
    case 'pullsecret':
      return 'pullSecret'
    default:
      return kind
  }
}

function formatDate(dateStr?: string | null): string {
  if (!dateStr) return '—'
  try {
    const d = new Date(dateStr)
    return d.toLocaleString('vi-VN', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit'
    })
  } catch {
    return dateStr
  }
}

onMounted(async () => {
  try {
    await store.fetchEntries()
  } catch (err: any) {
    toast.error(err.message || 'Không thể tải danh sách allowlist')
  }
})

function validateForm(): boolean {
  const errors: Record<string, string> = {}
  const ns = form.value.namespace.trim()
  const name = form.value.name.trim()

  if (!ns) {
    errors.namespace = 'Vui lòng nhập Namespace'
  } else if (ns.length > 63 || !/^[a-z0-9]([-a-z0-9]*[a-z0-9])?$/.test(ns)) {
    errors.namespace = 'Namespace phải tuân theo chuẩn DNS-1123 label (1-63 ký tự)'
  }

  if (!name) {
    errors.name = 'Vui lòng nhập tên mục'
  } else if (name.length > 253) {
    errors.name = 'Tên tối đa 253 ký tự'
  }

  formErrors.value = errors
  return Object.keys(errors).length === 0
}

async function handleAdd() {
  if (!validateForm()) return

  actionLoading.value = true
  try {
    await store.createEntry({
      namespace: form.value.namespace.trim(),
      kind: form.value.kind,
      name: form.value.name.trim()
    })
    toast.success('Đã thêm mục vào allowlist thành công')
    form.value = { namespace: form.value.namespace.trim(), kind: 'configmap', name: '' }
    formErrors.value = {}
  } catch (err: any) {
    if (err instanceof ApiError && err.status === 409) {
      toast.error('Mục này đã tồn tại trong allowlist.')
    } else {
      toast.error(err.problem?.detail || err.message || 'Thêm mục vào allowlist thất bại')
    }
  } finally {
    actionLoading.value = false
  }
}

function openDeleteConfirm(entry: JobAllowEntry) {
  deletingEntry.value = entry
  isConfirmOpen.value = true
}

async function handleDelete() {
  if (!deletingEntry.value) return
  deleteLoading.value = true
  try {
    await store.deleteEntry(deletingEntry.value.id)
    toast.success(`Đã xoá mục "${deletingEntry.value.name}" khỏi allowlist`)
    isConfirmOpen.value = false
    deletingEntry.value = null
  } catch (err: any) {
    if (err instanceof ApiError && err.status === 409) {
      toast.error(err.problem?.detail || 'Không thể xoá mục allowlist đang được Job sử dụng')
    } else {
      toast.error(err.message || 'Xoá mục khỏi allowlist thất bại')
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
      <div>
        <h1 class="text-2xl font-bold tracking-tight text-fg flex items-center gap-2.5">
          <ListChecks class="w-6 h-6 text-primary" aria-hidden="true" />
          <span>Allowlist Job</span>
        </h1>
        <p class="text-sm text-muted-fg mt-1">
          Cấu hình các namespace, ConfigMap, Secret và ImagePullSecret được phép sử dụng trong Job.
          Hệ thống chỉ lưu và sử dụng tên tham chiếu, không bao giờ lưu hoặc hiển thị giá trị bí mật.
        </p>
      </div>

      <!-- Add New Entry Form Card -->
      <div class="p-5 rounded-[8px] border border-border bg-card shadow-sm space-y-4">
        <h2 class="text-sm font-semibold text-fg flex items-center gap-2">
          <Plus class="w-4 h-4 text-primary" aria-hidden="true" />
          <span>Thêm mục vào allowlist</span>
        </h2>

        <form class="grid grid-cols-1 md:grid-cols-4 gap-4 items-start" @submit.prevent="handleAdd">
          <!-- Namespace -->
          <AppField
            label="Namespace"
            :required="true"
            :error="formErrors.namespace"
          >
            <template #default="{ id, error, ariaDescribedby }">
              <AppInput
                :id="id"
                v-model="form.namespace"
                placeholder="VD: avina-workspace"
                :disabled="actionLoading"
                :error="error"
                :aria-describedby="ariaDescribedby"
              />
            </template>
          </AppField>

          <!-- Kind -->
          <AppField
            label="Loại"
            :required="true"
            :error="formErrors.kind"
          >
            <template #default="{ id, error, ariaDescribedby }">
              <AppSelect
                :id="id"
                v-model="form.kind"
                :options="kindOptions"
                :disabled="actionLoading"
                :error="error"
                :aria-describedby="ariaDescribedby"
              />
            </template>
          </AppField>

          <!-- Name -->
          <AppField
            label="Tên tài nguyên"
            :required="true"
            :error="formErrors.name"
          >
            <template #default="{ id, error, ariaDescribedby }">
              <AppInput
                :id="id"
                v-model="form.name"
                placeholder="VD: avina-files-config"
                :disabled="actionLoading"
                :error="error"
                :aria-describedby="ariaDescribedby"
              />
            </template>
          </AppField>

          <!-- Submit Button -->
          <div class="pt-6">
            <AppButton
              type="submit"
              variant="primary"
              class="w-full"
              :loading="actionLoading"
              :disabled="actionLoading"
              aria-label="Thêm vào allowlist"
            >
              <Plus class="w-4 h-4 mr-1.5" aria-hidden="true" />
              <span>Thêm mục</span>
            </AppButton>
          </div>
        </form>
      </div>

      <!-- Table of Allowlist Entries -->
      <div v-if="store.loading && store.entries.length === 0" class="py-16 text-center text-muted-fg">
        Đang tải allowlist...
      </div>

      <EmptyState
        v-else-if="store.entries.length === 0"
        title="Chưa có mục nào trong allowlist"
        description="Thêm namespace, ConfigMap hoặc Secret đầu tiên để cho phép Job tham chiếu và chạy."
      >
        <template #icon>
          <ShieldAlert class="w-10 h-10 text-muted-fg" aria-hidden="true" />
        </template>
      </EmptyState>

      <AppTable v-else>
        <template #header>
          <tr>
            <th scope="col" class="text-left py-3 px-4 text-xs font-semibold uppercase tracking-wider text-muted-fg">
              Namespace
            </th>
            <th scope="col" class="text-left py-3 px-4 text-xs font-semibold uppercase tracking-wider text-muted-fg">
              Loại
            </th>
            <th scope="col" class="text-left py-3 px-4 text-xs font-semibold uppercase tracking-wider text-muted-fg">
              Tên tài nguyên
            </th>
            <th scope="col" class="text-left py-3 px-4 text-xs font-semibold uppercase tracking-wider text-muted-fg">
              Ngày tạo
            </th>
            <th scope="col" class="text-right py-3 px-4 text-xs font-semibold uppercase tracking-wider text-muted-fg">
              Hành động
            </th>
          </tr>
        </template>

        <tr
          v-for="entry in store.entries"
          :key="entry.id"
          class="hover:bg-muted/40 transition-colors border-b border-border/60 last:border-b-0"
        >
          <!-- Namespace -->
          <td class="py-3 px-4 text-xs font-mono text-fg font-medium">
            {{ entry.namespace }}
          </td>

          <!-- Kind -->
          <td class="py-3 px-4">
            <span
              :class="[
                'inline-flex items-center px-2 py-0.5 rounded text-xs font-medium border',
                kindBadgeClass(entry.kind)
              ]"
            >
              {{ kindLabel(entry.kind) }}
            </span>
          </td>

          <!-- Name -->
          <td class="py-3 px-4 text-xs font-mono text-fg">
            {{ entry.name }}
          </td>

          <!-- Created At -->
          <td class="py-3 px-4 text-xs text-muted-fg tabular-nums">
            {{ formatDate(entry.createdAt) }}
          </td>

          <!-- Actions -->
          <td class="py-3 px-4 text-right">
            <AppButton
              variant="ghost"
              size="sm"
              aria-label="Xoá mục khỏi allowlist"
              title="Xoá mục"
              class="hover:text-danger"
              :disabled="deleteLoading"
              @click="openDeleteConfirm(entry)"
            >
              <Trash2 class="w-3.5 h-3.5 text-danger" aria-hidden="true" />
              <span class="hidden sm:inline">Xoá</span>
            </AppButton>
          </td>
        </tr>
      </AppTable>

      <!-- Delete Confirmation Dialog -->
      <ConfirmDialog
        v-model="isConfirmOpen"
        title="Xoá mục khỏi allowlist"
        :message="`Bạn có chắc chắn muốn xoá mục &quot;${deletingEntry?.kind}:${deletingEntry?.name}&quot; khỏi allowlist không?`"
        confirm-text="Xoá mục"
        cancel-text="Huỷ"
        :loading="deleteLoading"
        :danger="true"
        @confirm="handleDelete"
      />
    </div>
  </AppLayout>
</template>
