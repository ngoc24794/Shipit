<script setup lang="ts">
import { ref, computed, onMounted, onActivated } from 'vue'
import { useRouter } from 'vue-router'
import {
  Server,
  Plus,
  Pencil,
  Trash2,
  Activity,
  CheckCircle2,
  XCircle,
  AlertCircle
} from 'lucide-vue-next'
import AppTable from '../../components/AppTable.vue'

defineOptions({ name: 'SlavesView' })
import AppButton from '../../components/AppButton.vue'
import ConfirmDialog from '../../components/ConfirmDialog.vue'
import EmptyState from '../../components/EmptyState.vue'
import DataToolbar from '../../components/DataToolbar.vue'
import DataPagination from '../../components/DataPagination.vue'
import SortableHeader from '../../components/SortableHeader.vue'
import { useConfigSlavesStore } from '../../stores/configSlaves'
import { useConfigCredentialsStore } from '../../stores/configCredentials'
import { useToast } from '../../composables/useToast'
import { useDataTable } from '../../composables/useDataTable'
import type { Slave } from '../../api/types'
import { ApiError } from '../../api/client'

const router = useRouter()
const store = useConfigSlavesStore()
const credStore = useConfigCredentialsStore()
const toast = useToast()

const isConfirmOpen = ref(false)
const deletingSlave = ref<Slave | null>(null)
const deleteLoading = ref(false)

function getCredentialName(id: number): string {
  const cred = credStore.credentials.find((c) => c.id === id)
  return cred ? cred.name : `#${id}`
}

const table = useDataTable<Slave>({
  mode: 'client',
  items: computed(() => store.slaves),
  searchFields: [
    'name',
    'host',
    'user',
    (s) => `${s.host}:${s.port ?? 22}`,
    (s) => getCredentialName(s.sshCredentialId)
  ],
  sortFns: {
    name: (a, b) => a.name.localeCompare(b.name, 'vi'),
    host: (a, b) => `${a.host}:${a.port ?? 22}`.localeCompare(`${b.host}:${b.port ?? 22}`),
    user: (a, b) => a.user.localeCompare(b.user, 'vi'),
    sshCredentialId: (a, b) =>
      getCredentialName(a.sshCredentialId).localeCompare(getCredentialName(b.sshCredentialId), 'vi')
  }
})

function navigateToNew() {
  router.push('/slaves/new')
}

function navigateToEdit(slave: Slave) {
  router.push(`/slaves/${slave.id}/edit`)
}

function promptDelete(slave: Slave) {
  deletingSlave.value = slave
  isConfirmOpen.value = true
}

async function handleDelete() {
  if (!deletingSlave.value) return

  deleteLoading.value = true
  try {
    await store.deleteSlave(deletingSlave.value.id)
    toast.success('Đã xoá máy build thành công')
    isConfirmOpen.value = false
    deletingSlave.value = null
  } catch (err: any) {
    if (err instanceof ApiError && err.status === 409) {
      toast.error('Không thể xoá máy build đang được pipeline sử dụng')
    } else {
      toast.error(err.message || 'Xoá máy build thất bại')
    }
  } finally {
    deleteLoading.value = false
  }
}

async function handleTestConnection(slave: Slave) {
  try {
    const result = await store.testSlave(slave.id)
    if (result.ok) {
      toast.success(result.message || 'Kết nối SSH thành công')
    } else {
      toast.error(result.message || 'Kết nối SSH thất bại')
    }
  } catch (err: any) {
    toast.error(err.message || 'Kiểm tra kết nối thất bại')
  }
}

let hasMountedSlaves = false
onMounted(() => {
  hasMountedSlaves = true
  store.fetchSlaves().catch(() => {})
  credStore.fetchCredentials().catch(() => {})
})

onActivated(() => {
  if (!hasMountedSlaves) return
  store.fetchSlaves({ silent: true }).catch(() => {})
  credStore.fetchCredentials().catch(() => {})
})
</script>

<template>
  <div class="flex-1 min-h-0 flex flex-col">
    <div class="flex-1 min-h-0 flex flex-col gap-4">
      <!-- Header -->
      <div class="shrink-0 flex flex-row items-center justify-between gap-4 flex-nowrap">
        <div class="min-w-0">
          <h1 class="text-2xl font-semibold text-fg tracking-tight truncate">Máy build</h1>
          <p class="text-sm text-muted-fg mt-1 truncate">
            Danh sách máy build kết nối qua SSH để build Docker image.
          </p>
        </div>
        <AppButton class="shrink-0 whitespace-nowrap" @click="navigateToNew">
          <Plus class="w-4 h-4" aria-hidden="true" />
          <span>Thêm máy build</span>
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
        v-if="store.loading && store.slaves.length === 0"
        class="flex items-center justify-center p-12 text-muted-fg text-sm"
      >
        <div class="flex items-center gap-2">
          <div class="w-4 h-4 border-2 border-primary border-t-transparent rounded-full animate-spin" />
          <span>Đang tải danh sách máy build...</span>
        </div>
      </div>

      <!-- Empty state -->
      <EmptyState
        v-else-if="!store.loading && store.slaves.length === 0"
        title="Chưa có máy build"
        description="Thêm máy build SSH để checkout mã nguồn, build image và push lên registry."
        :icon="Server"
      >
        <template #action>
          <AppButton @click="navigateToNew">
            <Plus class="w-4 h-4" aria-hidden="true" />
            <span>Thêm máy build</span>
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
          search-placeholder="Tìm tên, host, user..."
          :start-item="table.startItem.value"
          :end-item="table.endItem.value"
          :total="table.total.value"
          :has-active-filters="table.hasActiveFilters.value"
          @update:q="(v) => (table.q.value = v)"
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
                label="Máy chủ (Host:Port)"
                sort-key="host"
                :sort-by="table.sortBy.value"
                :sort-dir="table.sortDir.value"
                @sort="table.toggleSort"
              />
              <SortableHeader
                label="Người dùng"
                sort-key="user"
                :sort-by="table.sortBy.value"
                :sort-dir="table.sortDir.value"
                class="w-[115px]"
                @sort="table.toggleSort"
              />
              <SortableHeader
                label="Khoá SSH"
                sort-key="sshCredentialId"
                :sort-by="table.sortBy.value"
                :sort-dir="table.sortDir.value"
                class="w-[140px]"
                @sort="table.toggleSort"
              />
              <th scope="col" class="py-3 w-[165px] whitespace-nowrap">Trạng thái kết nối</th>
              <th scope="col" class="py-3 w-[300px] text-right whitespace-nowrap">Thao tác</th>
            </tr>
          </template>

          <tr v-for="slave in table.items.value" :key="slave.id">
            <td class="font-medium text-fg whitespace-nowrap">
              <div class="flex items-center gap-2">
                <Server class="w-4 h-4 text-muted-fg flex-shrink-0" aria-hidden="true" />
                <span>{{ slave.name }}</span>
              </div>
            </td>
            <td class="font-mono text-xs text-fg whitespace-nowrap">
              {{ slave.host }}:{{ slave.port ?? 22 }}
            </td>
            <td class="font-mono text-xs text-muted-fg whitespace-nowrap">
              {{ slave.user }}
            </td>
            <td class="text-xs text-fg whitespace-nowrap">
              <span class="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium bg-muted text-muted-fg border border-border">
                {{ getCredentialName(slave.sshCredentialId) }}
              </span>
            </td>
            <td class="text-xs whitespace-nowrap">
              <div v-if="store.testResults[slave.id]" class="flex items-center gap-1.5">
                <CheckCircle2
                  v-if="store.testResults[slave.id].ok"
                  class="w-4 h-4 text-success flex-shrink-0"
                  aria-hidden="true"
                />
                <XCircle
                  v-else
                  class="w-4 h-4 text-danger flex-shrink-0"
                  aria-hidden="true"
                />
                <span
                  :class="[
                    'truncate max-w-[200px]',
                    store.testResults[slave.id].ok ? 'text-success' : 'text-danger'
                  ]"
                  :title="store.testResults[slave.id].message"
                >
                  {{ store.testResults[slave.id].message }}
                </span>
              </div>
              <span v-else class="text-muted-fg">—</span>
            </td>
            <td class="text-right whitespace-nowrap">
              <div class="inline-flex items-center gap-1">
                <AppButton
                  variant="ghost"
                  size="sm"
                  class="whitespace-nowrap shrink-0"
                  :loading="store.testingSlaveId === slave.id"
                  aria-label="Kiểm tra kết nối SSH"
                  title="Kiểm tra kết nối SSH"
                  @click="handleTestConnection(slave)"
                >
                  <Activity class="w-3.5 h-3.5" aria-hidden="true" />
                  <span>Kiểm tra kết nối</span>
                </AppButton>
                <AppButton
                  variant="ghost"
                  size="sm"
                  class="whitespace-nowrap shrink-0"
                  aria-label="Sửa máy build"
                  @click="navigateToEdit(slave)"
                >
                  <Pencil class="w-3.5 h-3.5" aria-hidden="true" />
                  <span>Sửa</span>
                </AppButton>
                <AppButton
                  variant="ghost"
                  size="sm"
                  class="text-muted-fg hover:text-danger hover:bg-danger/10 focus-visible:text-danger whitespace-nowrap shrink-0"
                  aria-label="Xoá máy build"
                  @click="promptDelete(slave)"
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
      title="Xoá máy build"
      :message="`Xoá máy build &quot;${deletingSlave?.name}&quot;? Thao tác này không thể hoàn tác.`"
      confirm-text="Xoá máy build"
      cancel-text="Huỷ"
      :loading="deleteLoading"
      :danger="true"
      @confirm="handleDelete"
      @cancel="isConfirmOpen = false"
    />
  </div>
</template>
