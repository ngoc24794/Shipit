<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import {
  Server,
  Plus,
  Pencil,
  Trash2,
  Activity,
  CheckCircle2,
  XCircle,
  AlertCircle,
  AlertTriangle
} from 'lucide-vue-next'
import AppLayout from '../../layouts/AppLayout.vue'
import AppTable from '../../components/AppTable.vue'
import AppButton from '../../components/AppButton.vue'
import AppInput from '../../components/AppInput.vue'
import AppSelect from '../../components/AppSelect.vue'
import AppField from '../../components/AppField.vue'
import AppDialog from '../../components/AppDialog.vue'
import ConfirmDialog from '../../components/ConfirmDialog.vue'
import EmptyState from '../../components/EmptyState.vue'
import { useConfigSlavesStore } from '../../stores/configSlaves'
import { useConfigCredentialsStore } from '../../stores/configCredentials'
import { useToast } from '../../composables/useToast'
import type { Slave } from '../../api/types'
import { ApiError } from '../../api/client'

const store = useConfigSlavesStore()
const credStore = useConfigCredentialsStore()
const toast = useToast()

const isDialogOpen = ref(false)
const isConfirmOpen = ref(false)
const editingSlave = ref<Slave | null>(null)
const deletingSlave = ref<Slave | null>(null)
const actionLoading = ref(false)
const deleteLoading = ref(false)

const form = ref<{
  name: string
  host: string
  port: number
  user: string
  sshCredentialId: string | number
}>({
  name: '',
  host: '',
  port: 22,
  user: '',
  sshCredentialId: ''
})

const formErrors = ref<Record<string, string>>({})

const isEditing = computed(() => editingSlave.value !== null)

const sshCredentials = computed(() => {
  return credStore.credentials.filter((c) => c.kind === 'ssh_key')
})

const sshCredentialOptions = computed(() => {
  return sshCredentials.value.map((c) => ({
    label: c.name,
    value: c.id
  }))
})

function getCredentialName(id: number): string {
  const cred = credStore.credentials.find((c) => c.id === id)
  return cred ? cred.name : `#${id}`
}

function resetForm() {
  form.value = {
    name: '',
    host: '',
    port: 22,
    user: '',
    sshCredentialId: sshCredentials.value[0]?.id ?? ''
  }
  formErrors.value = {}
  editingSlave.value = null
}

function openAddDialog() {
  resetForm()
  isDialogOpen.value = true
}

function openEditDialog(slave: Slave) {
  resetForm()
  editingSlave.value = slave
  form.value = {
    name: slave.name,
    host: slave.host,
    port: slave.port ?? 22,
    user: slave.user,
    sshCredentialId: slave.sshCredentialId
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
    errors.name = 'Vui lòng nhập tên máy build'
  }

  if (!form.value.host.trim()) {
    errors.host = 'Vui lòng nhập địa chỉ máy chủ (host)'
  }

  const portNum = Number(form.value.port)
  if (!portNum || isNaN(portNum) || portNum < 1 || portNum > 65535) {
    errors.port = 'Cổng kết nối phải từ 1 đến 65535'
  }

  if (!form.value.user.trim()) {
    errors.user = 'Vui lòng nhập tài khoản SSH (user)'
  }

  if (!form.value.sshCredentialId) {
    errors.sshCredentialId = 'Vui lòng chọn thông tin xác thực SSH'
  }

  formErrors.value = errors
  return Object.keys(errors).length === 0
}

async function handleSave() {
  if (!validateForm()) return

  actionLoading.value = true
  try {
    const payload = {
      name: form.value.name.trim(),
      host: form.value.host.trim(),
      port: Number(form.value.port) || 22,
      user: form.value.user.trim(),
      sshCredentialId: Number(form.value.sshCredentialId)
    }

    if (isEditing.value && editingSlave.value) {
      await store.updateSlave(editingSlave.value.id, payload)
      toast.success('Cập nhật máy build thành công')
    } else {
      await store.createSlave(payload)
      toast.success('Thêm máy build thành công')
    }
    closeDialog()
  } catch (err: any) {
    if (err instanceof ApiError && err.problem?.errors) {
      for (const [key, msgs] of Object.entries(err.problem.errors)) {
        const lowerKey = key.toLowerCase()
        if (lowerKey.includes('name')) formErrors.value.name = msgs[0]
        else if (lowerKey.includes('host')) formErrors.value.host = msgs[0]
        else if (lowerKey.includes('port')) formErrors.value.port = msgs[0]
        else if (lowerKey.includes('user')) formErrors.value.user = msgs[0]
        else if (lowerKey.includes('credential')) formErrors.value.sshCredentialId = msgs[0]
      }
    }
    toast.error(err.message || 'Lưu máy build thất bại')
  } finally {
    actionLoading.value = false
  }
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
      <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <h2 class="text-xl font-semibold text-fg">Máy build (Slave)</h2>
          <p class="text-sm text-muted-fg mt-1">
            Cấu hình các máy chủ thực hiện tác vụ build Docker image và kết nối triển khai.
          </p>
        </div>
        <AppButton @click="openAddDialog">
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
        description="Thêm máy build chạy SSH để thực thi các bước checkout, build image và push vào registry."
        :icon="Server"
      >
        <template #action>
          <AppButton @click="openAddDialog">
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
                class="w-4 h-4 text-primary flex-shrink-0"
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
                  store.testResults[slave.id].ok ? 'text-primary' : 'text-danger'
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
                :loading="store.testingSlaveId === slave.id"
                aria-label="Kiểm tra kết nối SSH"
                title="Kiểm tra kết nối SSH"
                @click="handleTestConnection(slave)"
              >
                <Activity class="w-3.5 h-3.5" aria-hidden="true" />
                <span class="hidden sm:inline">Kiểm tra kết nối</span>
              </AppButton>
              <AppButton
                variant="ghost"
                size="sm"
                aria-label="Sửa máy build"
                @click="openEditDialog(slave)"
              >
                <Pencil class="w-3.5 h-3.5" aria-hidden="true" />
                <span class="hidden sm:inline">Sửa</span>
              </AppButton>
              <AppButton
                variant="ghost"
                size="sm"
                class="text-danger hover:text-danger hover:bg-danger/10"
                aria-label="Xoá máy build"
                @click="promptDelete(slave)"
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
      :title="isEditing ? 'Sửa máy build' : 'Thêm máy build'"
      max-width="md"
      @close="closeDialog"
    >
      <form class="space-y-4" @submit.prevent="handleSave">
        <!-- Warning when no SSH key credentials exist -->
        <div
          v-if="sshCredentials.length === 0"
          role="alert"
          class="flex items-start gap-3 p-3 rounded-[6px] bg-warn/10 border border-warn/30 text-warn text-xs"
        >
          <AlertTriangle class="w-4 h-4 flex-shrink-0 mt-0.5" />
          <div>
            <span>Chưa có thông tin xác thực loại <strong>Khoá SSH</strong>. Vui lòng </span>
            <router-link
              to="/credentials"
              class="underline font-semibold hover:text-fg focus-visible:outline-2 focus-visible:outline-ring"
            >
              tạo thông tin xác thực
            </router-link>
            <span> trước khi cấu hình máy build.</span>
          </div>
        </div>

        <!-- Name -->
        <AppField
          label="Tên máy build"
          required
          :error="formErrors.name"
        >
          <template #default="{ id, error, ariaDescribedby }">
            <AppInput
              :id="id"
              v-model="form.name"
              placeholder="Ví dụ: slave-staging-01"
              :error="error"
              :aria-describedby="ariaDescribedby"
            />
          </template>
        </AppField>

        <!-- Host and Port grid -->
        <div class="grid grid-cols-1 sm:grid-cols-3 gap-3">
          <div class="sm:col-span-2">
            <AppField
              label="Địa chỉ máy chủ (Host / IP)"
              required
              :error="formErrors.host"
            >
              <template #default="{ id, error, ariaDescribedby }">
                <AppInput
                  :id="id"
                  v-model="form.host"
                  placeholder="Ví dụ: 192.168.1.49"
                  :error="error"
                  :aria-describedby="ariaDescribedby"
                />
              </template>
            </AppField>
          </div>
          <div>
            <AppField
              label="Cổng (Port)"
              required
              :error="formErrors.port"
            >
              <template #default="{ id, error, ariaDescribedby }">
                <AppInput
                  :id="id"
                  v-model="form.port"
                  type="number"
                  placeholder="22"
                  :error="error"
                  :aria-describedby="ariaDescribedby"
                />
              </template>
            </AppField>
          </div>
        </div>

        <!-- User -->
        <AppField
          label="Người dùng SSH (User)"
          required
          description="Tài khoản hệ điều hành trên máy build có quyền chạy Docker."
          :error="formErrors.user"
        >
          <template #default="{ id, error, ariaDescribedby }">
            <AppInput
              :id="id"
              v-model="form.user"
              placeholder="Ví dụ: devops"
              :error="error"
              :aria-describedby="ariaDescribedby"
            />
          </template>
        </AppField>

        <!-- SSH Credential Select -->
        <AppField
          label="Khoá xác thực SSH"
          required
          description="Chỉ chấp nhận các thông tin xác thực loại Khoá SSH."
          :error="formErrors.sshCredentialId"
        >
          <template #default="{ id, error, ariaDescribedby }">
            <AppSelect
              :id="id"
              v-model="form.sshCredentialId"
              :options="sshCredentialOptions"
              placeholder="-- Chọn khoá SSH --"
              :disabled="sshCredentials.length === 0"
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
        <AppButton
          :loading="actionLoading"
          :disabled="sshCredentials.length === 0"
          @click="handleSave"
        >
          {{ isEditing ? 'Lưu thay đổi' : 'Thêm máy build' }}
        </AppButton>
      </template>
    </AppDialog>

    <!-- Confirm Delete Dialog -->
    <ConfirmDialog
      v-model="isConfirmOpen"
      title="Xoá máy build"
      :message="`Bạn có chắc chắn muốn xoá máy build &quot;${deletingSlave?.name}&quot;? Hành động này không thể hoàn tác.`"
      confirm-text="Xoá máy build"
      cancel-text="Huỷ"
      :loading="deleteLoading"
      :danger="true"
      @confirm="handleDelete"
      @cancel="isConfirmOpen = false"
    />
  </AppLayout>
</template>
