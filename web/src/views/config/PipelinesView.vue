<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import {
  Workflow,
  Plus,
  Pencil,
  Trash2,
  Play,
  AlertCircle,
  GitBranch,
  Hammer,
  Rocket
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
import StatusBadge from '../../components/StatusBadge.vue'
import { useConfigPipelinesStore } from '../../stores/configPipelines'
import { useConfigSlavesStore } from '../../stores/configSlaves'
import { useConfigCredentialsStore } from '../../stores/configCredentials'
import { useConfigDeployTargetsStore } from '../../stores/configDeployTargets'
import { useToast } from '../../composables/useToast'
import type { Pipeline, PipelineInput } from '../../api/types'
import { ApiError } from '../../api/client'

const router = useRouter()
const store = useConfigPipelinesStore()
const slaveStore = useConfigSlavesStore()
const credStore = useConfigCredentialsStore()
const deployTargetStore = useConfigDeployTargetsStore()
const toast = useToast()

const isDialogOpen = ref(false)
const isConfirmOpen = ref(false)
const editingPipeline = ref<Pipeline | null>(null)
const deletingPipeline = ref<Pipeline | null>(null)
const actionLoading = ref(false)
const deleteLoading = ref(false)

const form = ref<{
  name: string
  gitUrl: string
  gitBranch: string
  gitCredentialId: string | number
  slaveId: string | number
  dockerfilePath: string
  buildContext: string
  imageName: string
  registryHost: string
  deployRegistryHost: string
  registryCredentialId: string | number | null
  deployNamespace: string
  deployDeployment: string
  deployContainer: string
}>({
  name: '',
  gitUrl: '',
  gitBranch: 'main',
  gitCredentialId: '',
  slaveId: '',
  dockerfilePath: 'Dockerfile',
  buildContext: '',
  imageName: '',
  registryHost: '',
  deployRegistryHost: '',
  registryCredentialId: '',
  deployNamespace: '',
  deployDeployment: '',
  deployContainer: ''
})

const formErrors = ref<Record<string, string>>({})

const isEditing = computed(() => editingPipeline.value !== null)

const gitCredentials = computed(() => {
  return credStore.credentials.filter((c) => c.kind === 'git_token')
})

const gitCredentialOptions = computed(() => {
  return gitCredentials.value.map((c) => ({
    label: c.name,
    value: c.id
  }))
})

const registryCredentials = computed(() => {
  return credStore.credentials.filter((c) => c.kind === 'registry')
})

const registryCredentialOptions = computed(() => {
  return [
    { label: 'Không cần đăng nhập', value: '' },
    ...registryCredentials.value.map((c) => ({
      label: c.username ? `${c.name} (${c.username})` : c.name,
      value: c.id
    }))
  ]
})

const slaveOptions = computed(() => {
  return slaveStore.slaves.map((s) => ({
    label: `${s.name} (${s.host})`,
    value: s.id
  }))
})

const allowedNamespaces = computed(() => {
  return [...new Set(deployTargetStore.deployTargets.map((t) => t.namespace))].sort()
})

const allowedDeployments = computed(() => {
  const ns = form.value.deployNamespace.trim()
  const targets = ns
    ? deployTargetStore.deployTargets.filter((t) => t.namespace === ns)
    : deployTargetStore.deployTargets
  return [...new Set(targets.map((t) => t.deployment))].sort()
})

function getSlaveName(slaveId: number): string {
  const slave = slaveStore.slaves.find((s) => s.id === slaveId)
  return slave ? slave.name : `#${slaveId}`
}

function getRegistryCredentialName(id: number | null | undefined): string {
  if (id === null || id === undefined) return 'Không cần đăng nhập'
  const cred = credStore.credentials.find((c) => c.id === id)
  return cred ? (cred.username ? `${cred.name} (${cred.username})` : cred.name) : `#${id}`
}

function resetForm() {
  form.value = {
    name: '',
    gitUrl: '',
    gitBranch: 'main',
    gitCredentialId: gitCredentials.value[0]?.id ?? '',
    slaveId: slaveStore.slaves[0]?.id ?? '',
    dockerfilePath: 'Dockerfile',
    buildContext: '',
    imageName: '',
    registryHost: '',
    deployRegistryHost: '',
    registryCredentialId: registryCredentials.value[0]?.id ?? '',
    deployNamespace: '',
    deployDeployment: '',
    deployContainer: ''
  }
  formErrors.value = {}
  editingPipeline.value = null
}

function openAddDialog() {
  resetForm()
  isDialogOpen.value = true
}

function openEditDialog(pipeline: Pipeline) {
  resetForm()
  editingPipeline.value = pipeline
  form.value = {
    name: pipeline.name,
    gitUrl: pipeline.gitUrl,
    gitBranch: pipeline.gitBranch || 'main',
    gitCredentialId: pipeline.gitCredentialId,
    slaveId: pipeline.slaveId,
    dockerfilePath: pipeline.dockerfilePath || 'Dockerfile',
    buildContext: pipeline.buildContext ?? '',
    imageName: pipeline.imageName,
    registryHost: pipeline.registryHost,
    deployRegistryHost: pipeline.deployRegistryHost ?? '',
    registryCredentialId: pipeline.registryCredentialId ?? '',
    deployNamespace: pipeline.deployNamespace,
    deployDeployment: pipeline.deployDeployment,
    deployContainer: pipeline.deployContainer
  }
  isDialogOpen.value = true
}

function closeDialog() {
  isDialogOpen.value = false
  resetForm()
}

// Client-side validation:
// - Git URL must start with http:// or https://
// - Image name must be lowercase alphanumeric and ._-/
// - Kubernetes namespace and deployment must follow DNS-1123 label format
// - Host format: alphanumeric, ., -, and optional :port; no http://, whitespace, or special chars
const DNS_1123_REGEX = /^[a-z0-9]([-a-z0-9]*[a-z0-9])?$/
const IMAGE_NAME_REGEX = /^[a-z0-9._/-]+$/

function validateHost(val: string): string | null {
  const trimmed = val.trim()
  if (!trimmed) return null
  if (/^https?:\/\//i.test(trimmed)) {
    return 'Địa chỉ host không được chứa http:// hoặc https://'
  }
  if (/\s/.test(trimmed)) {
    return 'Địa chỉ host không được chứa khoảng trắng'
  }
  if (!/^[a-zA-Z0-9.-]+(?::\d+)?$/.test(trimmed)) {
    return 'Địa chỉ host chỉ được chứa chữ cái, số, dấu chấm, gạch ngang và cổng tuỳ chọn (ví dụ: registry.local:5000)'
  }
  return null
}

function validateForm(): boolean {
  const errors: Record<string, string> = {}

  if (!form.value.name.trim()) {
    errors.name = 'Vui lòng nhập tên pipeline'
  }

  // Source group
  if (!form.value.gitUrl.trim()) {
    errors.gitUrl = 'Vui lòng nhập địa chỉ Git URL'
  } else if (!/^https?:\/\/.+/.test(form.value.gitUrl.trim())) {
    errors.gitUrl = 'Git URL phải bắt đầu bằng http:// hoặc https://'
  }

  if (!form.value.gitCredentialId) {
    errors.gitCredentialId = 'Vui lòng chọn Token Git'
  }

  // Build group
  if (!form.value.slaveId) {
    errors.slaveId = 'Vui lòng chọn máy build'
  }

  const ctx = form.value.buildContext.trim()
  if (ctx) {
    if (ctx.length > 200) {
      errors.buildContext = 'Thư mục context build tối đa 200 ký tự'
    } else if (ctx.startsWith('/') || ctx.startsWith('-')) {
      errors.buildContext = 'Thư mục context build không được bắt đầu bằng / hoặc -'
    } else if (ctx.includes('..')) {
      errors.buildContext = 'Thư mục context build không được chứa ..'
    } else if (!/^[A-Za-z0-9._/-]+$/.test(ctx)) {
      errors.buildContext = 'Thư mục context build chỉ chứa chữ cái, số và ký tự . _ / -'
    }
  }

  if (!form.value.imageName.trim()) {
    errors.imageName = 'Vui lòng nhập tên Image'
  } else if (!IMAGE_NAME_REGEX.test(form.value.imageName.trim())) {
    errors.imageName = 'Tên image chỉ chứa chữ thường, số và ký tự . _ - /'
  }

  if (!form.value.registryHost.trim()) {
    errors.registryHost = 'Vui lòng nhập địa chỉ máy chủ Registry'
  } else {
    const hostErr = validateHost(form.value.registryHost)
    if (hostErr) {
      errors.registryHost = hostErr
    }
  }

  // Deploy group
  const deployHost = form.value.deployRegistryHost.trim()
  if (deployHost) {
    const hostErr = validateHost(deployHost)
    if (hostErr) {
      errors.deployRegistryHost = hostErr
    }
  }

  const ns = form.value.deployNamespace.trim()
  if (!ns) {
    errors.deployNamespace = 'Vui lòng nhập Namespace'
  } else if (ns.length > 63 || !DNS_1123_REGEX.test(ns)) {
    errors.deployNamespace = 'Namespace phải tuân theo chuẩn DNS-1123 (chữ thường, số, dấu gạch ngang; tối đa 63 ký tự)'
  }

  const deploy = form.value.deployDeployment.trim()
  if (!deploy) {
    errors.deployDeployment = 'Vui lòng nhập tên Deployment'
  } else if (deploy.length > 63 || !DNS_1123_REGEX.test(deploy)) {
    errors.deployDeployment = 'Deployment phải tuân theo chuẩn DNS-1123 (chữ thường, số, dấu gạch ngang; tối đa 63 ký tự)'
  }

  if (!form.value.deployContainer.trim()) {
    errors.deployContainer = 'Vui lòng nhập tên Container cần cập nhật'
  }

  formErrors.value = errors
  return Object.keys(errors).length === 0
}

async function handleSave() {
  if (!validateForm()) return

  actionLoading.value = true
  try {
    const regCredId =
      form.value.registryCredentialId === '' ||
      form.value.registryCredentialId === null ||
      form.value.registryCredentialId === undefined
        ? null
        : Number(form.value.registryCredentialId)

    const trimmedContext = form.value.buildContext.trim()
    const buildContext = trimmedContext !== '' ? trimmedContext : null

    const trimmedDeployHost = form.value.deployRegistryHost.trim()
    const deployRegistryHost = trimmedDeployHost !== '' ? trimmedDeployHost : null

    const payload: PipelineInput = {
      name: form.value.name.trim(),
      gitUrl: form.value.gitUrl.trim(),
      gitBranch: form.value.gitBranch.trim() || 'main',
      gitCredentialId: Number(form.value.gitCredentialId),
      slaveId: Number(form.value.slaveId),
      dockerfilePath: form.value.dockerfilePath.trim() || 'Dockerfile',
      buildContext,
      imageName: form.value.imageName.trim(),
      registryHost: form.value.registryHost.trim(),
      deployRegistryHost,
      registryCredentialId: regCredId,
      deployNamespace: form.value.deployNamespace.trim(),
      deployDeployment: form.value.deployDeployment.trim(),
      deployContainer: form.value.deployContainer.trim()
    }

    if (isEditing.value && editingPipeline.value) {
      await store.updatePipeline(editingPipeline.value.id, payload)
      toast.success('Cập nhật pipeline thành công')
    } else {
      await store.createPipeline(payload)
      toast.success('Tạo pipeline thành công')
    }
    closeDialog()
  } catch (err: any) {
    if (err instanceof ApiError && err.problem?.errors) {
      for (const [key, msgs] of Object.entries(err.problem.errors)) {
        const lowerKey = key.toLowerCase()
        if (msgs && msgs.length > 0) {
          const firstMsg = msgs[0]
          if (lowerKey === 'name') formErrors.value.name = firstMsg
          else if (lowerKey === 'giturl') formErrors.value.gitUrl = firstMsg
          else if (lowerKey === 'gitbranch') formErrors.value.gitBranch = firstMsg
          else if (lowerKey === 'gitcredentialid') formErrors.value.gitCredentialId = firstMsg
          else if (lowerKey === 'slaveid') formErrors.value.slaveId = firstMsg
          else if (lowerKey === 'dockerfilepath') formErrors.value.dockerfilePath = firstMsg
          else if (lowerKey === 'buildcontext') formErrors.value.buildContext = firstMsg
          else if (lowerKey === 'imagename') formErrors.value.imageName = firstMsg
          else if (lowerKey === 'registryhost') formErrors.value.registryHost = firstMsg
          else if (lowerKey === 'deployregistryhost') formErrors.value.deployRegistryHost = firstMsg
          else if (lowerKey === 'registrycredentialid') formErrors.value.registryCredentialId = firstMsg
          else if (lowerKey === 'deploynamespace') formErrors.value.deployNamespace = firstMsg
          else if (lowerKey === 'deploydeployment') formErrors.value.deployDeployment = firstMsg
          else if (lowerKey === 'deploycontainer') formErrors.value.deployContainer = firstMsg
        }
      }
    }
    toast.error(err.message || 'Lưu cấu hình pipeline thất bại')
  } finally {
    actionLoading.value = false
  }
}

function promptDelete(pipeline: Pipeline) {
  deletingPipeline.value = pipeline
  isConfirmOpen.value = true
}

async function handleDelete() {
  if (!deletingPipeline.value) return

  deleteLoading.value = true
  try {
    await store.deletePipeline(deletingPipeline.value.id)
    toast.success('Đã xoá pipeline thành công')
    isConfirmOpen.value = false
    deletingPipeline.value = null
  } catch (err: any) {
    if (err instanceof ApiError && err.status === 409) {
      toast.error('Không thể xoá pipeline khi đang có lượt chạy hoạt động')
    } else {
      toast.error(err.message || 'Xoá pipeline thất bại')
    }
  } finally {
    deleteLoading.value = false
  }
}

async function handleTriggerRun(pipeline: Pipeline) {
  try {
    const run = await store.triggerRun(pipeline.id)
    toast.success(`Đã kích hoạt lượt chạy #${run.id}`)
    router.push(`/runs/${run.id}`)
  } catch (err: any) {
    if (err instanceof ApiError && err.status === 409) {
      toast.error('Pipeline đang có lượt chạy')
    } else {
      toast.error(err.message || 'Kích hoạt lượt chạy thất bại')
    }
  }
}

onMounted(() => {
  store.fetchPipelines().catch(() => {})
  slaveStore.fetchSlaves().catch(() => {})
  credStore.fetchCredentials().catch(() => {})
  deployTargetStore.fetchDeployTargets().catch(() => {})
})
</script>

<template>
  <AppLayout>
    <div class="space-y-6">
      <!-- Header -->
      <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <h2 class="text-xl font-semibold text-fg">Cấu hình Pipeline</h2>
          <p class="text-sm text-muted-fg mt-1">
            Quản lý các pipeline CI/CD: tự động kéo mã nguồn, đóng gói Docker image và deploy lên Kubernetes.
          </p>
        </div>
        <AppButton @click="openAddDialog">
          <Plus class="w-4 h-4" aria-hidden="true" />
          <span>Thêm pipeline</span>
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
        v-if="store.loading && store.pipelines.length === 0"
        class="flex items-center justify-center p-12 text-muted-fg text-sm"
      >
        <div class="flex items-center gap-2">
          <div class="w-4 h-4 border-2 border-primary border-t-transparent rounded-full animate-spin" />
          <span>Đang tải danh sách pipeline...</span>
        </div>
      </div>

      <!-- Empty state -->
      <EmptyState
        v-else-if="!store.loading && store.pipelines.length === 0"
        title="Chưa có pipeline"
        description="Tạo pipeline đầu tiên để tự động hoá quy trình build và triển khai ứng dụng."
        :icon="Workflow"
      >
        <template #action>
          <AppButton @click="openAddDialog">
            <Plus class="w-4 h-4" aria-hidden="true" />
            <span>Thêm pipeline</span>
          </AppButton>
        </template>
      </EmptyState>

      <!-- Table -->
      <AppTable v-else>
        <template #header>
          <tr>
            <th scope="col" class="py-3">Tên pipeline</th>
            <th scope="col" class="py-3">Nguồn Git</th>
            <th scope="col" class="py-3">Máy build</th>
            <th scope="col" class="py-3">Image Docker</th>
            <th scope="col" class="py-3">Đích Deploy</th>
            <th scope="col" class="py-3">Lượt chạy gần nhất</th>
            <th scope="col" class="py-3 text-right">Thao tác</th>
          </tr>
        </template>

        <tr v-for="pipeline in store.pipelines" :key="pipeline.id">
          <td class="font-medium text-fg">
            <div class="flex items-center gap-2">
              <Workflow class="w-4 h-4 text-muted-fg flex-shrink-0" aria-hidden="true" />
              <span>{{ pipeline.name }}</span>
            </div>
          </td>
          <td class="font-mono text-xs text-muted-fg">
            <span class="text-fg">{{ pipeline.gitUrl }}</span>
            <span class="text-primary font-semibold">@{{ pipeline.gitBranch || 'main' }}</span>
          </td>
          <td class="text-xs text-fg">
            {{ getSlaveName(pipeline.slaveId) }}
          </td>
          <td class="font-mono text-xs text-fg">
            {{ pipeline.registryHost }}/{{ pipeline.imageName }}
          </td>
          <td class="font-mono text-xs text-muted-fg">
            <span class="text-fg">{{ pipeline.deployNamespace }}</span>/{{ pipeline.deployDeployment }}
          </td>
          <td>
            <StatusBadge
              v-if="pipeline.lastRun"
              :status="pipeline.lastRun.status"
              size="sm"
            />
            <span v-else class="text-muted-fg text-xs">—</span>
          </td>
          <td class="text-right">
            <div class="inline-flex items-center gap-1">
              <AppButton
                variant="primary"
                size="sm"
                :loading="store.triggeringId === pipeline.id"
                aria-label="Chạy ngay"
                title="Kích hoạt chạy pipeline ngay"
                @click="handleTriggerRun(pipeline)"
              >
                <Play class="w-3.5 h-3.5 fill-current" aria-hidden="true" />
                <span>Chạy ngay</span>
              </AppButton>
              <AppButton
                variant="ghost"
                size="sm"
                aria-label="Sửa pipeline"
                @click="openEditDialog(pipeline)"
              >
                <Pencil class="w-3.5 h-3.5" aria-hidden="true" />
                <span class="hidden sm:inline">Sửa</span>
              </AppButton>
              <AppButton
                variant="ghost"
                size="sm"
                class="text-danger hover:text-danger hover:bg-danger/10"
                aria-label="Xoá pipeline"
                @click="promptDelete(pipeline)"
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
      :title="isEditing ? 'Sửa pipeline' : 'Thêm pipeline mới'"
      max-width="lg"
      @close="closeDialog"
    >
      <form class="space-y-6" @submit.prevent="handleSave">
        <!-- Pipeline General Name -->
        <AppField
          label="Tên pipeline"
          required
          description="Tên đại diện cho quy trình build và deploy."
          :error="formErrors.name"
        >
          <template #default="{ id, error, ariaDescribedby }">
            <AppInput
              :id="id"
              v-model="form.name"
              placeholder="Ví dụ: shipit-demo-pipeline"
              :error="error"
              :aria-describedby="ariaDescribedby"
            />
          </template>
        </AppField>

        <!-- Group 1: Source (Nguồn) -->
        <div class="space-y-3 pt-2 border-t border-border">
          <div class="flex items-center gap-2 text-sm font-semibold text-fg">
            <GitBranch class="w-4 h-4 text-primary" aria-hidden="true" />
            <span>Nguồn (Git)</span>
          </div>

          <div class="grid grid-cols-1 sm:grid-cols-3 gap-3">
            <div class="sm:col-span-2">
              <AppField
                label="Địa chỉ Git URL"
                required
                :error="formErrors.gitUrl"
              >
                <template #default="{ id, error, ariaDescribedby }">
                  <AppInput
                    :id="id"
                    v-model="form.gitUrl"
                    placeholder="https://git.example.com/repo.git"
                    :error="error"
                    :aria-describedby="ariaDescribedby"
                  />
                </template>
              </AppField>
            </div>
            <div>
              <AppField
                label="Nhánh (Branch)"
                required
                :error="formErrors.gitBranch"
              >
                <template #default="{ id, error, ariaDescribedby }">
                  <AppInput
                    :id="id"
                    v-model="form.gitBranch"
                    placeholder="main"
                    :error="error"
                    :aria-describedby="ariaDescribedby"
                  />
                </template>
              </AppField>
            </div>
          </div>

          <AppField
            label="Token Git"
            required
            description="Thông tin xác thực loại Token Git có quyền đọc kho mã nguồn."
            :error="formErrors.gitCredentialId"
          >
            <template #default="{ id, error, ariaDescribedby }">
              <AppSelect
                :id="id"
                v-model="form.gitCredentialId"
                :options="gitCredentialOptions"
                placeholder="-- Chọn Token Git --"
                :disabled="gitCredentials.length === 0"
                :error="error"
                :aria-describedby="ariaDescribedby"
              />
            </template>
          </AppField>
        </div>

        <!-- Group 2: Build -->
        <div class="space-y-3 pt-2 border-t border-border">
          <div class="flex items-center gap-2 text-sm font-semibold text-fg">
            <Hammer class="w-4 h-4 text-primary" aria-hidden="true" />
            <span>Build (Docker)</span>
          </div>

          <AppField
            label="Máy build (Slave)"
            required
            :error="formErrors.slaveId"
          >
            <template #default="{ id, error, ariaDescribedby }">
              <AppSelect
                :id="id"
                v-model="form.slaveId"
                :options="slaveOptions"
                placeholder="-- Chọn máy build --"
                :disabled="slaveStore.slaves.length === 0"
                :error="error"
                :aria-describedby="ariaDescribedby"
              />
            </template>
          </AppField>

          <div class="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <AppField
              label="Đường dẫn Dockerfile"
              required
              :error="formErrors.dockerfilePath"
            >
              <template #default="{ id, error, ariaDescribedby }">
                <AppInput
                  :id="id"
                  v-model="form.dockerfilePath"
                  placeholder="Dockerfile"
                  :error="error"
                  :aria-describedby="ariaDescribedby"
                />
              </template>
            </AppField>

            <AppField
              label="Thư mục context build"
              description="Tuỳ chọn. Đường dẫn trong repo dùng làm context khi build. Nhập `.` để dùng gốc repo. Để trống để dùng thư mục chứa Dockerfile."
              :error="formErrors.buildContext"
            >
              <template #default="{ id, error, ariaDescribedby }">
                <AppInput
                  :id="id"
                  v-model="form.buildContext"
                  placeholder=". (gốc repo)"
                  :error="error"
                  :aria-describedby="ariaDescribedby"
                />
              </template>
            </AppField>
          </div>

          <div class="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <AppField
              label="Tên Image"
              required
              description="Chỉ gồm chữ thường, số và ký tự . _ - /"
              :error="formErrors.imageName"
            >
              <template #default="{ id, error, ariaDescribedby }">
                <AppInput
                  :id="id"
                  v-model="form.imageName"
                  placeholder="Ví dụ: shipit-demo"
                  :error="error"
                  :aria-describedby="ariaDescribedby"
                />
              </template>
            </AppField>

            <AppField
              label="Máy chủ Registry"
              required
              placeholder="192.168.1.241"
              :error="formErrors.registryHost"
            >
              <template #default="{ id, error, ariaDescribedby }">
                <AppInput
                  :id="id"
                  v-model="form.registryHost"
                  placeholder="Ví dụ: 192.168.1.241:5000"
                  :error="error"
                  :aria-describedby="ariaDescribedby"
                />
              </template>
            </AppField>
          </div>

          <AppField
            label="Thông tin xác thực Registry"
            description="Chỉ chọn khi registry yêu cầu đăng nhập. Để trống nếu registry không cần xác thực."
            :error="formErrors.registryCredentialId"
          >
            <template #default="{ id, error, ariaDescribedby }">
              <AppSelect
                :id="id"
                v-model="form.registryCredentialId"
                :options="registryCredentialOptions"
                :error="error"
                :aria-describedby="ariaDescribedby"
              />
            </template>
          </AppField>
        </div>

        <!-- Group 3: Deploy (Kubernetes) -->
        <div class="space-y-3 pt-2 border-t border-border">
          <div class="flex items-center gap-2 text-sm font-semibold text-fg">
            <Rocket class="w-4 h-4 text-primary" aria-hidden="true" />
            <span>Deploy (Kubernetes)</span>
          </div>

          <div class="grid grid-cols-1 sm:grid-cols-3 gap-3">
            <div>
              <AppField
                label="Namespace"
                required
                :error="formErrors.deployNamespace"
              >
                <template #default="{ id, error, ariaDescribedby }">
                  <AppInput
                    :id="id"
                    v-model="form.deployNamespace"
                    placeholder="devops"
                    :error="error"
                    :aria-describedby="ariaDescribedby"
                    list="allowed-namespaces-list"
                  />
                  <datalist id="allowed-namespaces-list">
                    <option v-for="ns in allowedNamespaces" :key="ns" :value="ns" />
                  </datalist>
                </template>
              </AppField>
            </div>

            <div>
              <AppField
                label="Deployment"
                required
                :error="formErrors.deployDeployment"
              >
                <template #default="{ id, error, ariaDescribedby }">
                  <AppInput
                    :id="id"
                    v-model="form.deployDeployment"
                    placeholder="shipit-demo"
                    :error="error"
                    :aria-describedby="ariaDescribedby"
                    list="allowed-deployments-list"
                  />
                  <datalist id="allowed-deployments-list">
                    <option v-for="dep in allowedDeployments" :key="dep" :value="dep" />
                  </datalist>
                </template>
              </AppField>
            </div>

            <div>
              <AppField
                label="Container"
                required
                :error="formErrors.deployContainer"
              >
                <template #default="{ id, error, ariaDescribedby }">
                  <AppInput
                    :id="id"
                    v-model="form.deployContainer"
                    placeholder="app"
                    :error="error"
                    :aria-describedby="ariaDescribedby"
                  />
                </template>
              </AppField>
            </div>
          </div>

          <AppField
            label="Host registry khi deploy"
            description="Tuỳ chọn. Dùng khi cụm k8s gọi registry bằng tên khác với máy build (ví dụ máy build dùng avina.ai:5000, cụm dùng registry.local:5000). Để trống để dùng chung Máy chủ Registry."
            :error="formErrors.deployRegistryHost"
          >
            <template #default="{ id, error, ariaDescribedby }">
              <AppInput
                :id="id"
                v-model="form.deployRegistryHost"
                placeholder="giống Máy chủ Registry"
                :error="error"
                :aria-describedby="ariaDescribedby"
              />
            </template>
          </AppField>
        </div>
      </form>

      <template #footer>
        <AppButton variant="secondary" :disabled="actionLoading" @click="closeDialog">
          Huỷ
        </AppButton>
        <AppButton :loading="actionLoading" @click="handleSave">
          {{ isEditing ? 'Lưu thay đổi' : 'Tạo pipeline' }}
        </AppButton>
      </template>
    </AppDialog>

    <!-- Confirm Delete Dialog -->
    <ConfirmDialog
      v-model="isConfirmOpen"
      title="Xoá pipeline"
      :message="`Bạn có chắc chắn muốn xoá pipeline &quot;${deletingPipeline?.name}&quot;? Hành động này không thể hoàn tác.`"
      confirm-text="Xoá pipeline"
      cancel-text="Huỷ"
      :loading="deleteLoading"
      :danger="true"
      @confirm="handleDelete"
      @cancel="isConfirmOpen = false"
    />
  </AppLayout>
</template>
