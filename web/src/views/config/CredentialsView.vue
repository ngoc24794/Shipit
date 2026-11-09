<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import {
  KeyRound,
  Plus,
  Pencil,
  Trash2,
  CheckCircle2,
  Eye,
  EyeOff,
  AlertCircle
} from 'lucide-vue-next'
import AppLayout from '../../layouts/AppLayout.vue'
import AppTable from '../../components/AppTable.vue'
import AppButton from '../../components/AppButton.vue'
import AppInput from '../../components/AppInput.vue'
import AppSecretInput from '../../components/AppSecretInput.vue'
import AppSelect from '../../components/AppSelect.vue'
import AppField from '../../components/AppField.vue'
import AppDialog from '../../components/AppDialog.vue'
import ConfirmDialog from '../../components/ConfirmDialog.vue'
import EmptyState from '../../components/EmptyState.vue'
import { useConfigCredentialsStore } from '../../stores/configCredentials'
import { useToast } from '../../composables/useToast'
import type { Credential, CredentialKind } from '../../api/types'
import { ApiError } from '../../api/client'

const store = useConfigCredentialsStore()
const toast = useToast()

const isDialogOpen = ref(false)
const isConfirmOpen = ref(false)
const editingCredential = ref<Credential | null>(null)
const deletingCredential = ref<Credential | null>(null)
const actionLoading = ref(false)
const deleteLoading = ref(false)
const showPemSecret = ref(false)

const kindOptions = [
  { label: 'Khoá SSH', value: 'ssh_key' },
  { label: 'Token Git', value: 'git_token' },
  { label: 'Registry', value: 'registry' }
]

const form = ref<{
  name: string
  kind: CredentialKind
  username: string
  value: string
}>({
  name: '',
  kind: 'ssh_key',
  username: '',
  value: ''
})

const formErrors = ref<Record<string, string>>({})

const isEditing = computed(() => editingCredential.value !== null)

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

function resetForm() {
  form.value = {
    name: '',
    kind: 'ssh_key',
    username: '',
    value: ''
  }
  formErrors.value = {}
  editingCredential.value = null
  showPemSecret.value = false
}

function openAddDialog() {
  resetForm()
  isDialogOpen.value = true
}

function openEditDialog(cred: Credential) {
  resetForm()
  editingCredential.value = cred
  form.value = {
    name: cred.name,
    kind: cred.kind,
    username: cred.username || '',
    value: '' // Strictly never pre-fill secret
  }
  isDialogOpen.value = true
}

function closeDialog() {
  isDialogOpen.value = false
  resetForm()
}

function validateForm(): boolean {
  const errors: Record<string, string> = {}

  if (!form.value.name.trim()) {
    errors.name = 'Vui lòng nhập tên thông tin xác thực'
  }

  if (form.value.kind === 'registry' && !form.value.username.trim()) {
    errors.username = 'Tên đăng nhập bắt buộc khi loại là Registry'
  }

  if (!isEditing.value && !form.value.value.trim()) {
    errors.value = 'Vui lòng nhập giá trị bí mật'
  }

  formErrors.value = errors
  return Object.keys(errors).length === 0
}

async function handleSave() {
  if (!validateForm()) return

  actionLoading.value = true
  try {
    if (isEditing.value && editingCredential.value) {
      await store.updateCredential(editingCredential.value.id, {
        name: form.value.name.trim(),
        username: form.value.kind === 'registry' ? form.value.username.trim() : null,
        value: form.value.value.trim() ? form.value.value : undefined
      })
      toast.success('Cập nhật thông tin xác thực thành công')
    } else {
      await store.createCredential({
        name: form.value.name.trim(),
        kind: form.value.kind,
        username: form.value.kind === 'registry' ? form.value.username.trim() : null,
        value: form.value.value
      })
      toast.success('Thêm thông tin xác thực thành công')
    }
    closeDialog()
  } catch (err: any) {
    if (err instanceof ApiError && err.problem?.errors) {
      for (const [key, msgs] of Object.entries(err.problem.errors)) {
        const lowerKey = key.toLowerCase()
        if (lowerKey.includes('name')) formErrors.value.name = msgs[0]
        else if (lowerKey.includes('username')) formErrors.value.username = msgs[0]
        else if (lowerKey.includes('value')) formErrors.value.value = msgs[0]
      }
    }
    toast.error(err.message || 'Lưu thông tin xác thực thất bại')
  } finally {
    actionLoading.value = false
    // Always clear secret value from form state / DOM
    form.value.value = ''
  }
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
    <div class="space-y-6">
      <!-- Header -->
      <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <h2 class="text-xl font-semibold text-fg">Thông tin xác thực</h2>
          <p class="text-sm text-muted-fg mt-1">
            Quản lý khoá SSH, git token và thông tin đăng nhập registry phục vụ build và deploy.
          </p>
        </div>
        <AppButton @click="openAddDialog">
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
        description="Tạo thông tin xác thực như khoá SSH, token Git hoặc tài khoản Docker registry để bắt đầu kết nối máy build."
        :icon="KeyRound"
      >
        <template #action>
          <AppButton @click="openAddDialog">
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
            <span class="inline-flex items-center gap-1.5 text-xs text-primary font-medium">
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
                aria-label="Sửa thông tin xác thực"
                @click="openEditDialog(cred)"
              >
                <Pencil class="w-3.5 h-3.5" aria-hidden="true" />
                <span class="hidden sm:inline">Sửa</span>
              </AppButton>
              <AppButton
                variant="ghost"
                size="sm"
                class="text-danger hover:text-danger hover:bg-danger/10"
                aria-label="Xoá thông tin xác thực"
                @click="promptDelete(cred)"
              >
                <Trash2 class="w-3.5 h-3.5" aria-hidden="true" />
                <span class="hidden sm:inline">Xoá</span>
              </AppButton>
            </div>
          </td>
        </tr>
      </AppTable>
    </div>

    <!-- Create / Edit Dialog -->
    <AppDialog
      v-model="isDialogOpen"
      :title="isEditing ? 'Sửa thông tin xác thực' : 'Thêm thông tin xác thực'"
      max-width="md"
      @close="closeDialog"
    >
      <form class="space-y-4" @submit.prevent="handleSave">
        <!-- Name -->
        <AppField
          label="Tên thông tin xác thực"
          required
          :error="formErrors.name"
        >
          <template #default="{ id, error, ariaDescribedby }">
            <AppInput
              :id="id"
              v-model="form.name"
              placeholder="Ví dụ: gitlab-deploy-key"
              :error="error"
              :aria-describedby="ariaDescribedby"
            />
          </template>
        </AppField>

        <!-- Kind -->
        <AppField
          label="Loại thông tin xác thực"
          required
          :error="formErrors.kind"
        >
          <template #default="{ id, error, ariaDescribedby }">
            <AppSelect
              :id="id"
              v-model="form.kind"
              :options="kindOptions"
              :disabled="isEditing"
              :error="error"
              :aria-describedby="ariaDescribedby"
            />
          </template>
        </AppField>

        <!-- Username (Only for Registry) -->
        <AppField
          v-if="form.kind === 'registry'"
          label="Tên đăng nhập (Username)"
          required
          description="Tài khoản dùng để đăng nhập Docker registry."
          :error="formErrors.username"
        >
          <template #default="{ id, error, ariaDescribedby }">
            <AppInput
              :id="id"
              v-model="form.username"
              placeholder="Ví dụ: devops"
              :error="error"
              :aria-describedby="ariaDescribedby"
            />
          </template>
        </AppField>

        <!-- Secret Value -->
        <AppField
          label="Giá trị bí mật"
          :required="!isEditing"
          :description="
            isEditing
              ? 'Để trống nếu không muốn thay đổi giá trị bí mật đã lưu.'
              : form.kind === 'ssh_key'
                ? 'Dán nội dung khoá riêng PEM (OpenSSH hoặc RSA PEM).'
                : form.kind === 'git_token'
                  ? 'Personal Access Token Git có quyền đọc kho mã nguồn.'
                  : 'Mật khẩu hoặc Access Token Docker Registry.'
          "
          :error="formErrors.value"
        >
          <template #default="{ id, error, ariaDescribedby }">
            <!-- For SSH PEM: multiline textarea with masked/visible toggle -->
            <div v-if="form.kind === 'ssh_key'" class="relative w-full">
              <textarea
                :id="id"
                v-model="form.value"
                rows="4"
                autocomplete="new-password"
                :placeholder="isEditing ? 'Đã lưu — để trống để giữ nguyên' : '-----BEGIN OPENSSH PRIVATE KEY-----\n...'"
                :aria-invalid="error ? 'true' : undefined"
                :aria-describedby="ariaDescribedby"
                :class="[
                  'w-full p-3 pr-10 text-xs font-mono rounded-[6px] bg-card text-fg border transition-colors resize-y',
                  'placeholder:text-muted-fg/60 placeholder:font-sans placeholder:text-sm',
                  'focus-visible:outline-2 focus-visible:outline-ring focus-visible:outline-offset-2',
                  error ? 'border-danger focus-visible:outline-danger' : 'border-border focus-visible:border-primary',
                  !showPemSecret && form.value ? 'filter blur-xs select-none' : ''
                ]"
              />
              <button
                type="button"
                :aria-label="showPemSecret ? 'Ẩn giá trị bí mật' : 'Hiện giá trị bí mật'"
                class="absolute top-2 right-2 p-1.5 rounded-[4px] text-muted-fg hover:text-fg hover:bg-muted focus-visible:outline-2 focus-visible:outline-ring cursor-pointer"
                @click="showPemSecret = !showPemSecret"
              >
                <EyeOff v-if="showPemSecret" class="w-4 h-4" aria-hidden="true" />
                <Eye v-else class="w-4 h-4" aria-hidden="true" />
              </button>
            </div>

            <!-- For Git token & Registry: AppSecretInput -->
            <AppSecretInput
              v-else
              :id="id"
              v-model="form.value"
              :has-existing-value="isEditing"
              :error="error"
              :aria-describedby="ariaDescribedby"
            />
          </template>
        </AppField>
      </form>

      <template #footer>
        <AppButton variant="secondary" :disabled="actionLoading" @click="closeDialog">
          Huỷ
        </AppButton>
        <AppButton :loading="actionLoading" @click="handleSave">
          {{ isEditing ? 'Lưu thay đổi' : 'Thêm thông tin' }}
        </AppButton>
      </template>
    </AppDialog>

    <!-- Confirm Delete Dialog -->
    <ConfirmDialog
      v-model="isConfirmOpen"
      title="Xoá thông tin xác thực"
      :message="`Bạn có chắc chắn muốn xoá thông tin xác thực &quot;${deletingCredential?.name}&quot;? Hành động này không thể hoàn tác.`"
      confirm-text="Xoá thông tin xác thực"
      cancel-text="Huỷ"
      :loading="deleteLoading"
      :danger="true"
      @confirm="handleDelete"
      @cancel="isConfirmOpen = false"
    />
  </AppLayout>
</template>
