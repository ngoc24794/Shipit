<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  Boxes,
  Plus,
  CheckCircle2,
  FileCode,
  ChevronDown,
  ChevronRight,
  AlertCircle
} from 'lucide-vue-next'
import FormWizardLayout from '../../components/FormWizardLayout.vue'
import type { WizardStep } from '../../components/FormStepper.vue'
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
const showAdvanced = ref(false)
const activeTaskIndex = ref(0)

const STEP_DEFS = [
  { id: 'source', label: 'Nguồn mã' },
  { id: 'build', label: 'Build image' },
  { id: 'runtime', label: 'Nơi chạy' },
  { id: 'tasks', label: 'Tác vụ' },
  { id: 'review', label: 'Xem lại và kiểm tra' }
] as const

const STEP_FIELDS: Record<number, string[]> = {
  0: ['name', 'gitUrl', 'gitBranch', 'gitCredentialId'],
  1: ['slaveId', 'dockerfilePath', 'buildContext', 'imageName', 'registryHost', 'registryCredentialId', 'deployRegistryHost'],
  2: ['namespace', 'containerName', 'serviceAccount', 'imagePullSecrets'],
  3: ['tasks']
}

function parseStepQuery(q: unknown): number {
  const n = Number(q)
  if (Number.isInteger(n) && n >= 1 && n <= STEP_DEFS.length) {
    return n - 1
  }
  return 0
}

const currentStep = ref(parseStepQuery(route.query.step))
const visitedSteps = ref<Set<number>>(new Set([currentStep.value]))
const hasAttemptedSave = ref(false)
const stepErrorFlags = ref<Set<number>>(new Set())

watch(
  () => route.query.step,
  (val) => {
    const parsed = parseStepQuery(val)
    if (parsed !== currentStep.value) {
      currentStep.value = parsed
      visitedSteps.value = new Set([...visitedSteps.value, parsed])
    }
  }
)

function goToStep(index: number) {
  if (index < 0 || index >= STEP_DEFS.length) return
  currentStep.value = index
  visitedSteps.value = new Set([...visitedSteps.value, index])
  router.replace({
    path: route.path,
    query: { ...route.query, step: String(index + 1) }
  })
  if (index === 4) {
    refreshPreview()
  }
}

function canNavigateTo(_index: number): boolean {
  return true
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

const gitCredentialOptions = computed(() => {
  return credStore.credentials
    .filter((c) => c.kind === 'git_token')
    .map((c) => ({ label: c.name, value: c.id }))
})

const registryCredentialOptions = computed(() => {
  return [
    { label: 'Không cần đăng nhập', value: '' },
    ...credStore.credentials
      .filter((c) => c.kind === 'registry')
      .map((c) => ({ label: c.name, value: c.id }))
  ]
})

const slaveOptions = computed(() => {
  return slaveStore.slaves.map((s) => ({ label: `${s.name} (${s.host})`, value: s.id }))
})

const namespaceOptions = computed(() => {
  return allowStore.namespaces.map((ns) => ({ label: ns, value: ns }))
})

const currentPullSecretOptions = computed(() => {
  if (!form.value.namespace) return []
  return allowStore.pullSecretsFor(form.value.namespace)
})

const currentConfigMapOptions = computed(() => {
  if (!form.value.namespace) return []
  return allowStore.configMapsFor(form.value.namespace)
})

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

function validateStep(stepIndex: number): boolean {
  const errors: Record<string, string[]> = { ...formErrors.value }
  for (const field of STEP_FIELDS[stepIndex] || []) {
    delete errors[field]
  }
  if (stepIndex === 3) {
    for (const k of Object.keys(errors)) {
      if (k.startsWith('tasks[')) delete errors[k]
    }
  }

  if (stepIndex === 0) {
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
  } else if (stepIndex === 1) {
    if (!form.value.slaveId) {
      errors.slaveId = ['Vui lòng chọn máy build']
    }
    if (!form.value.imageName.trim()) {
      errors.imageName = ['Vui lòng nhập tên Image']
    }
    if (!form.value.registryHost.trim()) {
      errors.registryHost = ['Vui lòng nhập Host registry']
    }
  } else if (stepIndex === 2) {
    if (!form.value.namespace.trim()) {
      errors.namespace = ['Vui lòng chọn Namespace']
    }
    if (!form.value.containerName.trim()) {
      errors.containerName = ['Vui lòng nhập tên container']
    }
  } else if (stepIndex === 3) {
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
  }

  formErrors.value = errors

  const hasStepError =
    stepIndex === 3
      ? Boolean(errors.tasks) || Object.keys(errors).some((k) => k.startsWith('tasks['))
      : (STEP_FIELDS[stepIndex] || []).some((f) => Boolean(errors[f]))

  const nextErr = new Set(stepErrorFlags.value)
  if (hasStepError) {
    nextErr.add(stepIndex)
  } else {
    nextErr.delete(stepIndex)
  }
  stepErrorFlags.value = nextErr
  return !hasStepError
}

function validateClientSide(): boolean {
  let allValid = true
  for (let i = 0; i < 4; i++) {
    if (!validateStep(i)) allValid = false
  }
  return allValid
}

function handleNext() {
  goToStep(currentStep.value + 1)
}

function handlePrev() {
  goToStep(currentStep.value - 1)
}

const wizardSteps = computed<WizardStep[]>(() => {
  return STEP_DEFS.map((s, idx) => {
    let status: WizardStep['status'] = 'pending'
    if (idx === currentStep.value) status = 'current'
    else if (hasAttemptedSave.value && stepErrorFlags.value.has(idx)) status = 'error'
    else if (visitedSteps.value.has(idx)) status = 'visited'
    return { id: s.id, label: s.label, status }
  })
})

const reviewErrorItems = computed(() => {
  const items: Array<{ stepIndex: number; stepLabel: string; field: string; message: string }> = []
  for (let i = 0; i < 4; i++) {
    for (const field of STEP_FIELDS[i]) {
      const msgs = formErrors.value[field]
      if (msgs && msgs.length > 0) {
        items.push({
          stepIndex: i,
          stepLabel: STEP_DEFS[i].label,
          field,
          message: msgs[0]
        })
      }
    }
  }
  for (const [key, msgs] of Object.entries(formErrors.value)) {
    if (key.startsWith('tasks[') && msgs && msgs.length > 0) {
      items.push({
        stepIndex: 3,
        stepLabel: STEP_DEFS[3].label,
        field: key,
        message: msgs[0]
      })
    }
  }
  return items
})

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
  activeTaskIndex.value = form.value.tasks.length - 1
}

function updateTask(index: number, updated: JobTaskInput) {
  form.value.tasks[index] = updated
}

function removeTask(index: number) {
  if (form.value.tasks.length > 1) {
    form.value.tasks.splice(index, 1)
    if (activeTaskIndex.value >= form.value.tasks.length) {
      activeTaskIndex.value = form.value.tasks.length - 1
    }
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
  activeTaskIndex.value = index + 1
}

function moveTaskUp(index: number) {
  if (index > 0) {
    const item = form.value.tasks.splice(index, 1)[0]
    form.value.tasks.splice(index - 1, 0, item)
    activeTaskIndex.value = index - 1
  }
}

function moveTaskDown(index: number) {
  if (index < form.value.tasks.length - 1) {
    const item = form.value.tasks.splice(index, 1)[0]
    form.value.tasks.splice(index + 1, 0, item)
    activeTaskIndex.value = index + 1
  }
}

let draggedTaskIndex: number | null = null

function onTaskDragStart(index: number, event: DragEvent) {
  draggedTaskIndex = index
  event.dataTransfer?.setData('text/plain', String(index))
}

function onTaskDragOver(_index: number, event: DragEvent) {
  event.preventDefault()
}

function onTaskDrop(index: number, event: DragEvent) {
  event.preventDefault()
  if (draggedTaskIndex === null || draggedTaskIndex === index) return
  const item = form.value.tasks.splice(draggedTaskIndex, 1)[0]
  form.value.tasks.splice(index, 0, item)
  activeTaskIndex.value = index
  draggedTaskIndex = null
}

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

async function refreshPreview() {
  previewLoading.value = true
  try {
    const payload = buildPayload()
    const res = await jobsStore.previewJob(payload)
    previewTasks.value = res.tasks
  } catch {
    // Keep previous preview if partial
  } finally {
    previewLoading.value = false
  }
}

async function handleSave() {
  hasAttemptedSave.value = true
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
      const nextErr = new Set(stepErrorFlags.value)
      for (let i = 0; i < 4; i++) {
        if (STEP_FIELDS[i].some((f) => Boolean(formErrors.value[f]))) {
          nextErr.add(i)
        }
      }
      if (Object.keys(formErrors.value).some((k) => k.startsWith('tasks['))) {
        nextErr.add(3)
      }
      stepErrorFlags.value = nextErr
      toast.error('Dữ liệu không hợp lệ. Vui lòng kiểm tra lại các ô bị đánh dấu.')
    } else if (err instanceof ApiError && err.status === 409) {
      formErrors.value = { name: ['Tên Job đã tồn tại'] }
      stepErrorFlags.value = new Set([...stepErrorFlags.value, 0])
      toast.error(err.problem?.detail || err.message || 'Tên Job đã tồn tại')
    } else {
      toast.error(err.message || 'Lưu Job thất bại')
    }
  } finally {
    saveLoading.value = false
  }
}

function findOptionLabel(options: Array<{ label: string; value: string | number }>, val: string | number | null): string {
  if (val === '' || val === null || val === undefined) return 'Không có'
  return options.find((o) => o.value === val)?.label || String(val)
}
</script>

<template>
  <FormWizardLayout
    :title="isEditing ? `Cấu hình Job: ${form.name}` : 'Tạo Job mới'"
    back-to="/jobs"
    breadcrumb-parent="Job"
    :breadcrumb-current="isEditing ? (form.name || 'Sửa') : 'Tạo mới'"
    :steps="wizardSteps"
    :current-step="currentStep"
    :can-navigate-to="canNavigateTo"
    :save-label="isEditing ? 'Lưu thay đổi' : 'Tạo Job'"
    :saving="saveLoading"
    :loading="pageLoading"
    @cancel="router.push('/jobs')"
    @prev="handlePrev"
    @next="handleNext"
    @save="handleSave"
    @select-step="goToStep"
  >
    <template #icon>
      <Boxes class="w-5 h-5 text-primary shrink-0" aria-hidden="true" />
    </template>

    <form class="space-y-6" @submit.prevent="handleSave">
      <!-- Step 1: Nguồn mã -->
      <section v-if="currentStep === 0" class="p-6 rounded-[8px] border border-border bg-card space-y-5">
        <div class="border-b border-border pb-3">
          <h2 class="text-base font-semibold text-fg">1. Nguồn mã</h2>
          <p class="text-xs text-muted-fg mt-0.5">Tên Job và thông tin kho mã nguồn Git</p>
        </div>

        <div class="grid grid-cols-2 gap-4">
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

          <AppField
            label="Token Git"
            :required="true"
            description="Token truy cập Git (chỉ lọc credential loại Git Token)"
            :error="formErrors.gitCredentialId?.[0]"
          >
            <template #default="{ id, error, ariaDescribedby }">
              <AppSelect
                :id="id"
                v-model="form.gitCredentialId"
                :options="gitCredentialOptions"
                placeholder="Chọn Token Git"
                :disabled="saveLoading"
                :error="error"
                :aria-describedby="ariaDescribedby"
              />
            </template>
          </AppField>
        </div>
      </section>

      <!-- Step 2: Build image -->
      <section v-else-if="currentStep === 1" class="space-y-4">
        <div class="p-6 rounded-[8px] border border-border bg-card space-y-5">
          <div class="border-b border-border pb-3">
            <h2 class="text-base font-semibold text-fg">2. Build image</h2>
            <p class="text-xs text-muted-fg mt-0.5">Máy build, Dockerfile và registry lưu Docker image</p>
          </div>

          <div class="grid grid-cols-2 gap-4">
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
                  placeholder="Chọn máy build"
                  :disabled="saveLoading"
                  :error="error"
                  :aria-describedby="ariaDescribedby"
                />
              </template>
            </AppField>

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
          </div>
        </div>

        <!-- Collapsible Advanced Build Options -->
        <div class="rounded-[8px] border border-border bg-card overflow-hidden">
          <button
            type="button"
            :aria-expanded="showAdvanced"
            class="w-full flex items-center justify-between px-6 py-3.5 text-left hover:bg-muted/40 transition-colors cursor-pointer focus-visible:outline-2 focus-visible:outline-ring"
            @click="showAdvanced = !showAdvanced"
          >
            <div>
              <h3 class="text-sm font-semibold text-fg">Tuỳ chọn nâng cao</h3>
              <p class="text-xs text-muted-fg mt-0.5">Build context và Deploy registry host</p>
            </div>
            <ChevronDown v-if="showAdvanced" class="w-4 h-4 text-muted-fg shrink-0" aria-hidden="true" />
            <ChevronRight v-else class="w-4 h-4 text-muted-fg shrink-0" aria-hidden="true" />
          </button>

          <div v-show="showAdvanced" class="px-6 pb-5 pt-3 border-t border-border grid grid-cols-2 gap-4">
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
        </div>
      </section>

      <!-- Step 3: Nơi chạy -->
      <section v-else-if="currentStep === 2" class="p-6 rounded-[8px] border border-border bg-card space-y-5">
        <div class="border-b border-border pb-3">
          <h2 class="text-base font-semibold text-fg">3. Nơi chạy</h2>
          <p class="text-xs text-muted-fg mt-0.5">Cấu hình namespace Kubernetes, container và pull secret (chỉ từ allowlist)</p>
        </div>

        <div class="grid grid-cols-2 gap-4">
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
                placeholder="Chọn Namespace"
                :disabled="saveLoading"
                :error="error"
                :aria-describedby="ariaDescribedby"
              />
            </template>
          </AppField>

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
        </div>
      </section>

      <!-- Step 4: Tác vụ (chỉ sửa một tác vụ một lần) -->
      <section v-else-if="currentStep === 3" class="space-y-4">
        <div class="flex items-center justify-between gap-3">
          <div>
            <h2 class="text-base font-semibold text-fg">4. Tác vụ ({{ form.tasks.length }})</h2>
            <p class="text-xs text-muted-fg mt-0.5">Chọn một tác vụ bên trái để chỉnh sửa chi tiết.</p>
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

        <div class="grid grid-cols-12 gap-4 items-start">
          <!-- Task Selector List -->
          <div class="col-span-4 rounded-[8px] border border-border bg-card p-2 space-y-1">
            <button
              v-for="(task, idx) in form.tasks"
              :key="idx"
              type="button"
              :class="[
                'w-full flex items-center justify-between gap-2 px-3 py-2 rounded-[6px] text-left text-xs transition-colors cursor-pointer',
                'focus-visible:outline-2 focus-visible:outline-ring',
                activeTaskIndex === idx
                  ? 'bg-primary/10 text-primary font-semibold'
                  : 'text-fg hover:bg-muted'
              ]"
              @click="activeTaskIndex = idx"
            >
              <span class="truncate font-mono">#{{ idx + 1 }} {{ task.label || '(chưa đặt tên)' }}</span>
              <span
                v-if="formErrors[`tasks[${idx}].label`] || formErrors[`tasks[${idx}].timeoutSeconds`]"
                class="w-2 h-2 rounded-full bg-danger shrink-0"
                title="Tác vụ có lỗi"
              />
            </button>
          </div>

          <!-- Active Task Editor -->
          <div class="col-span-8">
            <TaskCard
              v-if="form.tasks[activeTaskIndex]"
              :key="activeTaskIndex"
              :task="form.tasks[activeTaskIndex]"
              :index="activeTaskIndex"
              :total-tasks="form.tasks.length"
              :config-map-options="currentConfigMapOptions"
              :secret-options="currentSecretOptions"
              :errors="formErrors"
              :disabled="saveLoading"
              @update:task="updateTask(activeTaskIndex, $event)"
              @move-up="moveTaskUp(activeTaskIndex)"
              @move-down="moveTaskDown(activeTaskIndex)"
              @duplicate="duplicateTask(activeTaskIndex)"
              @remove="removeTask(activeTaskIndex)"
              @dragstart="onTaskDragStart(activeTaskIndex, $event)"
              @dragover="onTaskDragOver(activeTaskIndex, $event)"
              @drop="onTaskDrop(activeTaskIndex, $event)"
            />
          </div>
        </div>
      </section>

      <!-- Step 5: Xem lại và kiểm tra -->
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

        <!-- Summary Cards -->
        <div class="grid grid-cols-2 gap-4">
          <div class="p-4 rounded-[8px] border border-border bg-card space-y-1.5 text-xs">
            <div class="flex items-center justify-between">
              <h3 class="font-semibold text-fg">1. Nguồn mã</h3>
              <button type="button" class="text-primary hover:underline cursor-pointer" @click="goToStep(0)">Sửa</button>
            </div>
            <div class="text-muted-fg">Tên: <span class="text-fg font-medium">{{ form.name || '—' }}</span></div>
            <div class="text-muted-fg truncate">Git: <span class="text-fg font-mono">{{ form.gitUrl || '—' }}@{{ form.gitBranch || 'main' }}</span></div>
            <div class="text-muted-fg">Token Git: <span class="text-fg">{{ findOptionLabel(gitCredentialOptions, form.gitCredentialId) }}</span></div>
          </div>

          <div class="p-4 rounded-[8px] border border-border bg-card space-y-1.5 text-xs">
            <div class="flex items-center justify-between">
              <h3 class="font-semibold text-fg">2. Build image & 3. Nơi chạy</h3>
              <button type="button" class="text-primary hover:underline cursor-pointer" @click="goToStep(1)">Sửa</button>
            </div>
            <div class="text-muted-fg truncate">Image: <span class="text-fg font-mono">{{ form.registryHost }}/{{ form.imageName }}</span></div>
            <div class="text-muted-fg">Máy build: <span class="text-fg">{{ findOptionLabel(slaveOptions, form.slaveId) }}</span></div>
            <div class="text-muted-fg">Namespace / Container: <span class="text-fg font-mono">{{ form.namespace }} / {{ form.containerName }}</span></div>
          </div>
        </div>

        <!-- Validate & Preview Toolbar -->
        <div class="p-4 rounded-[8px] border border-border bg-card space-y-4">
          <div class="flex flex-row items-center justify-between gap-3 flex-nowrap">
            <div>
              <h2 class="text-sm font-semibold text-fg">Kiểm tra & Xem trước YAML</h2>
              <p class="text-xs text-muted-fg mt-0.5">Kiểm tra tính hợp lệ và xem trước manifest Kubernetes Job YAML.</p>
            </div>

            <div class="flex items-center gap-2 shrink-0">
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

          <YamlPreview
            :tasks="previewTasks"
            :loading="previewLoading"
            :errors="formErrors"
          />
        </div>
      </section>
    </form>
  </FormWizardLayout>
</template>
