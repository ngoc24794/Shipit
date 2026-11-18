<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
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
import DataToolbar, { type DataToolbarFilter } from '../../components/DataToolbar.vue'
import DataPagination from '../../components/DataPagination.vue'
import SortableHeader from '../../components/SortableHeader.vue'
import { useJobsAllowStore } from '../../stores/jobsAllow'
import { useToast } from '../../composables/useToast'
import { useDataTable } from '../../composables/useDataTable'
import type { JobAllowEntry, JobAllowKind } from '../../api/types'
import { ApiError } from '../../api/client'

const props = withDefaults(defineProps<{ embedded?: boolean }>(), {
  embedded: false
})

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

const table = useDataTable<JobAllowEntry>({
  mode: 'client',
  items: computed(() => store.entries),
  defaultFilters: {
    kind: '',
    namespace: ''
  },
  searchFields: ['namespace', 'name', (e) => kindLabel(e.kind)],
  filterFns: {
    kind: (e, val) => e.kind === val,
    namespace: (e, val) => e.namespace === val
  },
  sortFns: {
    namespace: (a, b) => a.namespace.localeCompare(b.namespace, 'vi'),
    kind: (a, b) => kindLabel(a.kind).localeCompare(kindLabel(b.kind), 'vi'),
    name: (a, b) => a.name.localeCompare(b.name, 'vi'),
    createdAt: (a, b) =>
      new Date(a.createdAt || 0).getTime() - new Date(b.createdAt || 0).getTime()
  }
})

const toolbarFilters = computed<DataToolbarFilter[]>(() => {
  const namespaces = Array.from(
    new Set(store.entries.map((e) => e.namespace).filter(Boolean))
  ).sort((a, b) => a.localeCompare(b))

  return [
    {
      key: 'kind',
      label: 'Loại tài nguyên',
      type: 'select',
      options: [
        { label: 'Tất cả loại', value: '' },
        { label: 'Namespace', value: 'namespace' },
        { label: 'ConfigMap', value: 'configmap' },
        { label: 'Secret', value: 'secret' },
        { label: 'pullSecret', value: 'pullsecret' }
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
  <component :is="props.embedded ? 'div' : AppLayout" :class="props.embedded ? 'flex-1 min-h-0 flex flex-col' : undefined">
    <div class="flex-1 min-h-0 flex flex-col gap-4">
      <!-- Page Header -->
      <div v-if="!props.embedded" class="shrink-0">
        <h1 class="text-2xl font-bold tracking-tight text-fg flex items-center gap-2.5">
          <ListChecks class="w-6 h-6 text-primary" aria-hidden="true" />
          <span>Allowlist Job</span>
        </h1>
        <p class="text-sm text-muted-fg mt-1">
          Danh sách namespace, ConfigMap, Secret và ImagePullSecret mà Job được phép dùng. Shipit chỉ lưu tên tham chiếu, không lưu giá trị bí mật.
        </p>
      </div>

      <!-- Add New Entry Form Card -->
      <div class="shrink-0 p-5 rounded-[8px] border border-border bg-card shadow-sm space-y-4">
        <h2 class="text-sm font-semibold text-fg flex items-center gap-2">
          <Plus class="w-4 h-4 text-primary" aria-hidden="true" />
          <span>Thêm mục vào allowlist</span>
        </h2>

        <form class="grid grid-cols-4 gap-4 items-start" @submit.prevent="handleAdd">
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
        description="Thêm namespace, ConfigMap hoặc Secret để Job có thể tham chiếu."
      >
        <template #icon>
          <ShieldAlert class="w-10 h-10 text-muted-fg" aria-hidden="true" />
        </template>
      </EmptyState>

      <div
        v-else
        class="flex-1 min-h-0 flex flex-col rounded-[8px] border border-border bg-card overflow-hidden"
      >
        <DataToolbar
          :q="table.q.value"
          search-placeholder="Tìm namespace, tên tài nguyên..."
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
                label="Namespace"
                sort-key="namespace"
                :sort-by="table.sortBy.value"
                :sort-dir="table.sortDir.value"
                class="py-3 px-4"
                @sort="table.toggleSort"
              />
              <SortableHeader
                label="Loại"
                sort-key="kind"
                :sort-by="table.sortBy.value"
                :sort-dir="table.sortDir.value"
                class="py-3 px-4"
                @sort="table.toggleSort"
              />
              <SortableHeader
                label="Tên tài nguyên"
                sort-key="name"
                :sort-by="table.sortBy.value"
                :sort-dir="table.sortDir.value"
                class="py-3 px-4"
                @sort="table.toggleSort"
              />
              <SortableHeader
                label="Ngày tạo"
                sort-key="createdAt"
                :sort-by="table.sortBy.value"
                :sort-dir="table.sortDir.value"
                class="py-3 px-4"
                @sort="table.toggleSort"
              />
              <th scope="col" class="text-right py-3 px-4 text-xs font-semibold text-muted-fg whitespace-nowrap">
                Thao tác
              </th>
            </tr>
          </template>

          <tr
            v-for="entry in table.items.value"
            :key="entry.id"
            class="hover:bg-muted/40 transition-colors border-b border-border/60 last:border-b-0"
          >
            <!-- Namespace -->
            <td class="py-3 px-4 text-xs font-mono text-fg font-medium whitespace-nowrap">
              {{ entry.namespace }}
            </td>

            <!-- Kind -->
            <td class="py-3 px-4 whitespace-nowrap">
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
            <td class="py-3 px-4 text-xs font-mono text-fg whitespace-nowrap">
              {{ entry.name }}
            </td>

            <!-- Created At -->
            <td class="py-3 px-4 text-xs text-muted-fg tabular-nums whitespace-nowrap">
              {{ formatDate(entry.createdAt) }}
            </td>

            <!-- Actions -->
            <td class="py-3 px-4 text-right whitespace-nowrap">
              <AppButton
                variant="ghost"
                size="sm"
                aria-label="Xoá mục khỏi allowlist"
                title="Xoá mục"
                class="text-muted-fg hover:text-danger hover:bg-danger/10 focus-visible:text-danger"
                :disabled="deleteLoading"
                @click="openDeleteConfirm(entry)"
              >
                <Trash2 class="w-3.5 h-3.5" aria-hidden="true" />
                <span class="whitespace-nowrap">Xoá</span>
              </AppButton>
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

      <!-- Delete Confirmation Dialog -->
      <ConfirmDialog
        v-model="isConfirmOpen"
        title="Xoá mục khỏi allowlist"
        :message="`Xoá mục &quot;${deletingEntry?.kind}:${deletingEntry?.name}&quot; khỏi allowlist?`"
        confirm-text="Xoá mục"
        cancel-text="Huỷ"
        :loading="deleteLoading"
        :danger="true"
        @confirm="handleDelete"
      />
    </div>
  </component>
</template>
