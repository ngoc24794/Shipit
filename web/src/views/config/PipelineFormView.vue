<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  Workflow,
  GitBranch,
  Hammer,
  Package,
  Rocket,
  AlertCircle,
  CheckCircle2
} from 'lucide-vue-next'
import FormWizardLayout from '../../components/FormWizardLayout.vue'
import type { WizardStep } from '../../components/FormStepper.vue'
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

const STEP_DEFS = [
  { id: 'source', label: 'Nguồn mã' },
  { id: 'build', label: 'Build' },
  { id: 'registry', label: 'Registry' },
  { id: 'deploy', label: 'Deploy' },
  { id: 'review', label: 'Xem lại' }
] as const

const STEP_FIELDS: Record<number, string[]> = {
  0: ['name', 'gitUrl', 'gitBranch', 'gitCredentialId'],
  1: ['slaveId', 'dockerfilePath', 'buildContext'],
  2: ['imageName', 'registryHost', 'registryCredentialId', 'deployRegistryHost'],
  3: ['deployNamespace', 'deployDeployment', 'deployContainer']
}

function parseStepQuery(q: unknown): number {
  const n = Number(q)
  if (Number.isInteger(n) && n >= 1 && n <= STEP_DEFS.length) {
    return n - 1
  }
  return 0
}

const currentStep = ref(parseStepQuery(route.query.step))
const highestVisitedStep = ref(currentStep.value)
const completedSteps = ref<Set<number>>(new Set())
const stepErrorFlags = ref<Set<number>>(new Set())

watch(
  () => route.query.step,
  (val) => {
    const parsed = parseStepQuery(val)
    if (parsed !== currentStep.value) {
      currentStep.value = parsed
      if (parsed > highestVisitedStep.value) {
        highestVisitedStep.value = parsed
      }
    }
  }
)

function goToStep(index: number) {
  if (index < 0 || index >= STEP_DEFS.length) return
  currentStep.value = index
  if (index > highestVisitedStep.value) {
    highestVisitedStep.value = index
  }
  router.replace({
    path: route.path,
    query: { ...route.query, step: String(index + 1) }
  })
}

function canNavigateTo(index: number): boolean {
  if (isEditing.value) return true
  return index <= highestVisitedStep.value
}

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

const gitCredentials = computed(() => credStore.credentials.filter((c) => c.kind === 'git_token'))
const gitCredentialOptions = computed(() => gitCredentials.value.map((c) => ({ label: c.name, value: c.id })))

const registryCredentials = computed(() => credStore.credentials.filter((c) => c.kind === 'registry'))
const registryCredentialOptions = computed(() => [
  { label: 'Không cần đăng nhập', value: '' },
  ...registryCredentials.value.map((c) => ({
    label: c.username ? `${c.name} (${c.username})` : c.name,
    value: c.id
  }))
])

const slaveOptions = computed(() =>
  slaveStore.slaves.map((s) => ({
    label: `${s.name} (${s.host})`,
    value: s.id
  }))
)

const allowedNamespaces = computed(() =>
  [...new Set(deployTargetStore.deployTargets.map((t) => t.namespace))].sort()
)

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

function validateStep(stepIndex: number): boolean {
  const errors = { ...formErrors.value }
  for (const field of STEP_FIELDS[stepIndex] || []) {
    delete errors[field]
  }

  if (stepIndex === 0) {
    if (!form.value.name.trim()) {
      errors.name = 'Vui lòng nhập tên pipeline'
    }
    if (!form.value.gitUrl.trim()) {
      errors.gitUrl = 'Vui lòng nhập địa chỉ Git URL'
    } else if (!/^https?:\/\/.+/.test(form.value.gitUrl.trim())) {
      errors.gitUrl = 'Git URL phải bắt đầu bằng http:// hoặc https://'
    }
    if (!form.value.gitCredentialId) {
      errors.gitCredentialId = 'Vui lòng chọn Token Git'
    }
  } else if (stepIndex === 1) {
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
  } else if (stepIndex === 2) {
    if (!form.value.imageName.trim()) {
      errors.imageName = 'Vui lòng nhập tên Image'
    } else if (!IMAGE_NAME_REGEX.test(form.value.imageName.trim())) {
      errors.imageName = 'Tên image chỉ chứa chữ thường, số và ký tự . _ - /'
    }
    if (!form.value.registryHost.trim()) {
      errors.registryHost = 'Vui lòng nhập địa chỉ máy chủ Registry'
    } else {
      const hostErr = validateHost(form.value.registryHost)
      if (hostErr) errors.registryHost = hostErr
    }
    const deployHost = form.value.deployRegistryHost.trim()
    if (deployHost) {
      const hostErr = validateHost(deployHost)
      if (hostErr) errors.deployRegistryHost = hostErr
    }
  } else if (stepIndex === 3) {
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
  }

  formErrors.value = errors
  const hasError = (STEP_FIELDS[stepIndex] || []).some((f) => Boolean(errors[f]))
  const nextErrors = new Set(stepErrorFlags.value)
  const nextCompleted = new Set(completedSteps.value)
  if (hasError) {
    nextErrors.add(stepIndex)
    nextCompleted.delete(stepIndex)
  } else {
    nextErrors.delete(stepIndex)
    nextCompleted.add(stepIndex)
  }
  stepErrorFlags.value = nextErrors
  completedSteps.value = nextCompleted
  return !hasError
}

function validateForm(): boolean {
  let allValid = true
  for (let i = 0; i < 4; i++) {
    if (!validateStep(i)) allValid = false
  }
  return allValid
}

function handleNext() {
  if (!validateStep(currentStep.value)) return
  goToStep(currentStep.value + 1)
}

function handlePrev() {
  goToStep(currentStep.value - 1)
}

const wizardSteps = computed<WizardStep[]>(() => {
  return STEP_DEFS.map((s, idx) => {
    let status: WizardStep['status'] = 'pending'
    if (idx === currentStep.value) status = 'current'
    else if (stepErrorFlags.value.has(idx)) status = 'error'
    else if (completedSteps.value.has(idx)) status = 'complete'
    return { id: s.id, label: s.label, status }
  })
})

const reviewErrorItems = computed(() => {
  const items: Array<{ stepIndex: number; stepLabel: string; field: string; message: string }> = []
  for (let i = 0; i < 4; i++) {
    for (const field of STEP_FIELDS[i]) {
      const msg = formErrors.value[field]
      if (msg) {
        items.push({
          stepIndex: i,
          stepLabel: STEP_DEFS[i].label,
          field,
          message: msg
        })
      }
    }
  }
  return items
})

function findLabel(options: Array<{ label: string; value: string | number }>, val: string | number | null): string {
  if (val === '' || val === null || val === undefined) return 'Không có'
  return options.find((o) => o.value === val)?.label || String(val)
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
      const nextErr = new Set(stepErrorFlags.value)
      for (let i = 0; i < 4; i++) {
        if (STEP_FIELDS[i].some((f) => Boolean(formErrors.value[f]))) {
          nextErr.add(i)
        }
      }
      stepErrorFlags.value = nextErr
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
      deployTargetStore.fetchDeployTargets().catch(() => {}),
      isEditing.value ? store.fetchPipelines() : Promise.resolve()
    ])

    if (isEditing.value) {
      if (pipelineId.value === null || Number.isNaN(pipelineId.value)) {
        notFound.value = true
        return
      }
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
      highestVisitedStep.value = STEP_DEFS.length - 1
      completedSteps.value = new Set([0, 1, 2, 3])
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
  <FormWizardLayout
    :title="isEditing ? 'Sửa pipeline' : 'Thêm pipeline mới'"
    back-to="/pipelines"
    breadcrumb-parent="Pipeline"
    :breadcrumb-current="isEditing ? (form.name || 'Sửa') : 'Thêm mới'"
    :steps="wizardSteps"
    :current-step="currentStep"
    :can-navigate-to="canNavigateTo"
    :save-label="isEditing ? 'Lưu thay đổi' : 'Tạo pipeline'"
    :saving="actionLoading"
    :loading="pageLoading"
    :not-found="notFound"
    not-found-title="Không tìm thấy pipeline"
    not-found-description="Pipeline bạn tìm kiếm không tồn tại hoặc đã bị xoá."
    @cancel="router.push('/pipelines')"
    @prev="handlePrev"
    @next="handleNext"
    @save="handleSave"
    @select-step="goToStep"
  >
    <template #icon>
      <Workflow class="w-5 h-5 text-primary shrink-0" aria-hidden="true" />
    </template>

    <form class="space-y-6" @submit.prevent="handleSave">
      <!-- Step 1: Nguồn mã -->
      <section v-if="currentStep === 0" class="p-6 rounded-[8px] border border-border bg-card space-y-5">
        <div class="flex items-center gap-2 text-base font-semibold text-fg border-b border-border pb-3">
          <GitBranch class="w-4 h-4 text-primary" aria-hidden="true" />
          <span>1. Nguồn mã</span>
        </div>

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

        <div class="grid grid-cols-1 sm:grid-cols-3 gap-4">
          <div class="sm:col-span-2">
            <AppField label="Địa chỉ Git URL" required :error="formErrors.gitUrl">
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
            <AppField label="Nhánh (Branch)" required :error="formErrors.gitBranch">
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

      <!-- Step 2: Build -->
      <section v-else-if="currentStep === 1" class="p-6 rounded-[8px] border border-border bg-card space-y-5">
        <div class="flex items-center gap-2 text-base font-semibold text-fg border-b border-border pb-3">
          <Hammer class="w-4 h-4 text-primary" aria-hidden="true" />
          <span>2. Build (Docker)</span>
        </div>

        <AppField label="Máy build" required :error="formErrors.slaveId">
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
          <AppField label="Đường dẫn Dockerfile" required :error="formErrors.dockerfilePath">
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
      </section>

      <!-- Step 3: Registry -->
      <section v-else-if="currentStep === 2" class="p-6 rounded-[8px] border border-border bg-card space-y-5">
        <div class="flex items-center gap-2 text-base font-semibold text-fg border-b border-border pb-3">
          <Package class="w-4 h-4 text-primary" aria-hidden="true" />
          <span>3. Registry</span>
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

          <AppField label="Máy chủ Registry" required :error="formErrors.registryHost">
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

      <!-- Step 4: Deploy -->
      <section v-else-if="currentStep === 3" class="p-6 rounded-[8px] border border-border bg-card space-y-5">
        <div class="flex items-center gap-2 text-base font-semibold text-fg border-b border-border pb-3">
          <Rocket class="w-4 h-4 text-primary" aria-hidden="true" />
          <span>4. Deploy (Kubernetes)</span>
        </div>

        <div class="grid grid-cols-1 sm:grid-cols-3 gap-4">
          <AppField label="Namespace" required :error="formErrors.deployNamespace">
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

          <AppField label="Deployment" required :error="formErrors.deployDeployment">
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

          <AppField label="Container" required :error="formErrors.deployContainer">
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
      </section>

      <!-- Step 5: Xem lại -->
      <section v-else class="space-y-4">
        <div
          v-if="reviewErrorItems.length > 0"
          role="alert"
          class="p-4 rounded-[8px] border border-danger/40 border-l-4 border-l-danger bg-danger/10 text-sm space-y-2"
        >
          <div class="flex items-center gap-2 font-semibold text-danger">
            <AlertCircle class="w-4 h-4 shrink-0" aria-hidden="true" />
            <span>Còn {{ reviewErrorItems.length }} lỗi cần sửa trước khi lưu</span>
          </div>
          <ul class="space-y-1 pl-6 list-disc text-xs text-fg">
            <li v-for="(errItem, i) in reviewErrorItems" :key="i">
              <button
                type="button"
                class="font-semibold text-primary hover:underline cursor-pointer mr-1.5"
                @click="goToStep(errItem.stepIndex)"
              >
                {{ errItem.stepLabel }}:
              </button>
              <span>{{ errItem.message }}</span>
            </li>
          </ul>
        </div>

        <div
          v-else
          class="p-3.5 rounded-[8px] border border-success/30 bg-success/10 text-success text-xs font-medium flex items-center gap-2"
        >
          <CheckCircle2 class="w-4 h-4 shrink-0" aria-hidden="true" />
          <span>Thông tin các bước đã đầy đủ. Kiểm tra lại trước khi lưu.</span>
        </div>

        <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
          <div class="p-4 rounded-[8px] border border-border bg-card space-y-2">
            <div class="flex items-center justify-between">
              <h2 class="text-sm font-semibold text-fg">1. Nguồn mã</h2>
              <button type="button" class="text-xs text-primary hover:underline cursor-pointer" @click="goToStep(0)">Sửa</button>
            </div>
            <dl class="text-xs space-y-1">
              <div class="flex justify-between gap-2"><dt class="text-muted-fg">Tên:</dt><dd class="font-medium text-fg truncate">{{ form.name || '—' }}</dd></div>
              <div class="flex justify-between gap-2"><dt class="text-muted-fg">Git URL:</dt><dd class="font-mono text-fg truncate">{{ form.gitUrl || '—' }}</dd></div>
              <div class="flex justify-between gap-2"><dt class="text-muted-fg">Nhánh:</dt><dd class="font-mono text-fg">{{ form.gitBranch || 'main' }}</dd></div>
              <div class="flex justify-between gap-2"><dt class="text-muted-fg">Token Git:</dt><dd class="text-fg">{{ findLabel(gitCredentialOptions, form.gitCredentialId) }}</dd></div>
            </dl>
          </div>

          <div class="p-4 rounded-[8px] border border-border bg-card space-y-2">
            <div class="flex items-center justify-between">
              <h2 class="text-sm font-semibold text-fg">2. Build</h2>
              <button type="button" class="text-xs text-primary hover:underline cursor-pointer" @click="goToStep(1)">Sửa</button>
            </div>
            <dl class="text-xs space-y-1">
              <div class="flex justify-between gap-2"><dt class="text-muted-fg">Máy build:</dt><dd class="text-fg truncate">{{ findLabel(slaveOptions, form.slaveId) }}</dd></div>
              <div class="flex justify-between gap-2"><dt class="text-muted-fg">Dockerfile:</dt><dd class="font-mono text-fg">{{ form.dockerfilePath || 'Dockerfile' }}</dd></div>
              <div class="flex justify-between gap-2"><dt class="text-muted-fg">Context:</dt><dd class="font-mono text-fg">{{ form.buildContext || '(thư mục Dockerfile)' }}</dd></div>
            </dl>
          </div>

          <div class="p-4 rounded-[8px] border border-border bg-card space-y-2">
            <div class="flex items-center justify-between">
              <h2 class="text-sm font-semibold text-fg">3. Registry</h2>
              <button type="button" class="text-xs text-primary hover:underline cursor-pointer" @click="goToStep(2)">Sửa</button>
            </div>
            <dl class="text-xs space-y-1">
              <div class="flex justify-between gap-2"><dt class="text-muted-fg">Image:</dt><dd class="font-mono text-fg truncate">{{ form.registryHost }}/{{ form.imageName }}</dd></div>
              <div class="flex justify-between gap-2"><dt class="text-muted-fg">Xác thực:</dt><dd class="text-fg truncate">{{ findLabel(registryCredentialOptions, form.registryCredentialId) }}</dd></div>
              <div class="flex justify-between gap-2"><dt class="text-muted-fg">Deploy host:</dt><dd class="font-mono text-fg">{{ form.deployRegistryHost || '(dùng Registry host)' }}</dd></div>
            </dl>
          </div>

          <div class="p-4 rounded-[8px] border border-border bg-card space-y-2">
            <div class="flex items-center justify-between">
              <h2 class="text-sm font-semibold text-fg">4. Deploy</h2>
              <button type="button" class="text-xs text-primary hover:underline cursor-pointer" @click="goToStep(3)">Sửa</button>
            </div>
            <dl class="text-xs space-y-1">
              <div class="flex justify-between gap-2"><dt class="text-muted-fg">Namespace:</dt><dd class="font-mono text-fg">{{ form.deployNamespace || '—' }}</dd></div>
              <div class="flex justify-between gap-2"><dt class="text-muted-fg">Deployment:</dt><dd class="font-mono text-fg">{{ form.deployDeployment || '—' }}</dd></div>
              <div class="flex justify-between gap-2"><dt class="text-muted-fg">Container:</dt><dd class="font-mono text-fg">{{ form.deployContainer || '—' }}</dd></div>
            </dl>
          </div>
        </div>
      </section>
    </form>
  </FormWizardLayout>
</template>
