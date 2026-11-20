<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  KeyRound,
  ArrowLeft,
  Save,
  Eye,
  EyeOff,
  FileQuestion
} from 'lucide-vue-next'
import AppButton from '../../components/AppButton.vue'
import AppInput from '../../components/AppInput.vue'
import AppSecretInput from '../../components/AppSecretInput.vue'
import AppSelect from '../../components/AppSelect.vue'
import AppField from '../../components/AppField.vue'
import { useConfigCredentialsStore } from '../../stores/configCredentials'
import { useToast } from '../../composables/useToast'
import { useUnsavedChangesGuard } from '../../composables/useUnsavedChangesGuard'
import type { CredentialKind } from '../../api/types'
import { ApiError } from '../../api/client'

const route = useRoute()
const router = useRouter()
const store = useConfigCredentialsStore()
const toast = useToast()

const credentialId = computed(() => {
  const p = route.params.id
  if (!p) return null
  const n = Number(p)
  return Number.isInteger(n) && n > 0 ? n : NaN
})
const isEditing = computed(() => route.params.id !== undefined)

const pageLoading = ref(false)
const notFound = ref(false)
const actionLoading = ref(false)
const showPemSecret = ref(false)
const initialSnapshot = ref('')
const allowLeave = ref(false)

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
    if (isEditing.value && credentialId.value && !Number.isNaN(credentialId.value)) {
      await store.updateCredential(credentialId.value, {
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
    allowLeave.value = true
    form.value.value = ''
    await router.push('/credentials')
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
    form.value.value = ''
  }
}

onMounted(async () => {
  if (isEditing.value) {
    if (credentialId.value === null || Number.isNaN(credentialId.value)) {
      notFound.value = true
      return
    }
    pageLoading.value = true
    try {
      await store.fetchCredentials()
      const cred = store.credentials.find((c) => c.id === credentialId.value)
      if (!cred) {
        notFound.value = true
        return
      }
      form.value = {
        name: cred.name,
        kind: cred.kind,
        username: cred.username || '',
        value: '' // Strictly never pre-fill secret
      }
    } catch {
      notFound.value = true
    } finally {
      pageLoading.value = false
      takeSnapshot()
    }
  } else {
    takeSnapshot()
  }
})
</script>

<template>
  <div class="flex-1 w-full flex flex-col min-h-0 gap-4">
      <!-- Top Header Bar -->
      <div class="shrink-0 flex flex-row items-center justify-between gap-4 flex-nowrap">
        <div class="flex items-center gap-3 min-w-0">
          <button
            type="button"
            aria-label="Quay lại danh sách thông tin xác thực"
            class="p-2 rounded-[6px] text-muted-fg hover:text-fg hover:bg-muted transition-colors cursor-pointer focus-visible:outline-2 focus-visible:outline-ring shrink-0"
            @click="router.push('/credentials')"
          >
            <ArrowLeft class="w-5 h-5" aria-hidden="true" />
          </button>
          <div class="min-w-0">
            <nav aria-label="Breadcrumb" class="text-xs text-muted-fg flex items-center gap-1.5 whitespace-nowrap">
              <router-link to="/credentials" class="hover:text-fg hover:underline">
                Thông tin xác thực
              </router-link>
              <span aria-hidden="true">/</span>
              <span class="text-fg font-medium truncate">
                {{ isEditing ? (form.name || 'Sửa') : 'Thêm mới' }}
              </span>
            </nav>
            <h1 class="text-xl font-bold tracking-tight text-fg flex items-center gap-2 mt-0.5 truncate">
              <KeyRound class="w-5 h-5 text-primary shrink-0" aria-hidden="true" />
              <span class="truncate">{{ isEditing ? 'Sửa thông tin xác thực' : 'Thêm thông tin xác thực' }}</span>
            </h1>
          </div>
        </div>

        <div v-if="!notFound" class="flex items-center gap-2.5 shrink-0 whitespace-nowrap">
          <AppButton
            type="button"
            variant="secondary"
            :disabled="actionLoading"
            @click="router.push('/credentials')"
          >
            Huỷ
          </AppButton>
          <AppButton
            type="button"
            :loading="actionLoading"
            :disabled="pageLoading"
            @click="handleSave"
          >
            <Save class="w-4 h-4" aria-hidden="true" />
            <span>{{ isEditing ? 'Lưu thay đổi' : 'Thêm thông tin' }}</span>
          </AppButton>
        </div>
      </div>

      <!-- Loading -->
      <div v-if="pageLoading" class="flex-1 min-h-0 p-6 rounded-[8px] border border-border bg-card space-y-4 overflow-y-auto" aria-busy="true">
        <div v-for="i in 3" :key="i" class="h-10 rounded bg-muted/70 animate-pulse" />
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
            <h2 class="text-base font-semibold text-fg">Không tìm thấy thông tin xác thực</h2>
            <p class="text-sm text-muted-fg mt-1">Mục bạn tìm kiếm không tồn tại hoặc đã bị xoá.</p>
          </div>
          <AppButton variant="secondary" @click="router.push('/credentials')">
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
        <div class="flex-1 min-h-0 overflow-y-auto p-6 flex flex-col gap-5">
          <div class="shrink-0 grid grid-cols-12 gap-5">
            <!-- Name -->
            <div :class="form.kind === 'registry' ? 'col-span-4' : 'col-span-6'">
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
            </div>

            <!-- Kind -->
            <div :class="form.kind === 'registry' ? 'col-span-4' : 'col-span-6'">
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
            </div>

            <!-- Username (Only for Registry) -->
            <div v-if="form.kind === 'registry'" class="col-span-4">
              <AppField
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
            </div>
          </div>

          <!-- Secret Value -->
          <div :class="form.kind === 'ssh_key' ? 'flex-1 min-h-[220px] flex flex-col' : 'shrink-0 grid grid-cols-12 gap-5'">
            <div :class="form.kind === 'ssh_key' ? 'flex-1 min-h-0 flex flex-col' : 'col-span-6'">
              <AppField
                :class="form.kind === 'ssh_key' ? 'flex-1 min-h-0' : ''"
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
                  <div v-if="form.kind === 'ssh_key'" class="relative w-full flex-1 min-h-[160px] flex flex-col">
                    <textarea
                      :id="id"
                      v-model="form.value"
                      autocomplete="new-password"
                      :placeholder="isEditing ? 'Đã lưu, để trống để giữ nguyên' : '-----BEGIN OPENSSH PRIVATE KEY-----\n...'"
                      :aria-invalid="error ? 'true' : undefined"
                      :aria-describedby="ariaDescribedby"
                      :class="[
                        'w-full flex-1 min-h-[160px] p-3 pr-10 text-xs font-mono rounded-[6px] bg-card text-fg border transition-colors resize-none',
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
            </div>
          </div>
        </div>
      </form>
  </div>
</template>
