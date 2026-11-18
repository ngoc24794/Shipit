<script setup lang="ts">
import { ref, onMounted } from 'vue'
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
import { useConfigCredentialsStore } from '../../stores/configCredentials'
import { useToast } from '../../composables/useToast'
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
    <div class="flex-1 min-h-0 flex flex-col gap-5">
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
        class="flex items-center gap-3 p-4 rounded-[8px] bg-danger/10 border border-danger/20 text-danger text-sm"
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

      <!-- Table -->
      <AppTable v-else>
        <template #header>
          <tr>
            <th scope="col" class="py-3">Tên</th>
            <th scope="col" class="py-3">Loại</th>
            <th scope="col" class="py-3">Tên đăng nhập</th>
            <th scope="col" class="py-3">Giá trị bí mật</th>
            <th scope="col" class="py-3">Cập nhật lúc</th>
            <th scope="col" class="py-3 text-right">Thao tác</th>
          </tr>
        </template>

        <tr v-for="cred in store.credentials" :key="cred.id">
          <td class="font-medium text-fg">
            <div class="flex items-center gap-2">
              <KeyRound class="w-4 h-4 text-muted-fg flex-shrink-0" aria-hidden="true" />
              <span>{{ cred.name }}</span>
            </div>
          </td>
          <td>
            <span class="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium bg-muted text-muted-fg border border-border">
              {{ formatKind(cred.kind) }}
            </span>
          </td>
          <td class="font-mono text-xs text-muted-fg">
            {{ cred.username || '—' }}
          </td>
          <td>
            <span class="inline-flex items-center gap-1.5 text-xs text-success font-medium">
              <CheckCircle2 class="w-3.5 h-3.5" aria-hidden="true" />
              <span>Đã lưu giá trị</span>
            </span>
          </td>
          <td class="text-xs text-muted-fg">
            {{ formatDate(cred.updatedAt) }}
          </td>
          <td class="text-right">
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
