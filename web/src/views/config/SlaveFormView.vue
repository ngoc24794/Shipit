<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  Server,
  ArrowLeft,
  Save,
  Activity,
  AlertTriangle,
  CheckCircle2,
  XCircle,
  FileQuestion
} from 'lucide-vue-next'
import AppLayout from '../../layouts/AppLayout.vue'
import AppButton from '../../components/AppButton.vue'
import AppInput from '../../components/AppInput.vue'
import AppSelect from '../../components/AppSelect.vue'
import AppField from '../../components/AppField.vue'
import { useConfigSlavesStore } from '../../stores/configSlaves'
import { useConfigCredentialsStore } from '../../stores/configCredentials'
import { useToast } from '../../composables/useToast'
import { useUnsavedChangesGuard } from '../../composables/useUnsavedChangesGuard'
import { ApiError } from '../../api/client'

const route = useRoute()
const router = useRouter()
const store = useConfigSlavesStore()
const credStore = useConfigCredentialsStore()
const toast = useToast()

const slaveId = computed(() => {
  const p = route.params.id
  if (!p) return null
  const n = Number(p)
  return Number.isInteger(n) && n > 0 ? n : NaN
})
const isEditing = computed(() => route.params.id !== undefined)

const pageLoading = ref(false)
const notFound = ref(false)
const actionLoading = ref(false)
const initialSnapshot = ref('')
const allowLeave = ref(false)

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

const sshCredentials = computed(() => {
  return credStore.credentials.filter((c) => c.kind === 'ssh_key')
})

const sshCredentialOptions = computed(() => {
  return sshCredentials.value.map((c) => ({
    label: c.name,
    value: c.id
  }))
})

const testResult = computed(() => {
  if (!slaveId.value || Number.isNaN(slaveId.value)) return null
  return store.testResults[slaveId.value] || null
})

function takeSnapshot() {
  initialSnapshot.value = JSON.stringify(form.value)
}

const isDirty = computed(() => {
  if (allowLeave.value || pageLoading.value || notFound.value) return false
  return JSON.stringify(form.value) !== initialSnapshot.value
})

useUnsavedChangesGuard(isDirty)

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

    if (isEditing.value && slaveId.value && !Number.isNaN(slaveId.value)) {
      await store.updateSlave(slaveId.value, payload)
      toast.success('Cập nhật máy build thành công')
    } else {
      await store.createSlave(payload)
      toast.success('Thêm máy build thành công')
    }
    allowLeave.value = true
    await router.push('/slaves')
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

async function handleTestConnection() {
  if (!slaveId.value || Number.isNaN(slaveId.value)) return
  try {
    const result = await store.testSlave(slaveId.value)
    if (result.ok) {
      toast.success(result.message || 'Kết nối SSH thành công')
    } else {
      toast.error(result.message || 'Kết nối SSH thất bại')
    }
  } catch (err: any) {
    toast.error(err.message || 'Kiểm tra kết nối thất bại')
  }
}

onMounted(async () => {
  pageLoading.value = true
  try {
    await Promise.all([
      credStore.fetchCredentials().catch(() => {}),
      isEditing.value ? store.fetchSlaves() : Promise.resolve()
    ])
    if (isEditing.value) {
      if (slaveId.value === null || Number.isNaN(slaveId.value)) {
        notFound.value = true
        return
      }
      const slave = store.slaves.find((s) => s.id === slaveId.value)
      if (!slave) {
        notFound.value = true
        return
      }
      form.value = {
        name: slave.name,
        host: slave.host,
        port: slave.port ?? 22,
        user: slave.user,
        sshCredentialId: slave.sshCredentialId
      }
    } else {
      form.value.sshCredentialId = sshCredentials.value[0]?.id ?? ''
    }
  } catch {
    if (isEditing.value) notFound.value = true
  } finally {
    pageLoading.value = false
    takeSnapshot()
  }
})
</script>

<template>
  <AppLayout>
    <div class="flex-1 w-full flex flex-col min-h-0 gap-4">
      <!-- Top Header Bar -->
      <div class="shrink-0 flex flex-row items-center justify-between gap-4 flex-nowrap">
        <div class="flex items-center gap-3 min-w-0">
          <button
            type="button"
            aria-label="Quay lại danh sách máy build"
            class="p-2 rounded-[6px] text-muted-fg hover:text-fg hover:bg-muted transition-colors cursor-pointer focus-visible:outline-2 focus-visible:outline-ring shrink-0"
            @click="router.push('/slaves')"
          >
            <ArrowLeft class="w-5 h-5" aria-hidden="true" />
          </button>
          <div class="min-w-0">
            <nav aria-label="Breadcrumb" class="text-xs text-muted-fg flex items-center gap-1.5 whitespace-nowrap">
              <router-link to="/slaves" class="hover:text-fg hover:underline">
                Máy build
              </router-link>
              <span aria-hidden="true">/</span>
              <span class="text-fg font-medium truncate">
                {{ isEditing ? (form.name || 'Sửa') : 'Thêm mới' }}
              </span>
            </nav>
            <h1 class="text-xl font-bold tracking-tight text-fg flex items-center gap-2 mt-0.5 truncate">
              <Server class="w-5 h-5 text-primary shrink-0" aria-hidden="true" />
              <span class="truncate">{{ isEditing ? 'Sửa máy build' : 'Thêm máy build' }}</span>
            </h1>
          </div>
        </div>

        <div v-if="!notFound" class="flex items-center gap-2.5 shrink-0 whitespace-nowrap">
          <AppButton
            v-if="isEditing && slaveId && !Number.isNaN(slaveId)"
            type="button"
            variant="secondary"
            :loading="store.testingSlaveId === slaveId"
            :disabled="pageLoading || actionLoading"
            @click="handleTestConnection"
          >
            <Activity class="w-4 h-4" aria-hidden="true" />
            <span>Kiểm tra kết nối</span>
          </AppButton>
          <AppButton
            type="button"
            variant="secondary"
            :disabled="actionLoading"
            @click="router.push('/slaves')"
          >
            Huỷ
          </AppButton>
          <AppButton
            type="button"
            :loading="actionLoading"
            :disabled="pageLoading || sshCredentials.length === 0"
            @click="handleSave"
          >
            <Save class="w-4 h-4" aria-hidden="true" />
            <span>{{ isEditing ? 'Lưu thay đổi' : 'Thêm máy build' }}</span>
          </AppButton>
        </div>
      </div>

      <!-- Loading -->
      <div v-if="pageLoading" class="flex-1 min-h-0 p-6 rounded-[8px] border border-border bg-card space-y-4 overflow-y-auto" aria-busy="true">
        <div v-for="i in 4" :key="i" class="h-10 rounded bg-muted/70 animate-pulse" />
      </div>

      <!-- Not Found -->
      <div
        v-else-if="notFound"
        class="flex-1 min-h-0 p-8 rounded-[8px] border border-border bg-card flex items-center justify-center"
      >
        <div class="text-center space-y-4">
          <div class="w-12 h-12 rounded-full bg-muted flex items-center justify-center text-muted-fg mx-auto">
            <FileQuestion class="w-6 h-6" aria-hidden="true" />
          </div>
          <div>
            <h2 class="text-base font-semibold text-fg">Không tìm thấy máy build</h2>
            <p class="text-sm text-muted-fg mt-1">Máy build bạn tìm kiếm không tồn tại hoặc đã bị xoá.</p>
          </div>
          <AppButton variant="secondary" @click="router.push('/slaves')">
            <ArrowLeft class="w-4 h-4" aria-hidden="true" />
            <span>Quay lại danh sách</span>
          </AppButton>
        </div>
      </div>

      <!-- Form -->
      <form
        v-else
        class="flex-1 min-h-0 rounded-[8px] border border-border bg-card flex flex-col overflow-hidden"
        @submit.prevent="handleSave"
      >
        <div class="flex-1 min-h-0 overflow-y-auto p-6 space-y-5">
          <!-- Test connection result banner when editing -->
          <div
            v-if="testResult"
            role="status"
            :class="[
              'flex items-center gap-2.5 p-3 rounded-[6px] border text-xs',
              testResult.ok
                ? 'bg-success/10 border-success/30 text-success'
                : 'bg-danger/10 border-danger/30 text-danger'
            ]"
          >
            <CheckCircle2 v-if="testResult.ok" class="w-4 h-4 shrink-0" aria-hidden="true" />
            <XCircle v-else class="w-4 h-4 shrink-0" aria-hidden="true" />
            <span>{{ testResult.message }}</span>
          </div>

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
                to="/credentials/new"
                class="underline font-semibold hover:text-fg focus-visible:outline-2 focus-visible:outline-ring"
              >
                tạo thông tin xác thực
              </router-link>
              <span> trước khi cấu hình máy build.</span>
            </div>
          </div>

          <div class="grid grid-cols-12 gap-5">
            <!-- Name -->
            <div class="col-span-4">
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
            </div>

            <!-- Host -->
            <div class="col-span-5">
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

            <!-- Port -->
            <div class="col-span-3">
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

            <!-- User -->
            <div class="col-span-6">
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
            </div>

            <!-- SSH Credential Select -->
            <div class="col-span-6">
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
                    placeholder="Chọn khoá SSH"
                    :disabled="sshCredentials.length === 0"
                    :error="error"
                    :aria-describedby="ariaDescribedby"
                  />
                </template>
              </AppField>
            </div>
          </div>
        </div>
      </form>
    </div>
  </AppLayout>
</template>
