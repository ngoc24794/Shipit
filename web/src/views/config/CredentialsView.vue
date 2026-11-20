<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import {
  KeyRound,
  Plus,
  Pencil,
  Trash2,
  CheckCircle2,
  AlertCircle
} from 'lucide-vue-next'
import AppLayout from '../../layouts/AppLayout.vue'
import AppTable from '../../components/AppTable.vue'
import AppButton from '../../components/AppButton.vue'
import ConfirmDialog from '../../components/ConfirmDialog.vue'
import EmptyState from '../../components/EmptyState.vue'
import DataToolbar, { type DataToolbarFilter } from '../../components/DataToolbar.vue'
import DataPagination from '../../components/DataPagination.vue'
import SortableHeader from '../../components/SortableHeader.vue'
import { useConfigCredentialsStore } from '../../stores/configCredentials'
import { useToast } from '../../composables/useToast'
import { useDataTable } from '../../composables/useDataTable'
import type { Credential, CredentialKind } from '../../api/types'
import { ApiError } from '../../api/client'

const router = useRouter()
const store = useConfigCredentialsStore()
const toast = useToast()

const isConfirmOpen = ref(false)
const deletingCredential = ref<Credential | null>(null)
const deleteLoading = ref(false)

function formatKind(kind: CredentialKind): string {
  switch (kind) {
    case 'ssh_key':
      return 'Khoá SSH'
    case 'git_token':
      return 'Token Git'
    case 'registry':
      return 'Registry'
    default:
      return kind
  }
}

const table = useDataTable<Credential>({
  mode: 'client',
  items: computed(() => store.credentials),
  defaultFilters: {
    kind: ''
  },
  searchFields: ['name', 'username', (c) => formatKind(c.kind)],
  filterFns: {
    kind: (c, val) => c.kind === val
  },
  sortFns: {
    name: (a, b) => a.name.localeCompare(b.name, 'vi'),
    kind: (a, b) => formatKind(a.kind).localeCompare(formatKind(b.kind), 'vi'),
    username: (a, b) => (a.username ?? '').localeCompare(b.username ?? '', 'vi'),
    updatedAt: (a, b) =>
      new Date(a.updatedAt || 0).getTime() - new Date(b.updatedAt || 0).getTime()
  }
})

const toolbarFilters: DataToolbarFilter[] = [
  {
    key: 'kind',
    label: 'Loại thông tin xác thực',
    type: 'select',
    options: [
      { label: 'Tất cả loại', value: '' },
      { label: 'Khoá SSH', value: 'ssh_key' },
      { label: 'Token Git', value: 'git_token' },
      { label: 'Registry', value: 'registry' }
    ]
  }
]

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

function navigateToNew() {
  router.push('/credentials/new')
}

function navigateToEdit(cred: Credential) {
  router.push(`/credentials/${cred.id}/edit`)
}

function promptDelete(cred: Credential) {
  deletingCredential.value = cred
  isConfirmOpen.value = true
}

async function handleDelete() {
  if (!deletingCredential.value) return

  deleteLoading.value = true
  try {
    await store.deleteCredential(deletingCredential.value.id)
    toast.success('Đã xoá thông tin xác thực thành công')
    isConfirmOpen.value = false
    deletingCredential.value = null
  } catch (err: any) {
    if (err instanceof ApiError && err.status === 409) {
      toast.error('Thông tin xác thực đang được dùng bởi slave/pipeline')
    } else {
      toast.error(err.message || 'Xoá thông tin xác thực thất bại')
    }
  } finally {
    deleteLoading.value = false
  }
}

onMounted(() => {
  store.fetchCredentials().catch(() => {
    // handled in store
  })
})
</script>

<template>
  <AppLayout>
    <div class="flex-1 min-h-0 flex flex-col gap-4">
      <!-- Header -->
      <div class="shrink-0 flex flex-row items-center justify-between gap-4 flex-nowrap">
        <div class="min-w-0">
          <h1 class="text-2xl font-semibold text-fg tracking-tight truncate">Thông tin xác thực</h1>
          <p class="text-sm text-muted-fg mt-1 truncate">
            Khoá SSH, token Git và tài khoản registry dùng khi build và deploy.
          </p>
        </div>
        <AppButton class="shrink-0 whitespace-nowrap" @click="navigateToNew">
          <Plus class="w-4 h-4" aria-hidden="true" />
          <span>Thêm thông tin xác thực</span>
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
        v-if="store.loading && store.credentials.length === 0"
        class="flex items-center justify-center p-12 text-muted-fg text-sm"
      >
        <div class="flex items-center gap-2">
          <div class="w-4 h-4 border-2 border-primary border-t-transparent rounded-full animate-spin" />
          <span>Đang tải danh sách thông tin xác thực...</span>
        </div>
      </div>

      <!-- Empty state -->
      <EmptyState
        v-else-if="!store.loading && store.credentials.length === 0"
        title="Chưa có thông tin xác thực"
        description="Thêm khoá SSH, token Git hoặc tài khoản Docker registry."
        :icon="KeyRound"
      >
        <template #action>
          <AppButton @click="navigateToNew">
            <Plus class="w-4 h-4" aria-hidden="true" />
            <span>Thêm thông tin xác thực</span>
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
          search-placeholder="Tìm theo tên, tên đăng nhập..."
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
              <SortableHeader
                label="Loại"
                sort-key="kind"
                :sort-by="table.sortBy.value"
                :sort-dir="table.sortDir.value"
                class="w-[145px]"
                @sort="table.toggleSort"
              />
              <SortableHeader
                label="Tên đăng nhập"
                sort-key="username"
                :sort-by="table.sortBy.value"
                :sort-dir="table.sortDir.value"
                class="w-[155px]"
                @sort="table.toggleSort"
              />
              <th scope="col" class="py-3 w-[145px] whitespace-nowrap">Giá trị bí mật</th>
              <SortableHeader
                label="Cập nhật lúc"
                sort-key="updatedAt"
                :sort-by="table.sortBy.value"
                :sort-dir="table.sortDir.value"
                class="w-[155px]"
                @sort="table.toggleSort"
              />
              <th scope="col" class="py-3 w-[145px] text-right whitespace-nowrap">Thao tác</th>
            </tr>
          </template>

          <tr v-for="cred in table.items.value" :key="cred.id">
            <td class="font-medium text-fg whitespace-nowrap">
              <div class="flex items-center gap-2">
                <KeyRound class="w-4 h-4 text-muted-fg flex-shrink-0" aria-hidden="true" />
                <span>{{ cred.name }}</span>
              </div>
            </td>
            <td class="whitespace-nowrap">
              <span class="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium bg-muted text-muted-fg border border-border">
                {{ formatKind(cred.kind) }}
              </span>
            </td>
            <td class="font-mono text-xs text-muted-fg whitespace-nowrap">
              {{ cred.username || '—' }}
            </td>
            <td class="whitespace-nowrap">
              <span class="inline-flex items-center gap-1.5 text-xs text-success font-medium">
                <CheckCircle2 class="w-3.5 h-3.5" aria-hidden="true" />
                <span>Đã lưu giá trị</span>
              </span>
            </td>
            <td class="text-xs text-muted-fg whitespace-nowrap tabular-nums">
              {{ formatDate(cred.updatedAt) }}
            </td>
            <td class="text-right whitespace-nowrap">
              <div class="inline-flex items-center gap-1">
                <AppButton
                  variant="ghost"
                  size="sm"
                  class="whitespace-nowrap shrink-0"
                  aria-label="Sửa thông tin xác thực"
                  @click="navigateToEdit(cred)"
                >
                  <Pencil class="w-3.5 h-3.5" aria-hidden="true" />
                  <span>Sửa</span>
                </AppButton>
                <AppButton
                  variant="ghost"
                  size="sm"
                  class="text-muted-fg hover:text-danger hover:bg-danger/10 focus-visible:text-danger whitespace-nowrap shrink-0"
                  aria-label="Xoá thông tin xác thực"
                  @click="promptDelete(cred)"
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
      title="Xoá thông tin xác thực"
      :message="`Xoá thông tin xác thực &quot;${deletingCredential?.name}&quot;? Thao tác này không thể hoàn tác.`"
      confirm-text="Xoá thông tin xác thực"
      cancel-text="Huỷ"
      :loading="deleteLoading"
      :danger="true"
      @confirm="handleDelete"
      @cancel="isConfirmOpen = false"
    />
  </AppLayout>
</template>
