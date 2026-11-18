<script setup lang="ts">
import { ref, onMounted } from 'vue'
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
import AppLayout from '../../layouts/AppLayout.vue'
import AppTable from '../../components/AppTable.vue'
import AppButton from '../../components/AppButton.vue'
import ConfirmDialog from '../../components/ConfirmDialog.vue'
import EmptyState from '../../components/EmptyState.vue'
import { useConfigSlavesStore } from '../../stores/configSlaves'
import { useConfigCredentialsStore } from '../../stores/configCredentials'
import { useToast } from '../../composables/useToast'
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

onMounted(() => {
  store.fetchSlaves().catch(() => {})
  credStore.fetchCredentials().catch(() => {})
})
</script>

<template>
  <AppLayout>
    <div class="space-y-6">
      <!-- Header -->
      <div class="flex flex-row items-center justify-between gap-4 flex-nowrap">
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
        class="flex items-center gap-3 p-4 rounded-[8px] bg-danger/10 border border-danger/20 text-danger text-sm"
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

      <!-- Table -->
      <AppTable v-else>
        <template #header>
          <tr>
            <th scope="col" class="py-3">Tên</th>
            <th scope="col" class="py-3">Máy chủ (Host:Port)</th>
            <th scope="col" class="py-3">Người dùng</th>
            <th scope="col" class="py-3">Khoá SSH</th>
            <th scope="col" class="py-3">Trạng thái kết nối</th>
            <th scope="col" class="py-3 text-right">Thao tác</th>
          </tr>
        </template>

        <tr v-for="slave in store.slaves" :key="slave.id">
          <td class="font-medium text-fg">
            <div class="flex items-center gap-2">
              <Server class="w-4 h-4 text-muted-fg flex-shrink-0" aria-hidden="true" />
              <span>{{ slave.name }}</span>
            </div>
          </td>
          <td class="font-mono text-xs text-fg">
            {{ slave.host }}:{{ slave.port ?? 22 }}
          </td>
          <td class="font-mono text-xs text-muted-fg">
            {{ slave.user }}
          </td>
          <td class="text-xs text-fg">
            <span class="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium bg-muted text-muted-fg border border-border">
              {{ getCredentialName(slave.sshCredentialId) }}
            </span>
          </td>
          <td class="text-xs">
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
          <td class="text-right">
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
  </AppLayout>
</template>
