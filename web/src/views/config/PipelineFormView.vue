<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  Workflow,
  ArrowLeft,
  Save,
  GitBranch,
  Hammer,
  Rocket,
  FileQuestion
} from 'lucide-vue-next'
import AppLayout from '../../layouts/AppLayout.vue'
import AppButton from '../../components/AppButton.vue'
import AppInput from '../../components/AppInput.vue'
import AppSelect from '../../components/AppSelect.vue'
import AppField from '../../components/AppField.vue'
import { useConfigPipelinesStore } from '../../stores/configPipelines'
import { useConfigSlavesStore } from '../../stores/configSlaves'
import { useConfigCredentialsStore } from '../../stores/configCredentials'
import { useConfigDeployTargetsStore } from '../../stores/configDeployTargets'
import { useToast } from '../../composables/useToast'
import { useUnsavedChangesGuard } from '../../composables/useUnsavedChangesGuard'
import type { PipelineInput } from '../../api/types'
import { ApiError } from '../../api/client'

const route = useRoute()
const router = useRouter()
const store = useConfigPipelinesStore()
const slaveStore = useConfigSlavesStore()
const credStore = useConfigCredentialsStore()
const deployTargetStore = useConfigDeployTargetsStore()
const toast = useToast()

const pipelineId = computed(() => {
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

function takeSnapshot() {
  initialSnapshot.value = JSON.stringify(form.value)
}

const isDirty = computed(() => {
  if (allowLeave.value || pageLoading.value || notFound.value) return false
  return JSON.stringify(form.value) !== initialSnapshot.value
})

useUnsavedChangesGuard(isDirty)

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

    if (isEditing.value && pipelineId.value && !Number.isNaN(pipelineId.value)) {
      await store.updatePipeline(pipelineId.value, payload)
      toast.success('Cập nhật pipeline thành công')
    } else {
      await store.createPipeline(payload)
      toast.success('Tạo pipeline thành công')
    }
    allowLeave.value = true
    await router.push('/pipelines')
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

onMounted(async () => {
  pageLoading.value = true
  try {
    await Promise.all([
      slaveStore.fetchSlaves().catch(() => {}),
      credStore.fetchCredentials().catch(() => {}),
      deployTargetStore.fetchDeployTargets().catch(() => {})
    ])

    if (isEditing.value) {
      if (pipelineId.value === null || Number.isNaN(pipelineId.value)) {
        notFound.value = true
        return
      }
      await store.fetchPipelines()
      const pipeline = store.pipelines.find((p) => p.id === pipelineId.value)
      if (!pipeline) {
        notFound.value = true
        return
      }
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
    } else {
      form.value.gitCredentialId = gitCredentials.value[0]?.id ?? ''
      form.value.slaveId = slaveStore.slaves[0]?.id ?? ''
      form.value.registryCredentialId = registryCredentials.value[0]?.id ?? ''
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
    <div class="space-y-6 max-w-4xl mx-auto pb-12">
      <!-- Sticky Top Header Bar -->
      <div class="sticky top-0 z-10 bg-bg/95 backdrop-blur-xs -mx-4 sm:-mx-6 px-4 sm:px-6 py-3 flex flex-col sm:flex-row sm:items-center justify-between gap-4 border-b border-border">
        <div class="flex items-center gap-3 min-w-0">
          <button
            type="button"
            aria-label="Quay lại danh sách pipeline"
            class="p-2 rounded-[6px] text-muted-fg hover:text-fg hover:bg-muted transition-colors cursor-pointer focus-visible:outline-2 focus-visible:outline-ring shrink-0"
            @click="router.push('/pipelines')"
          >
            <ArrowLeft class="w-5 h-5" aria-hidden="true" />
          </button>
          <div class="min-w-0">
            <nav aria-label="Breadcrumb" class="text-xs text-muted-fg flex items-center gap-1.5">
              <router-link to="/pipelines" class="hover:text-fg hover:underline">
                Pipeline
              </router-link>
              <span aria-hidden="true">/</span>
              <span class="text-fg font-medium truncate">
                {{ isEditing ? (form.name || 'Sửa') : 'Thêm mới' }}
              </span>
            </nav>
            <h1 class="text-xl font-bold tracking-tight text-fg flex items-center gap-2 mt-0.5 truncate">
              <Workflow class="w-5 h-5 text-primary shrink-0" aria-hidden="true" />
              <span class="truncate">{{ isEditing ? 'Sửa pipeline' : 'Thêm pipeline mới' }}</span>
            </h1>
          </div>
        </div>

        <div v-if="!notFound" class="flex items-center gap-2.5 shrink-0 whitespace-nowrap">
          <AppButton
            type="button"
            variant="secondary"
            :disabled="actionLoading"
            @click="router.push('/pipelines')"
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
            <span>{{ isEditing ? 'Lưu thay đổi' : 'Tạo pipeline' }}</span>
          </AppButton>
        </div>
      </div>

      <!-- Loading -->
      <div v-if="pageLoading" class="p-8 rounded-[8px] border border-border bg-card space-y-4" aria-busy="true">
        <div v-for="i in 5" :key="i" class="h-10 rounded bg-muted/70 animate-pulse" />
      </div>

      <!-- Not Found -->
      <div
        v-else-if="notFound"
        class="p-8 rounded-[8px] border border-border bg-card text-center space-y-4"
      >
        <div class="w-12 h-12 rounded-full bg-muted flex items-center justify-center text-muted-fg mx-auto">
          <FileQuestion class="w-6 h-6" aria-hidden="true" />
        </div>
        <div>
          <h2 class="text-base font-semibold text-fg">Không tìm thấy pipeline</h2>
          <p class="text-sm text-muted-fg mt-1">Pipeline bạn tìm kiếm không tồn tại hoặc đã bị xoá.</p>
        </div>
        <AppButton variant="secondary" @click="router.push('/pipelines')">
          <ArrowLeft class="w-4 h-4" aria-hidden="true" />
          <span>Quay lại danh sách</span>
        </AppButton>
      </div>

      <!-- Form -->
      <form v-else class="space-y-6" @submit.prevent="handleSave">
        <!-- Pipeline General Name -->
        <section class="p-6 rounded-[8px] border border-border bg-card space-y-4">
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
        </section>

        <!-- Group 1: Source (Nguồn) -->
        <section class="p-6 rounded-[8px] border border-border bg-card space-y-4">
          <div class="flex items-center gap-2 text-sm font-semibold text-fg border-b border-border pb-3">
            <GitBranch class="w-4 h-4 text-primary" aria-hidden="true" />
            <span>Nguồn (Git)</span>
          </div>

          <div class="grid grid-cols-1 sm:grid-cols-3 gap-4">
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
                placeholder="Chọn Token Git"
                :disabled="gitCredentials.length === 0"
                :error="error"
                :aria-describedby="ariaDescribedby"
              />
            </template>
          </AppField>
        </section>

        <!-- Group 2: Build -->
        <section class="p-6 rounded-[8px] border border-border bg-card space-y-4">
          <div class="flex items-center gap-2 text-sm font-semibold text-fg border-b border-border pb-3">
            <Hammer class="w-4 h-4 text-primary" aria-hidden="true" />
            <span>Build (Docker)</span>
          </div>

          <AppField
            label="Máy build"
            required
            :error="formErrors.slaveId"
          >
            <template #default="{ id, error, ariaDescribedby }">
              <AppSelect
                :id="id"
                v-model="form.slaveId"
                :options="slaveOptions"
                placeholder="Chọn máy build"
                :disabled="slaveStore.slaves.length === 0"
                :error="error"
                :aria-describedby="ariaDescribedby"
              />
            </template>
          </AppField>

          <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
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

          <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
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
        </section>

        <!-- Group 3: Deploy (Kubernetes) -->
        <section class="p-6 rounded-[8px] border border-border bg-card space-y-4">
          <div class="flex items-center gap-2 text-sm font-semibold text-fg border-b border-border pb-3">
            <Rocket class="w-4 h-4 text-primary" aria-hidden="true" />
            <span>Deploy (Kubernetes)</span>
          </div>

          <div class="grid grid-cols-1 sm:grid-cols-3 gap-4">
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
        </section>
      </form>
    </div>
  </AppLayout>
</template>
