<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  Boxes,
  Plus,
  ArrowLeft,
  CheckCircle2,
  FileCode,
  Save
} from 'lucide-vue-next'
import AppLayout from '../../layouts/AppLayout.vue'
import AppButton from '../../components/AppButton.vue'
import AppInput from '../../components/AppInput.vue'
import AppSelect from '../../components/AppSelect.vue'
import AppField from '../../components/AppField.vue'
import TaskCard from '../../components/job/TaskCard.vue'
import YamlPreview from '../../components/job/YamlPreview.vue'
import AllowlistMultiSelect from '../../components/job/AllowlistMultiSelect.vue'
import { useJobsStore } from '../../stores/jobs'
import { useJobsAllowStore } from '../../stores/jobsAllow'
import { useConfigSlavesStore } from '../../stores/configSlaves'
import { useConfigCredentialsStore } from '../../stores/configCredentials'
import { useToast } from '../../composables/useToast'
import type { JobInput, JobTaskInput, JobPreviewTask } from '../../api/types'
import { ApiError } from '../../api/client'

const route = useRoute()
const router = useRouter()
const toast = useToast()

const jobsStore = useJobsStore()
const allowStore = useJobsAllowStore()
const slaveStore = useConfigSlavesStore()
const credStore = useConfigCredentialsStore()

const jobId = computed(() => {
  const p = route.params.id
  return p ? Number(p) : null
})
const isEditing = computed(() => jobId.value !== null && !isNaN(jobId.value))

const pageLoading = ref(true)
const saveLoading = ref(false)
const validateLoading = ref(false)
const previewLoading = ref(false)

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
  namespace: string
  containerName: string
  serviceAccount: string
  imagePullSecrets: string[]
  tasks: JobTaskInput[]
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
  namespace: '',
  containerName: '',
  serviceAccount: '',
  imagePullSecrets: [],
  tasks: [
    {
      label: 'task-1',
      args: [],
      envFromConfigMaps: [],
      envFromSecrets: [],
      env: [],
      timeoutSeconds: 1800,
      backoffLimit: 0,
      ttlSeconds: 3600
    }
  ]
})

const formErrors = ref<Record<string, string[]>>({})
const previewTasks = ref<JobPreviewTask[]>([])

// Git Credentials (kind === 'git_token')
const gitCredentialOptions = computed(() => {
  return credStore.credentials
    .filter((c) => c.kind === 'git_token')
    .map((c) => ({
      label: c.name,
      value: c.id
    }))
})

// Registry Credentials (kind === 'registry')
const registryCredentialOptions = computed(() => {
  return [
    { label: 'Không cần đăng nhập', value: '' },
    ...credStore.credentials
      .filter((c) => c.kind === 'registry')
      .map((c) => ({
        label: c.name,
        value: c.id
      }))
  ]
})

// Slaves
const slaveOptions = computed(() => {
  return slaveStore.slaves.map((s) => ({
    label: `${s.name} (${s.host})`,
    value: s.id
  }))
})

// Namespace options from allowlist
const namespaceOptions = computed(() => {
  return allowStore.namespaces.map((ns) => ({
    label: ns,
    value: ns
  }))
})

// PullSecret options for current namespace
const currentPullSecretOptions = computed(() => {
  if (!form.value.namespace) return []
  return allowStore.pullSecretsFor(form.value.namespace)
})

// ConfigMap options for current namespace
const currentConfigMapOptions = computed(() => {
  if (!form.value.namespace) return []
  return allowStore.configMapsFor(form.value.namespace)
})

// Secret options for current namespace
const currentSecretOptions = computed(() => {
  if (!form.value.namespace) return []
  return allowStore.secretsFor(form.value.namespace)
})

onMounted(async () => {
  try {
    await Promise.all([
      allowStore.fetchEntries(),
      slaveStore.fetchSlaves(),
      credStore.fetchCredentials()
    ])

    if (isEditing.value && jobId.value) {
      const existing = await jobsStore.getJob(jobId.value)
      form.value = {
        name: existing.name,
        gitUrl: existing.gitUrl,
        gitBranch: existing.gitBranch || 'main',
        gitCredentialId: existing.gitCredentialId,
        slaveId: existing.slaveId,
        dockerfilePath: existing.dockerfilePath || 'Dockerfile',
        buildContext: existing.buildContext || '',
        imageName: existing.imageName,
        registryHost: existing.registryHost,
        deployRegistryHost: existing.deployRegistryHost || '',
        registryCredentialId: existing.registryCredentialId ?? '',
        namespace: existing.namespace,
        containerName: existing.containerName,
        serviceAccount: existing.serviceAccount || '',
        imagePullSecrets: [...existing.imagePullSecrets],
        tasks: existing.tasks.map((t) => ({
          label: t.label,
          args: [...t.args],
          envFromConfigMaps: [...t.envFromConfigMaps],
          envFromSecrets: [...t.envFromSecrets],
          env: t.env.map((e) => ({ ...e })),
          timeoutSeconds: t.timeoutSeconds,
          backoffLimit: t.backoffLimit,
          ttlSeconds: t.ttlSeconds
        }))
      }
      await refreshPreview()
    } else {
      // Default namespace to first allowed namespace if available
      if (allowStore.namespaces.length > 0 && !form.value.namespace) {
        form.value.namespace = allowStore.namespaces[0]
      }
      if (gitCredentialOptions.value.length > 0 && !form.value.gitCredentialId) {
        form.value.gitCredentialId = gitCredentialOptions.value[0].value
      }
      if (slaveOptions.value.length > 0 && !form.value.slaveId) {
        form.value.slaveId = slaveOptions.value[0].value
      }
    }
  } catch (err: any) {
    toast.error(err.message || 'Lỗi khi tải dữ liệu khởi tạo')
  } finally {
    pageLoading.value = false
  }
})

// Helper to convert form to API JobInput payload
function buildPayload(): JobInput {
  return {
    name: form.value.name.trim(),
    gitUrl: form.value.gitUrl.trim(),
    gitBranch: form.value.gitBranch.trim() || 'main',
    gitCredentialId: Number(form.value.gitCredentialId),
    slaveId: Number(form.value.slaveId),
    dockerfilePath: form.value.dockerfilePath.trim() || 'Dockerfile',
    buildContext: form.value.buildContext.trim() === '' ? null : form.value.buildContext.trim(),
    imageName: form.value.imageName.trim(),
    registryHost: form.value.registryHost.trim(),
    deployRegistryHost: form.value.deployRegistryHost.trim() === '' ? null : form.value.deployRegistryHost.trim(),
    registryCredentialId: form.value.registryCredentialId === '' || form.value.registryCredentialId === null
      ? null
      : Number(form.value.registryCredentialId),
    namespace: form.value.namespace.trim(),
    containerName: form.value.containerName.trim(),
    serviceAccount: form.value.serviceAccount.trim() === '' ? null : form.value.serviceAccount.trim(),
    imagePullSecrets: [...form.value.imagePullSecrets],
    tasks: form.value.tasks.map((t) => ({
      label: t.label.trim(),
      args: [...(t.args || [])],
      envFromConfigMaps: [...(t.envFromConfigMaps || [])],
      envFromSecrets: [...(t.envFromSecrets || [])],
      env: (t.env || []).map((e) => ({
        name: e.name ? e.name.trim() : null,
        value: e.secretName ? null : (e.value ?? null),
        secretName: e.secretName ? e.secretName.trim() : null,
        secretKey: e.secretKey ? e.secretKey.trim() : null
      })),
      timeoutSeconds: t.timeoutSeconds === null || t.timeoutSeconds === undefined ? null : Number(t.timeoutSeconds),
      backoffLimit: t.backoffLimit === null || t.backoffLimit === undefined ? 0 : Number(t.backoffLimit),
      ttlSeconds: t.ttlSeconds === null || t.ttlSeconds === undefined ? 3600 : Number(t.ttlSeconds)
    }))
  }
}

// Client-side quick check
function validateClientSide(): boolean {
  const errors: Record<string, string[]> = {}

  if (!form.value.name.trim()) {
    errors.name = ['Vui lòng nhập tên Job']
  } else if (!/^[a-z0-9]([-a-z0-9.]*[a-z0-9])?$/.test(form.value.name.trim())) {
    errors.name = ['Tên Job phải tuân theo chuẩn DNS-1123 subdomain (chữ thường, số, dấu chấm, dấu gạch ngang)']
  }

  if (!form.value.gitUrl.trim()) {
    errors.gitUrl = ['Vui lòng nhập địa chỉ Git URL']
  }

  if (!form.value.gitCredentialId) {
    errors.gitCredentialId = ['Vui lòng chọn thông tin xác thực Git']
  }

  if (!form.value.slaveId) {
    errors.slaveId = ['Vui lòng chọn máy build']
  }

  if (!form.value.imageName.trim()) {
    errors.imageName = ['Vui lòng nhập tên Image']
  }

  if (!form.value.registryHost.trim()) {
    errors.registryHost = ['Vui lòng nhập Host registry']
  }

  if (!form.value.namespace.trim()) {
    errors.namespace = ['Vui lòng chọn Namespace']
  }

  if (!form.value.containerName.trim()) {
    errors.containerName = ['Vui lòng nhập tên container']
  }

  if (form.value.tasks.length === 0) {
    errors.tasks = ['Job phải có ít nhất một tác vụ']
  } else {
    form.value.tasks.forEach((t, i) => {
      if (!t.label || !t.label.trim()) {
        errors[`tasks[${i}].label`] = ['Vui lòng nhập nhãn tác vụ']
      }
      if (t.timeoutSeconds === undefined || t.timeoutSeconds === null || Number(t.timeoutSeconds) <= 0) {
        errors[`tasks[${i}].timeoutSeconds`] = ['Thời gian timeout bắt buộc và phải lớn hơn 0']
      }
    })
  }

  formErrors.value = errors
  return Object.keys(errors).length === 0
}

// Task management functions
function addTask() {
  form.value.tasks.push({
    label: `task-${form.value.tasks.length + 1}`,
    args: [],
    envFromConfigMaps: [],
    envFromSecrets: [],
    env: [],
    timeoutSeconds: 1800,
    backoffLimit: 0,
    ttlSeconds: 3600
  })
}

function updateTask(index: number, updated: JobTaskInput) {
  form.value.tasks[index] = updated
}

function removeTask(index: number) {
  if (form.value.tasks.length > 1) {
    form.value.tasks.splice(index, 1)
  }
}

function duplicateTask(index: number) {
  const original = form.value.tasks[index]
  const copy: JobTaskInput = {
    label: `${original.label}-copy`,
    args: [...(original.args || [])],
    envFromConfigMaps: [...(original.envFromConfigMaps || [])],
    envFromSecrets: [...(original.envFromSecrets || [])],
    env: (original.env || []).map((e) => ({ ...e })),
    timeoutSeconds: original.timeoutSeconds,
    backoffLimit: original.backoffLimit,
    ttlSeconds: original.ttlSeconds
  }
  form.value.tasks.splice(index + 1, 0, copy)
}

function moveTaskUp(index: number) {
  if (index > 0) {
    const item = form.value.tasks.splice(index, 1)[0]
    form.value.tasks.splice(index - 1, 0, item)
  }
}

function moveTaskDown(index: number) {
  if (index < form.value.tasks.length - 1) {
    const item = form.value.tasks.splice(index, 1)[0]
    form.value.tasks.splice(index + 1, 0, item)
  }
}

// Drag and drop for tasks
let draggedTaskIndex: number | null = null

function onTaskDragStart(index: number, event: DragEvent) {
  draggedTaskIndex = index
  event.dataTransfer?.setData('text/plain', String(index))
}

function onTaskDragOver(index: number, event: DragEvent) {
  event.preventDefault()
}

function onTaskDrop(index: number, event: DragEvent) {
  event.preventDefault()
  if (draggedTaskIndex === null || draggedTaskIndex === index) return
  const item = form.value.tasks.splice(draggedTaskIndex, 1)[0]
  form.value.tasks.splice(index, 0, item)
  draggedTaskIndex = null
}

// Validate button handler
async function handleValidate() {
  validateLoading.value = true
  try {
    const payload = buildPayload()
    const res = await jobsStore.validateJob(payload)
    formErrors.value = res.errors
    if (res.valid) {
      toast.success('Cấu hình hợp lệ!')
    } else {
      toast.error('Phát hiện lỗi trong cấu hình Job')
    }
  } catch (err: any) {
    toast.error(err.message || 'Kiểm tra cấu hình thất bại')
  } finally {
    validateLoading.value = false
  }
}

// Refresh preview YAML
async function refreshPreview() {
  previewLoading.value = true
  try {
    const payload = buildPayload()
    const res = await jobsStore.previewJob(payload)
    previewTasks.value = res.tasks
  } catch {
    // If preview fails due to partial form, keep previous
  } finally {
    previewLoading.value = false
  }
}

// Save form
async function handleSave() {
  if (!validateClientSide()) {
    toast.error('Vui lòng kiểm tra lại các trường có lỗi')
    return
  }

  saveLoading.value = true
  try {
    const payload = buildPayload()
    if (isEditing.value && jobId.value) {
      await jobsStore.updateJob(jobId.value, payload)
      toast.success('Đã cập nhật Job thành công')
    } else {
      await jobsStore.createJob(payload)
      toast.success('Đã tạo Job thành công')
    }
    router.push('/jobs')
  } catch (err: any) {
    if (err instanceof ApiError && err.status === 400 && err.problem?.errors) {
      formErrors.value = err.problem.errors
      toast.error('Dữ liệu không hợp lệ. Vui lòng kiểm tra lại các ô bị đánh dấu.')
    } else if (err instanceof ApiError && err.status === 409) {
      formErrors.value = { name: ['Tên Job đã tồn tại'] }
      toast.error(err.problem?.detail || err.message || 'Tên Job đã tồn tại')
    } else {
      toast.error(err.message || 'Lưu Job thất bại')
    }
  } finally {
    saveLoading.value = false
  }
}
</script>

<template>
  <AppLayout>
    <div class="space-y-8 max-w-4xl mx-auto pb-12">
      <!-- Top header with back link -->
      <div class="flex items-center justify-between border-b border-border pb-4">
        <div class="flex items-center gap-3">
          <button
            type="button"
            aria-label="Quay lại danh sách Job"
            class="p-2 rounded-[6px] text-muted-fg hover:text-fg hover:bg-muted transition-colors cursor-pointer focus-visible:outline-2 focus-visible:outline-ring"
            @click="router.push('/jobs')"
          >
            <ArrowLeft class="w-5 h-5" aria-hidden="true" />
          </button>
          <div>
            <h1 class="text-xl font-bold tracking-tight text-fg flex items-center gap-2">
              <Boxes class="w-5 h-5 text-primary" aria-hidden="true" />
              <span>{{ isEditing ? `Cấu hình Job: ${form.name}` : 'Tạo Job mới' }}</span>
            </h1>
            <p class="text-xs text-muted-fg mt-0.5">
              {{ isEditing ? 'Chỉnh sửa thông số và danh sách tác vụ của Job' : 'Cấu hình nguồn mã, máy build, nơi chạy và các tác vụ k8s' }}
            </p>
          </div>
        </div>

        <div class="flex items-center gap-3">
          <AppButton
            variant="secondary"
            aria-label="Huỷ thao tác"
            @click="router.push('/jobs')"
          >
            Huỷ
          </AppButton>
          <AppButton
            variant="primary"
            :loading="saveLoading"
            :disabled="saveLoading || pageLoading"
            aria-label="Lưu Job"
            @click="handleSave"
          >
            <Save class="w-4 h-4 mr-1.5" aria-hidden="true" />
            <span>{{ isEditing ? 'Lưu thay đổi' : 'Tạo Job' }}</span>
          </AppButton>
        </div>
      </div>

      <!-- Loading skeleton -->
      <div v-if="pageLoading" class="py-16 text-center text-muted-fg">
        Đang tải dữ liệu...
      </div>

      <form v-else class="space-y-8" @submit.prevent="handleSave">
        <!-- ================================================================= -->
        <!-- (1) Thông tin chung và nguồn mã                                   -->
        <!-- ================================================================= -->
        <section class="p-6 rounded-[8px] border border-border bg-card space-y-6">
          <div class="border-b border-border pb-3">
            <h2 class="text-base font-semibold text-fg">
              1. Thông tin chung và nguồn mã
            </h2>
            <p class="text-xs text-muted-fg mt-0.5">
              Cấu hình tên Job, kho Git, máy build và thông số Docker image
            </p>
          </div>

          <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
            <!-- Job Name -->
            <AppField
              label="Tên Job"
              :required="true"
              description="Tên định danh (DNS-1123, chữ thường, số, dấu chấm hoặc gạch ngang)"
              :error="formErrors.name?.[0]"
            >
              <template #default="{ id, error, ariaDescribedby }">
                <AppInput
                  :id="id"
                  v-model="form.name"
                  placeholder="VD: avina-dbmigrator"
                  :disabled="saveLoading"
                  :error="error"
                  :aria-describedby="ariaDescribedby"
                />
              </template>
            </AppField>

            <!-- Git URL -->
            <AppField
              label="Git URL"
              :required="true"
              description="Địa chỉ repository (HTTPS hoặc SSH)"
              :error="formErrors.gitUrl?.[0]"
            >
              <template #default="{ id, error, ariaDescribedby }">
                <AppInput
                  :id="id"
                  v-model="form.gitUrl"
                  placeholder="http://git.example/AvinaWorkspace"
                  :disabled="saveLoading"
                  :error="error"
                  :aria-describedby="ariaDescribedby"
                />
              </template>
            </AppField>

            <!-- Git Branch -->
            <AppField
              label="Nhánh Git"
              description="Mặc định 'main'"
              :error="formErrors.gitBranch?.[0]"
            >
              <template #default="{ id, error, ariaDescribedby }">
                <AppInput
                  :id="id"
                  v-model="form.gitBranch"
                  placeholder="main"
                  :disabled="saveLoading"
                  :error="error"
                  :aria-describedby="ariaDescribedby"
                />
              </template>
            </AppField>

            <!-- Git Credential (kind === 'git_token') -->
            <AppField
              label="Credential Git"
              :required="true"
              description="Token truy cập Git (chỉ lọc credential loại Git Token)"
              :error="formErrors.gitCredentialId?.[0]"
            >
              <template #default="{ id, error, ariaDescribedby }">
                <AppSelect
                  :id="id"
                  v-model="form.gitCredentialId"
                  :options="gitCredentialOptions"
                  placeholder="-- Chọn Credential Git --"
                  :disabled="saveLoading"
                  :error="error"
                  :aria-describedby="ariaDescribedby"
                />
              </template>
            </AppField>

            <!-- Slave -->
            <AppField
              label="Máy build"
              :required="true"
              description="Máy chủ thực hiện checkout và build Docker image"
              :error="formErrors.slaveId?.[0]"
            >
              <template #default="{ id, error, ariaDescribedby }">
                <AppSelect
                  :id="id"
                  v-model="form.slaveId"
                  :options="slaveOptions"
                  placeholder="-- Chọn máy build --"
                  :disabled="saveLoading"
                  :error="error"
                  :aria-describedby="ariaDescribedby"
                />
              </template>
            </AppField>

            <!-- Dockerfile Path -->
            <AppField
              label="Đường dẫn Dockerfile"
              description="Đường dẫn tương đối trong repo. Mặc định 'Dockerfile'"
              :error="formErrors.dockerfilePath?.[0]"
            >
              <template #default="{ id, error, ariaDescribedby }">
                <AppInput
                  :id="id"
                  v-model="form.dockerfilePath"
                  placeholder="docker/Dockerfile.dbmigrator"
                  :disabled="saveLoading"
                  :error="error"
                  :aria-describedby="ariaDescribedby"
                />
              </template>
            </AppField>

            <!-- Build Context -->
            <AppField
              label="Build context"
              description="Thư mục context khi build (. là gốc repository)"
              :error="formErrors.buildContext?.[0]"
            >
              <template #default="{ id, error, ariaDescribedby }">
                <AppInput
                  :id="id"
                  v-model="form.buildContext"
                  placeholder="."
                  :disabled="saveLoading"
                  :error="error"
                  :aria-describedby="ariaDescribedby"
                />
              </template>
            </AppField>

            <!-- Image Name -->
            <AppField
              label="Tên Image"
              :required="true"
              description="Tên image không kèm host registry (VD: avina-dbmigrator)"
              :error="formErrors.imageName?.[0]"
            >
              <template #default="{ id, error, ariaDescribedby }">
                <AppInput
                  :id="id"
                  v-model="form.imageName"
                  placeholder="avina-dbmigrator"
                  :disabled="saveLoading"
                  :error="error"
                  :aria-describedby="ariaDescribedby"
                />
              </template>
            </AppField>

            <!-- Registry Host -->
            <AppField
              label="Registry host"
              :required="true"
              description="Host registry dùng khi push image (VD: 192.168.1.241)"
              :error="formErrors.registryHost?.[0]"
            >
              <template #default="{ id, error, ariaDescribedby }">
                <AppInput
                  :id="id"
                  v-model="form.registryHost"
                  placeholder="192.168.1.241"
                  :disabled="saveLoading"
                  :error="error"
                  :aria-describedby="ariaDescribedby"
                />
              </template>
            </AppField>

            <!-- Registry Credential (Optional) -->
            <AppField
              label="Registry credential"
              description="Thông tin đăng nhập Docker registry (tuỳ chọn)"
              :error="formErrors.registryCredentialId?.[0]"
            >
              <template #default="{ id, error, ariaDescribedby }">
                <AppSelect
                  :id="id"
                  v-model="form.registryCredentialId"
                  :options="registryCredentialOptions"
                  :disabled="saveLoading"
                  :error="error"
                  :aria-describedby="ariaDescribedby"
                />
              </template>
            </AppField>

            <!-- Deploy Registry Host (Optional) -->
            <AppField
              label="Deploy registry host"
              description="Host registry đặt vào k8s Job spec (tuỳ chọn, để trống dùng Registry host)"
              :error="formErrors.deployRegistryHost?.[0]"
            >
              <template #default="{ id, error, ariaDescribedby }">
                <AppInput
                  :id="id"
                  v-model="form.deployRegistryHost"
                  placeholder="VD: registry.local:5000"
                  :disabled="saveLoading"
                  :error="error"
                  :aria-describedby="ariaDescribedby"
                />
              </template>
            </AppField>
          </div>
        </section>

        <!-- ================================================================= -->
        <!-- (2) Nơi chạy                                                      -->
        <!-- ================================================================= -->
        <section class="p-6 rounded-[8px] border border-border bg-card space-y-6">
          <div class="border-b border-border pb-3">
            <h2 class="text-base font-semibold text-fg">
              2. Nơi chạy
            </h2>
            <p class="text-xs text-muted-fg mt-0.5">
              Cấu hình namespace Kubernetes, container và pull secret (chỉ từ allowlist)
            </p>
          </div>

          <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
            <!-- Namespace (from allowlist kind 'namespace') -->
            <AppField
              label="Namespace"
              :required="true"
              description="Namespace được phép chạy Job (chọn từ allowlist)"
              :error="formErrors.namespace?.[0]"
            >
              <template #default="{ id, error, ariaDescribedby }">
                <AppSelect
                  :id="id"
                  v-model="form.namespace"
                  :options="namespaceOptions"
                  placeholder="-- Chọn Namespace từ allowlist --"
                  :disabled="saveLoading"
                  :error="error"
                  :aria-describedby="ariaDescribedby"
                />
              </template>
            </AppField>

            <!-- Container Name -->
            <AppField
              label="Tên container"
              :required="true"
              description="Tên container trong k8s Pod spec"
              :error="formErrors.containerName?.[0]"
            >
              <template #default="{ id, error, ariaDescribedby }">
                <AppInput
                  :id="id"
                  v-model="form.containerName"
                  placeholder="VD: dbmigrator"
                  :disabled="saveLoading"
                  :error="error"
                  :aria-describedby="ariaDescribedby"
                />
              </template>
            </AppField>

            <!-- Service Account (Optional) -->
            <AppField
              label="Service Account"
              description="ServiceAccount trong namespace (tuỳ chọn)"
              :error="formErrors.serviceAccount?.[0]"
            >
              <template #default="{ id, error, ariaDescribedby }">
                <AppInput
                  :id="id"
                  v-model="form.serviceAccount"
                  placeholder="default"
                  :disabled="saveLoading"
                  :error="error"
                  :aria-describedby="ariaDescribedby"
                />
              </template>
            </AppField>

            <!-- Image Pull Secrets (from allowlist kind 'pullsecret' of that namespace) -->
            <AppField
              label="ImagePullSecrets"
              description="Secret kéo ảnh từ registry (chỉ liệt kê từ allowlist của namespace đã chọn)"
              :error="formErrors.imagePullSecrets?.[0]"
            >
              <template #default="{ id, error, ariaDescribedby }">
                <AllowlistMultiSelect
                  :id="id"
                  v-model="form.imagePullSecrets"
                  :options="currentPullSecretOptions"
                  placeholder="Chọn pullSecret từ allowlist..."
                  empty-text="Chưa có pullSecret nào trong allowlist của namespace này"
                  :disabled="saveLoading || !form.namespace"
                  :error="error"
                  :aria-describedby="ariaDescribedby"
                />
              </template>
            </AppField>
          </div>
        </section>

        <!-- ================================================================= -->
        <!-- (3) Tác vụ                                                        -->
        <!-- ================================================================= -->
        <section class="p-6 rounded-[8px] border border-border bg-card space-y-6">
          <div class="flex items-center justify-between border-b border-border pb-3">
            <div>
              <h2 class="text-base font-semibold text-fg">
                3. Tác vụ
              </h2>
              <p class="text-xs text-muted-fg mt-0.5">
                Danh sách tác vụ chạy tuần tự. Kéo thả hoặc dùng nút lên/xuống để đổi thứ tự.
              </p>
            </div>

            <AppButton
              type="button"
              variant="secondary"
              size="sm"
              :disabled="saveLoading"
              aria-label="Thêm tác vụ mới"
              @click="addTask"
            >
              <Plus class="w-4 h-4 mr-1" aria-hidden="true" />
              <span>Thêm tác vụ</span>
            </AppButton>
          </div>

          <div v-if="formErrors.tasks" class="text-xs text-danger font-medium">
            {{ formErrors.tasks[0] }}
          </div>

          <!-- List of Task Cards -->
          <div class="space-y-4">
            <TaskCard
              v-for="(task, idx) in form.tasks"
              :key="idx"
              :task="task"
              :index="idx"
              :total-tasks="form.tasks.length"
              :config-map-options="currentConfigMapOptions"
              :secret-options="currentSecretOptions"
              :errors="formErrors"
              :disabled="saveLoading"
              @update:task="updateTask(idx, $event)"
              @move-up="moveTaskUp(idx)"
              @move-down="moveTaskDown(idx)"
              @duplicate="duplicateTask(idx)"
              @remove="removeTask(idx)"
              @dragstart="onTaskDragStart(idx, $event)"
              @dragover="onTaskDragOver(idx, $event)"
              @drop="onTaskDrop(idx, $event)"
            />
          </div>

          <div class="pt-2">
            <AppButton
              type="button"
              variant="secondary"
              class="w-full border-dashed"
              :disabled="saveLoading"
              aria-label="Thêm tác vụ"
              @click="addTask"
            >
              <Plus class="w-4 h-4 mr-1.5" aria-hidden="true" />
              <span>Thêm tác vụ</span>
            </AppButton>
          </div>
        </section>

        <!-- ================================================================= -->
        <!-- (4) Xem trước và kiểm tra                                         -->
        <!-- ================================================================= -->
        <section class="p-6 rounded-[8px] border border-border bg-card space-y-6">
          <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 border-b border-border pb-3">
            <div>
              <h2 class="text-base font-semibold text-fg">
                4. Xem trước và kiểm tra
              </h2>
              <p class="text-xs text-muted-fg mt-0.5">
                Kiểm tra tính hợp lệ của cấu hình và xem trước manifest Kubernetes Job YAML
              </p>
            </div>

            <div class="flex items-center gap-2">
              <AppButton
                type="button"
                variant="secondary"
                size="sm"
                :loading="validateLoading"
                :disabled="saveLoading || validateLoading"
                aria-label="Kiểm tra cấu hình"
                @click="handleValidate"
              >
                <CheckCircle2 class="w-3.5 h-3.5 mr-1 text-primary" aria-hidden="true" />
                <span>Kiểm tra cấu hình</span>
              </AppButton>

              <AppButton
                type="button"
                variant="secondary"
                size="sm"
                :loading="previewLoading"
                :disabled="saveLoading || previewLoading"
                aria-label="Xem trước YAML"
                @click="refreshPreview"
              >
                <FileCode class="w-3.5 h-3.5 mr-1 text-info" aria-hidden="true" />
                <span>Xem trước</span>
              </AppButton>
            </div>
          </div>

          <!-- YamlPreview component -->
          <YamlPreview
            :tasks="previewTasks"
            :loading="previewLoading"
            :errors="formErrors"
          />
        </section>

        <!-- Bottom Save Button Bar -->
        <div class="flex items-center justify-end gap-3 pt-4 border-t border-border">
          <AppButton
            type="button"
            variant="secondary"
            :disabled="saveLoading"
            aria-label="Huỷ"
            @click="router.push('/jobs')"
          >
            Huỷ
          </AppButton>

          <AppButton
            type="submit"
            variant="primary"
            :loading="saveLoading"
            :disabled="saveLoading"
            aria-label="Lưu Job"
          >
            <Save class="w-4 h-4 mr-1.5" aria-hidden="true" />
            <span>{{ isEditing ? 'Lưu thay đổi' : 'Tạo Job' }}</span>
          </AppButton>
        </div>
      </form>
    </div>
  </AppLayout>
</template>
