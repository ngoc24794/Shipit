<script setup lang="ts">
import { ref } from 'vue'
import { Sun, Moon, Play, Trash2, Terminal, Inbox } from 'lucide-vue-next'
import AppLayout from '../../layouts/AppLayout.vue'
import { useTheme } from '../../composables/useTheme'
import { useToast } from '../../composables/useToast'
import { useShortcuts } from '../../composables/useShortcuts'
import { useSearchStore } from '../../stores/search'

// Forms
import AppButton from '../../components/AppButton.vue'
import ButtonGroup from '../../components/ButtonGroup.vue'
import SegmentedControl from '../../components/SegmentedControl.vue'
import AppInput from '../../components/AppInput.vue'
import AppTextarea from '../../components/AppTextarea.vue'
import AppSecretInput from '../../components/AppSecretInput.vue'
import AppSelect from '../../components/AppSelect.vue'
import AppCombobox from '../../components/AppCombobox.vue'
import AppCheckbox from '../../components/AppCheckbox.vue'
import AppRadioGroup from '../../components/AppRadioGroup.vue'
import AppRadio from '../../components/AppRadio.vue'
import AppSwitch from '../../components/AppSwitch.vue'
import AppField from '../../components/AppField.vue'
import AppTag from '../../components/AppTag.vue'
import AppKbd from '../../components/AppKbd.vue'
import AppSpinner from '../../components/AppSpinner.vue'

// Data & Navigation
import AppTable from '../../components/AppTable.vue'
import SortableHeader from '../../components/SortableHeader.vue'
import DataToolbar from '../../components/DataToolbar.vue'
import DataPagination from '../../components/DataPagination.vue'
import AppTabs from '../../components/AppTabs.vue'
import AppBreadcrumb from '../../components/AppBreadcrumb.vue'
import FormStepper from '../../components/FormStepper.vue'

// Overlays
import AppDialog from '../../components/AppDialog.vue'
import ConfirmDialog from '../../components/ConfirmDialog.vue'
import AppDrawer from '../../components/AppDrawer.vue'
import AppTooltip from '../../components/AppTooltip.vue'
import AppPopover from '../../components/AppPopover.vue'
import AppDropdownMenu from '../../components/AppDropdownMenu.vue'
import ContextMenu from '../../components/ContextMenu.vue'

// Feedback & Status
import StatusBadge from '../../components/StatusBadge.vue'
import EmptyState from '../../components/EmptyState.vue'
import ErrorState from '../../components/ErrorState.vue'
import AppSkeleton from '../../components/AppSkeleton.vue'
import AppProgressBar from '../../components/AppProgressBar.vue'
import AppBadge from '../../components/AppBadge.vue'

// Shell & Business
import SplitPane from '../../components/SplitPane.vue'
import KeyValueGrid from '../../components/KeyValueGrid.vue'
import CopyButton from '../../components/CopyButton.vue'
import RelativeTime from '../../components/RelativeTime.vue'
import DurationText from '../../components/DurationText.vue'
import LogViewer from '../../components/LogViewer.vue'
import StepTimeline from '../../components/StepTimeline.vue'
import TaskTable from '../../components/run/TaskTable.vue'
import TaskCard from '../../components/job/TaskCard.vue'
import EnvEditor from '../../components/job/EnvEditor.vue'
import ChipInput from '../../components/job/ChipInput.vue'
import AllowlistMultiSelect from '../../components/job/AllowlistMultiSelect.vue'
import YamlPreview from '../../components/job/YamlPreview.vue'
import type { JobEnvVar, JobTaskInput, LogLine, RunStep, RunTask } from '../../api/types'

const { theme, toggleTheme } = useTheme()
const toast = useToast()
const { openOverlay } = useShortcuts()
const searchStore = useSearchStore()

// Global demo state toggles
const demoDisabled = ref(false)
const demoError = ref(false)
const demoLoading = ref(false)
const demoSize = ref<'sm' | 'md'>('md')
const demoDensity = ref<'compact' | 'normal'>('compact')

// Form demo states
const segVal = ref('pipeline')
const inputVal = ref('avina-api')
const textareaVal = ref('Triển khai dịch vụ lên staging')
const secretVal = ref('s3cr3t-token')
const selectVal = ref('main')
const comboVal = ref<string[]>(['avina-api'])
const checkVal = ref(true)
const checkIndeterminate = ref(false)
const radioVal = ref('pipeline')
const switchVal = ref(true)

// Data demo states
const activeTab = ref('overview')
const sortBy = ref<string | null>('name')
const sortDir = ref<'asc' | 'desc'>('asc')
const toolbarSearch = ref('')
const toolbarFilters = ref<Record<string, string>>({ status: '' })
const page = ref(1)
const pageSize = ref(10)
const selectedKeys = ref<Array<string | number>>([1])
const tableActiveIdx = ref(0)
const stepperStep = ref(1)

const sampleRows = [
  { id: 1, name: 'avina-api', branch: 'main', status: 'success' as const },
  { id: 2, name: 'avina-dbmigrator', branch: 'dev', status: 'running' as const },
  { id: 3, name: 'avina-worker', branch: 'main', status: 'failed' as const }
]

// Overlay demo states
const dialogOpen = ref(false)
const confirmOpen = ref(false)
const drawerOpen = ref(false)
const popoverOpen = ref(false)
const menuItems = [
  { id: 'rerun', label: 'Chạy lại', shortcut: 'r' },
  { id: 'logs', label: 'Xem nhật ký', shortcut: 'l' },
  { id: 'sep', separator: true },
  { id: 'delete', label: 'Xoá', danger: true }
]

// Business demo states
const sampleLogs: LogLine[] = [
  { seq: 1, step: 'checkout', stream: 'system', text: 'Bắt đầu checkout nhánh main...', at: '2026-11-20T00:00:00Z' },
  { seq: 2, step: 'build', stream: 'stdout', text: 'Building docker image avina-api:v1.2.0', at: '2026-11-20T00:00:05Z' },
  { seq: 3, step: 'build', stream: 'stderr', text: 'WARNING! Cache miss on layer 3', at: '2026-11-20T00:00:08Z' },
  { seq: 4, step: 'deploy', stream: 'stderr', text: 'ERROR: connection refused to registry', at: '2026-11-20T00:00:12Z' }
]
const sampleSteps: RunStep[] = [
  { name: 'checkout', status: 'success', startedAt: '2026-11-20T00:00:00Z', finishedAt: '2026-11-20T00:00:10Z' },
  { name: 'build', status: 'running', startedAt: '2026-11-20T00:00:10Z', finishedAt: null },
  { name: 'push', status: 'pending', startedAt: null, finishedAt: null },
  { name: 'deploy', status: 'pending', startedAt: null, finishedAt: null }
]
const sampleTasks: RunTask[] = [
  { position: 0, label: 'migrate-files', status: 'success', exitCode: 0, startedAt: '2026-11-20T00:00:00Z', finishedAt: '2026-11-20T00:00:08Z' },
  { position: 1, label: 'migrate-users', status: 'failed', exitCode: 1, startedAt: '2026-11-20T00:00:08Z', finishedAt: '2026-11-20T00:00:15Z' }
]
const sampleJobTask = ref<JobTaskInput>({
  label: 'migrate-files',
  args: ['migrate', '--module', 'files'],
  env: [{ name: 'ASPNETCORE_ENVIRONMENT', value: 'Staging' }],
  envFromConfigMaps: ['avina-files-config'],
  envFromSecrets: ['avina-files-secret'],
  timeoutSeconds: 1800,
  backoffLimit: 1,
  ttlSeconds: 3600
})
const sampleChips = ref(['migrate', '--module', 'files'])
const sampleAllowlist = ref(['avina-files-config'])
const sampleEnv = ref<JobEnvVar[]>([
  { name: 'PORT', value: '8080' },
  { name: 'DB_PASS', secretName: 'avina-files-secret', secretKey: 'password' }
])
const sampleYamlTasks = [
  { position: 0, label: 'migrate-files', yaml: 'apiVersion: batch/v1\nkind: Job\nmetadata:\n  name: avina-migrate-files' }
]

const sections = [
  { id: 'forms', label: 'Biểu mẫu (Forms)', count: 16 },
  { id: 'data', label: 'Bảng & Điều hướng', count: 7 },
  { id: 'overlays', label: 'Lớp phủ (Overlays)', count: 8 },
  { id: 'feedback', label: 'Phản hồi & Trạng thái', count: 7 },
  { id: 'shell', label: 'Khung & Tiện ích', count: 7 },
  { id: 'business', label: 'Nghiệp vụ CI/CD', count: 8 }
]

const activeSection = ref('forms')

function scrollToSection(id: string) {
  activeSection.value = id
  const el = document.getElementById(`gallery-${id}`)
  if (el && typeof el.scrollIntoView === 'function') {
    el.scrollIntoView({ behavior: 'auto', block: 'start' })
  }
}

function handleSort(key: string) {
  if (sortBy.value === key) {
    sortDir.value = sortDir.value === 'asc' ? 'desc' : 'asc'
  } else {
    sortBy.value = key
    sortDir.value = 'asc'
  }
}
</script>

<template>
  <AppLayout>
    <div class="flex-1 min-h-0 flex flex-col gap-3" data-testid="components-gallery">
      <!-- Header & Global Controls -->
      <div class="shrink-0 flex items-center justify-between gap-3 pb-2 border-b border-border flex-nowrap">
        <div class="flex items-center gap-3 min-w-0">
          <h1 class="text-[15px] font-bold text-fg-strong whitespace-nowrap">
            Thư viện thành phần D1 Terminal-native
          </h1>
          <AppBadge variant="phosphor" size="sm">53 thành phần</AppBadge>
        </div>

        <div class="flex items-center gap-2 shrink-0 whitespace-nowrap">
          <AppSwitch v-model="demoDisabled" label="Disabled" />
          <AppSwitch v-model="demoError" label="Error" />
          <AppSwitch v-model="demoLoading" label="Loading" />
          <SegmentedControl
            v-model="demoSize"
            size="sm"
            aria-label="Kích thước demo"
            :options="[{ label: 'sm', value: 'sm' }, { label: 'md', value: 'md' }]"
          />
          <SegmentedControl
            v-model="demoDensity"
            size="sm"
            aria-label="Mật độ bảng"
            :options="[{ label: 'compact', value: 'compact' }, { label: 'normal', value: 'normal' }]"
          />
          <AppButton variant="secondary" size="sm" @click="toggleTheme">
            <Sun v-if="theme === 'dark'" class="w-3.5 h-3.5" aria-hidden="true" />
            <Moon v-else class="w-3.5 h-3.5" aria-hidden="true" />
            <span>{{ theme === 'dark' ? 'Sáng' : 'Tối' }}</span>
          </AppButton>
        </div>
      </div>

      <!-- Body: Left Anchor Index + Internal Scrolling Content -->
      <div class="flex-1 min-h-0 flex flex-row gap-3 overflow-hidden">
        <aside
          aria-label="Mục lục thư viện"
          class="w-[210px] shrink-0 bg-bg-elevated border border-border rounded-[2px] p-2 overflow-y-auto space-y-1"
        >
          <div class="px-2 py-1 text-[10px] font-bold uppercase tracking-wider text-fg-dim">
            Nhóm thành phần
          </div>
          <button
            v-for="sec in sections"
            :key="sec.id"
            type="button"
            :class="[
              'w-full flex items-center justify-between px-2 py-1.5 rounded-[2px] border-l-2 text-xs text-left cursor-pointer transition-colors',
              activeSection === sec.id
                ? 'bg-bg-selected text-phosphor border-phosphor font-semibold'
                : 'border-transparent text-fg-muted hover:text-fg-strong hover:bg-bg-hover'
            ]"
            @click="scrollToSection(sec.id)"
          >
            <span class="truncate">{{ sec.label }}</span>
            <span class="text-[10px] text-fg-dim font-mono">{{ sec.count }}</span>
          </button>
        </aside>

        <div class="flex-1 min-w-0 min-h-0 overflow-y-auto space-y-6 pr-1" data-testid="gallery-scroll">
          <!-- 1. FORMS -->
          <section id="gallery-forms" class="space-y-3">
            <h2 class="text-xs font-bold uppercase tracking-wider text-phosphor border-b border-border pb-1">
              1. Biểu mẫu (Forms)
            </h2>

            <div class="grid grid-cols-2 gap-3">
              <!-- AppButton + ButtonGroup + SegmentedControl -->
              <div class="p-3 bg-bg-elevated border border-border rounded-[2px] space-y-2">
                <div class="text-xs font-bold text-fg-strong">AppButton / ButtonGroup / SegmentedControl</div>
                <div class="flex flex-wrap items-center gap-2">
                  <AppButton variant="primary" :size="demoSize" :disabled="demoDisabled" :loading="demoLoading" kbd="n">
                    <Play class="w-3.5 h-3.5" aria-hidden="true" />
                    <span>Chạy ngay</span>
                  </AppButton>
                  <AppButton variant="secondary" :size="demoSize" :disabled="demoDisabled">Phụ</AppButton>
                  <AppButton variant="ghost" :size="demoSize" :disabled="demoDisabled">Ghost</AppButton>
                  <AppButton variant="danger" :size="demoSize" :disabled="demoDisabled">
                    <Trash2 class="w-3.5 h-3.5" aria-hidden="true" />
                    <span>Xoá</span>
                  </AppButton>
                  <AppButton variant="link" :size="demoSize" :disabled="demoDisabled">Liên kết</AppButton>
                </div>
                <div class="flex flex-wrap items-center gap-3 pt-1">
                  <ButtonGroup aria-label="Nhóm nút">
                    <AppButton variant="secondary" size="sm">Lùi</AppButton>
                    <AppButton variant="secondary" size="sm">Tiếp</AppButton>
                  </ButtonGroup>
                  <SegmentedControl
                    v-model="segVal"
                    size="sm"
                    :disabled="demoDisabled"
                    aria-label="Chọn loại"
                    :options="[{ label: 'Pipeline', value: 'pipeline' }, { label: 'Job', value: 'job' }]"
                  />
                </div>
                <table class="w-full text-[11px] border-t border-border mt-2 pt-1">
                  <thead class="text-fg-dim text-left"><tr><th>Prop</th><th>Type</th><th>Default</th></tr></thead>
                  <tbody class="text-fg-muted font-mono">
                    <tr><td>variant</td><td>primary|secondary|ghost|danger|link</td><td>secondary</td></tr>
                    <tr><td>size / kbd</td><td>sm|md|lg / string</td><td>md / -</td></tr>
                  </tbody>
                </table>
              </div>

              <!-- AppInput, AppTextarea, AppSecretInput, AppField -->
              <div class="p-3 bg-bg-elevated border border-border rounded-[2px] space-y-2">
                <div class="text-xs font-bold text-fg-strong">AppField / AppInput / AppTextarea / AppSecretInput</div>
                <AppField
                  label="Tên dịch vụ"
                  required
                  description="Dùng chữ thường và dấu gạch ngang"
                  :error="demoError ? 'Giá trị không hợp lệ' : ''"
                >
                  <template #default="{ id, error, ariaDescribedby }">
                    <AppInput
                      :id="id"
                      v-model="inputVal"
                      :size="demoSize"
                      :disabled="demoDisabled"
                      :error="error"
                      :aria-describedby="ariaDescribedby"
                      clearable
                    />
                  </template>
                </AppField>
                <AppSecretInput
                  v-model="secretVal"
                  :size="demoSize"
                  :disabled="demoDisabled"
                  :error="demoError"
                />
                <AppTextarea
                  v-model="textareaVal"
                  :rows="2"
                  :disabled="demoDisabled"
                  :error="demoError"
                />
                <table class="w-full text-[11px] border-t border-border pt-1">
                  <thead class="text-fg-dim text-left"><tr><th>Prop</th><th>Type</th><th>Default</th></tr></thead>
                  <tbody class="text-fg-muted font-mono">
                    <tr><td>modelValue</td><td>string | number</td><td>''</td></tr>
                    <tr><td>clearable / error</td><td>boolean / boolean|string</td><td>false</td></tr>
                  </tbody>
                </table>
              </div>

              <!-- AppSelect & AppCombobox -->
              <div class="p-3 bg-bg-elevated border border-border rounded-[2px] space-y-2">
                <div class="text-xs font-bold text-fg-strong">AppSelect &amp; AppCombobox</div>
                <AppSelect
                  v-model="selectVal"
                  :size="demoSize"
                  :disabled="demoDisabled"
                  :error="demoError"
                  aria-label="Chọn nhánh"
                  :options="[
                    { label: 'main', value: 'main' },
                    { label: 'dev', value: 'dev' },
                    { label: 'staging', value: 'staging' }
                  ]"
                />
                <AppCombobox
                  v-model="comboVal"
                  multiple
                  searchable
                  clearable
                  :loading="demoLoading"
                  :disabled="demoDisabled"
                  :error="demoError"
                  placeholder="Chọn dịch vụ..."
                  :options="[
                    { label: 'avina-api', value: 'avina-api' },
                    { label: 'avina-catalog', value: 'avina-catalog' },
                    { label: 'avina-dbmigrator', value: 'avina-dbmigrator' }
                  ]"
                />
                <table class="w-full text-[11px] border-t border-border pt-1">
                  <thead class="text-fg-dim text-left"><tr><th>Prop</th><th>Type</th><th>Default</th></tr></thead>
                  <tbody class="text-fg-muted font-mono">
                    <tr><td>options / multiple</td><td>SelectOption[] / boolean</td><td>[] / false</td></tr>
                    <tr><td>searchable / creatable</td><td>boolean / boolean</td><td>false / false</td></tr>
                  </tbody>
                </table>
              </div>

              <!-- Checkbox, Radio, Switch, Tag, Kbd, Spinner -->
              <div class="p-3 bg-bg-elevated border border-border rounded-[2px] space-y-2">
                <div class="text-xs font-bold text-fg-strong">AppCheckbox / AppRadioGroup / AppSwitch / AppTag / AppKbd / AppSpinner</div>
                <div class="flex flex-wrap items-center gap-4">
                  <AppCheckbox v-model="checkVal" :indeterminate="checkIndeterminate" :disabled="demoDisabled" label="Kích hoạt tự động" />
                  <AppSwitch v-model="switchVal" :disabled="demoDisabled" label="Theo dõi log" />
                </div>
                <AppRadioGroup
                  v-model="radioVal"
                  orientation="horizontal"
                  :disabled="demoDisabled"
                  aria-label="Chế độ"
                >
                  <AppRadio value="pipeline" label="Pipeline" />
                  <AppRadio value="job" label="Job" />
                </AppRadioGroup>
                <div class="flex flex-wrap items-center gap-2 pt-1">
                  <AppTag variant="default">default</AppTag>
                  <AppTag variant="phosphor">phosphor</AppTag>
                  <AppTag variant="accent">accent</AppTag>
                  <AppTag variant="warn">warn</AppTag>
                  <AppTag variant="danger" removable>danger</AppTag>
                  <AppKbd keys="Ctrl+K" />
                  <AppKbd>g r</AppKbd>
                  <AppSpinner size="sm" label="Đang tải" />
                </div>
                <table class="w-full text-[11px] border-t border-border pt-1">
                  <thead class="text-fg-dim text-left"><tr><th>Component</th><th>Key Props</th></tr></thead>
                  <tbody class="text-fg-muted font-mono">
                    <tr><td>AppCheckbox / AppSwitch</td><td>modelValue, indeterminate, disabled, label</td></tr>
                    <tr><td>AppTag / AppKbd / AppSpinner</td><td>variant, removable / keys / size, label</td></tr>
                  </tbody>
                </table>
              </div>
            </div>
          </section>

          <!-- 2. DATA & NAVIGATION -->
          <section id="gallery-data" class="space-y-3">
            <h2 class="text-xs font-bold uppercase tracking-wider text-phosphor border-b border-border pb-1">
              2. Bảng &amp; Điều hướng (Data &amp; Navigation)
            </h2>

            <div class="p-3 bg-bg-elevated border border-border rounded-[2px] space-y-3">
              <div class="flex items-center justify-between gap-3">
                <AppBreadcrumb
                  :items="[
                    { label: 'Vận hành', to: '/' },
                    { label: 'Thư viện thành phần' }
                  ]"
                />
                <FormStepper
                  :steps="[
                    { id: '1', label: 'Thông tin', status: 'visited' },
                    { id: '2', label: 'Mã nguồn', status: 'current' },
                    { id: '3', label: 'Xem trước', status: 'pending' }
                  ]"
                  :current-step="stepperStep"
                  @update:current-step="stepperStep = $event"
                />
              </div>

              <AppTabs
                v-model="activeTab"
                aria-label="Tab mẫu"
                :tabs="[
                  { id: 'overview', label: 'Tổng quan', count: 3 },
                  { id: 'history', label: 'Lịch sử', count: 12 },
                  { id: 'disabled', label: 'Khoá', disabled: true }
                ]"
              />

              <DataToolbar
                v-model:q="toolbarSearch"
                :filter-values="toolbarFilters"
                search-placeholder="Lọc bảng mẫu..."
                :filters="[
                  {
                    key: 'status',
                    label: 'Trạng thái',
                    type: 'select',
                    options: [
                      { label: 'Tất cả', value: '' },
                      { label: 'Thành công', value: 'success' }
                    ]
                  }
                ]"
              />

              <AppTable
                :rows="sampleRows"
                row-key="id"
                selectable
                keyboard-nav
                :density="demoDensity"
                :loading="demoLoading"
                :error="demoError ? 'Không thể tải dữ liệu bảng' : null"
                v-model:selected-keys="selectedKeys"
                v-model:active-index="tableActiveIdx"
              >
                <template #header-columns>
                  <SortableHeader label="Tên" sort-key="name" :sort-by="sortBy" :sort-dir="sortDir" @sort="handleSort" />
                  <th scope="col">Nhánh</th>
                  <th scope="col">Trạng thái</th>
                </template>
                <template #row="{ row }">
                  <td class="font-medium text-fg-strong truncate">{{ row.name }}</td>
                  <td class="font-mono text-fg-muted">{{ row.branch }}</td>
                  <td><StatusBadge :status="row.status" size="sm" /></td>
                </template>
              </AppTable>

              <DataPagination
                v-model:page="page"
                v-model:page-size="pageSize"
                :total="25"
                :total-pages="3"
              />
            </div>
          </section>

          <!-- 3. OVERLAYS -->
          <section id="gallery-overlays" class="space-y-3">
            <h2 class="text-xs font-bold uppercase tracking-wider text-phosphor border-b border-border pb-1">
              3. Lớp phủ (Overlays)
            </h2>

            <div class="p-3 bg-bg-elevated border border-border rounded-[2px] flex flex-wrap items-center gap-2">
              <AppButton variant="secondary" size="sm" @click="dialogOpen = true">Mở AppDialog</AppButton>
              <AppButton variant="danger" size="sm" @click="confirmOpen = true">Mở ConfirmDialog</AppButton>
              <AppButton variant="secondary" size="sm" @click="drawerOpen = true">Mở AppDrawer</AppButton>
              <AppButton variant="secondary" size="sm" @click="openOverlay()">Mở ShortcutsOverlay (?)</AppButton>

              <AppTooltip text="Chú thích phím tắt D1">
                <AppButton variant="ghost" size="sm">Rê chuột (Tooltip)</AppButton>
              </AppTooltip>

              <AppPopover v-model="popoverOpen">
                <template #trigger="{ toggle }">
                  <AppButton variant="secondary" size="sm" @click="toggle">AppPopover</AppButton>
                </template>
                <div class="p-2 text-xs text-fg w-[200px]">Nội dung nổi trong AppPopover</div>
              </AppPopover>

              <AppDropdownMenu
                :items="menuItems"
                label="Menu thao tác"
                @select="(item) => toast.info(`Đã chọn: ${item.label}`)"
              />

              <ContextMenu
                :items="menuItems"
                @select="(item) => toast.info(`Context: ${item.label}`)"
              >
                <div class="px-2.5 py-1 border border-dashed border-border rounded-[2px] text-xs text-fg-muted cursor-context-menu">
                  Nhấn chuột phải (ContextMenu)
                </div>
              </ContextMenu>
            </div>
          </section>

          <!-- 4. FEEDBACK & STATUS -->
          <section id="gallery-feedback" class="space-y-3">
            <h2 class="text-xs font-bold uppercase tracking-wider text-phosphor border-b border-border pb-1">
              4. Phản hồi &amp; Trạng thái (Feedback &amp; Status)
            </h2>

            <div class="grid grid-cols-2 gap-3">
              <div class="p-3 bg-bg-elevated border border-border rounded-[2px] space-y-2">
                <div class="text-xs font-bold text-fg-strong">StatusBadge / AppBadge / AppToast</div>
                <div class="flex flex-wrap items-center gap-1.5">
                  <StatusBadge status="success" size="sm" />
                  <StatusBadge status="running" size="sm" />
                  <StatusBadge status="failed" size="sm" />
                  <StatusBadge status="queued" size="sm" />
                  <StatusBadge status="cancelled" size="sm" />
                  <StatusBadge status="skipped" size="sm" />
                  <AppBadge variant="accent">v1.2</AppBadge>
                </div>
                <div class="flex flex-wrap items-center gap-2 pt-1">
                  <AppButton variant="primary" size="sm" @click="toast.success('Triển khai thành công')">Toast Success</AppButton>
                  <AppButton variant="secondary" size="sm" @click="toast.warn('Cảnh báo bộ nhớ')">Toast Warn</AppButton>
                  <AppButton variant="danger" size="sm" @click="toast.error('Lỗi kết nối cluster')">Toast Error</AppButton>
                  <AppButton variant="ghost" size="sm" @click="toast.openDrawer()">Mở Console Drawer</AppButton>
                </div>
              </div>

              <div class="p-3 bg-bg-elevated border border-border rounded-[2px] space-y-2">
                <div class="text-xs font-bold text-fg-strong">AppProgressBar &amp; AppSkeleton</div>
                <AppProgressBar :value="68" label="Đang build image" show-percent variant="phosphor" />
                <AppProgressBar indeterminate label="Đang chờ máy build" variant="accent" />
                <AppSkeleton variant="rows" :count="2" />
              </div>

              <div class="p-3 bg-bg-elevated border border-border rounded-[2px]">
                <EmptyState
                  title="Chưa có dữ liệu"
                  description="Tạo mục mới để bắt đầu."
                  :icon="Inbox"
                />
              </div>

              <div class="p-3 bg-bg-elevated border border-border rounded-[2px]">
                <ErrorState
                  title="Lỗi tải dữ liệu"
                  message="Không thể kết nối máy chủ API."
                  :retrying="demoLoading"
                  @retry="toast.info('Đang thử lại...')"
                />
              </div>
            </div>
          </section>

          <!-- 5. SHELL & UTILITY -->
          <section id="gallery-shell" class="space-y-3">
            <h2 class="text-xs font-bold uppercase tracking-wider text-phosphor border-b border-border pb-1">
              5. Khung &amp; Tiện ích (Shell &amp; Utility)
            </h2>

            <div class="grid grid-cols-2 gap-3">
              <div class="p-3 bg-bg-elevated border border-border rounded-[2px] space-y-2">
                <div class="text-xs font-bold text-fg-strong">KeyValueGrid / CopyButton / RelativeTime / DurationText</div>
                <KeyValueGrid
                  :items="[
                    { key: 'sha', label: 'Tag SHA', value: '9f4a2c1', copyable: true },
                    { key: 'image', label: 'Image', value: 'registry.local/avina-api:9f4a2c1', copyable: true }
                  ]"
                />
                <div class="flex items-center gap-3 text-xs text-fg-muted pt-1">
                  <CopyButton value="shipit-demo" label="Sao chép nhanh" />
                  <span>Thời gian: <RelativeTime :value="new Date(Date.now() - 120000).toISOString()" :live="false" /></span>
                  <span>Thời lượng: <DurationText started-at="2026-11-20T00:00:00Z" finished-at="2026-11-20T00:01:42Z" /></span>
                </div>
                <div class="pt-1">
                  <AppButton variant="secondary" size="sm" @click="searchStore.open()">
                    <Terminal class="w-3.5 h-3.5" aria-hidden="true" />
                    <span>Mở GlobalSearch (Ctrl+K)</span>
                  </AppButton>
                </div>
              </div>

              <div class="p-3 bg-bg-elevated border border-border rounded-[2px] space-y-2">
                <div class="text-xs font-bold text-fg-strong">SplitPane (kéo hoặc mũi tên trái/phải)</div>
                <div class="h-[130px] flex">
                  <SplitPane :default-size="180" :min-size="120" :max-size="320">
                    <template #left>
                      <div class="p-2 text-xs text-fg-muted">Khung trái (StepTree)</div>
                    </template>
                    <template #right>
                      <div class="p-2 text-xs text-fg-muted">Khung phải (LogViewer)</div>
                    </template>
                  </SplitPane>
                </div>
              </div>
            </div>
          </section>

          <!-- 6. BUSINESS COMPONENTS -->
          <section id="gallery-business" class="space-y-3 pb-4">
            <h2 class="text-xs font-bold uppercase tracking-wider text-phosphor border-b border-border pb-1">
              6. Thành phần nghiệp vụ CI/CD (Business)
            </h2>

            <div class="grid grid-cols-2 gap-3">
              <div class="p-3 bg-bg-elevated border border-border rounded-[2px] space-y-2">
                <div class="text-xs font-bold text-fg-strong">StepTimeline (tmux tree) &amp; TaskTable</div>
                <StepTimeline :steps="sampleSteps" kind="pipeline" orientation="vertical" />
                <div class="h-[120px] flex">
                  <TaskTable :tasks="sampleTasks" :selected-position="1" />
                </div>
              </div>

              <div class="p-3 bg-bg-elevated border border-border rounded-[2px] space-y-2 flex flex-col">
                <div class="text-xs font-bold text-fg-strong">LogViewer</div>
                <div class="h-[240px] flex">
                  <LogViewer :lines="sampleLogs" status="running" :run-id="1" />
                </div>
              </div>
            </div>

            <div class="grid grid-cols-2 gap-3">
              <div class="p-3 bg-bg-elevated border border-border rounded-[2px] space-y-2">
                <div class="text-xs font-bold text-fg-strong">ChipInput / AllowlistMultiSelect / EnvEditor</div>
                <ChipInput v-model="sampleChips" :disabled="demoDisabled" :error="demoError" />
                <AllowlistMultiSelect
                  v-model="sampleAllowlist"
                  :options="['avina-files-config', 'avina-users-config']"
                  :disabled="demoDisabled"
                  :error="demoError"
                />
                <EnvEditor
                  v-model="sampleEnv"
                  :secret-options="['avina-files-secret', 'avina-users-secret']"
                  :disabled="demoDisabled"
                />
              </div>

              <div class="p-3 bg-bg-elevated border border-border rounded-[2px] space-y-2">
                <div class="text-xs font-bold text-fg-strong">YamlPreview</div>
                <YamlPreview :tasks="sampleYamlTasks" :loading="demoLoading" />
              </div>
            </div>

            <TaskCard
              :task="sampleJobTask"
              :index="0"
              :total-tasks="1"
              :config-map-options="['avina-files-config']"
              :secret-options="['avina-files-secret']"
              :disabled="demoDisabled"
              @update:task="sampleJobTask = $event"
            />
          </section>
        </div>
      </div>
    </div>

    <!-- Overlay Demo Instances -->
    <AppDialog v-model="dialogOpen" title="AppDialog mẫu" size="md">
      <p class="text-xs text-fg">Nội dung hộp thoại D1 Terminal-native với focus trap và phím Esc.</p>
      <template #footer>
        <AppButton variant="secondary" size="sm" @click="dialogOpen = false">Đóng</AppButton>
      </template>
    </AppDialog>

    <ConfirmDialog
      v-model="confirmOpen"
      title="Xác nhận thao tác"
      message="Bạn có chắc chắn muốn thực hiện thao tác này?"
      danger
      @confirm="confirmOpen = false"
      @cancel="confirmOpen = false"
    />

    <AppDrawer v-model="drawerOpen" title="AppDrawer mẫu" side="right" size="md">
      <p class="text-xs text-fg">Bảng trượt bên phải trong D1 Terminal-native.</p>
    </AppDrawer>
  </AppLayout>
</template>
