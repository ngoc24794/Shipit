<script setup lang="ts">
import { computed } from 'vue'
import {
  GripVertical,
  ArrowUp,
  ArrowDown,
  Copy,
  Trash2
} from 'lucide-vue-next'
import type { JobTaskInput, JobEnvVar } from '../../api/types'
import AppField from '../AppField.vue'
import AppInput from '../AppInput.vue'
import ChipInput from './ChipInput.vue'
import AllowlistMultiSelect from './AllowlistMultiSelect.vue'
import EnvEditor from './EnvEditor.vue'

const props = withDefaults(
  defineProps<{
    task: JobTaskInput
    index: number
    totalTasks: number
    configMapOptions: string[]
    secretOptions: string[]
    errors?: Record<string, string[]>
    disabled?: boolean
  }>(),
  {
    errors: () => ({}),
    disabled: false
  }
)

const emit = defineEmits<{
  (e: 'update:task', value: JobTaskInput): void
  (e: 'move-up'): void
  (e: 'move-down'): void
  (e: 'duplicate'): void
  (e: 'remove'): void
  (e: 'dragstart', event: DragEvent): void
  (e: 'dragover', event: DragEvent): void
  (e: 'drop', event: DragEvent): void
}>()

const taskErrors = computed(() => {
  const prefix = `tasks[${props.index}].`
  const result: Record<string, string> = {}
  for (const [key, val] of Object.entries(props.errors || {})) {
    if (key.startsWith(prefix) && val.length > 0) {
      result[key.substring(prefix.length)] = val[0]
    }
  }
  return result
})

function updateField<K extends keyof JobTaskInput>(field: K, value: JobTaskInput[K]) {
  emit('update:task', {
    ...props.task,
    [field]: value
  })
}

function onNumberInput(field: 'timeoutSeconds' | 'backoffLimit' | 'ttlSeconds', raw: string | number) {
  const num = raw === '' ? null : Number(raw)
  updateField(field, isNaN(num as number) ? null : num)
}
</script>

<template>
  <div
    class="rounded-[8px] border border-border bg-card shadow-sm transition-all overflow-hidden"
    draggable="true"
    @dragstart="emit('dragstart', $event)"
    @dragover="emit('dragover', $event)"
    @drop="emit('drop', $event)"
  >
    <!-- Header of TaskCard -->
    <div class="px-4 py-3 bg-muted/40 border-b border-border flex items-center justify-between gap-3 select-none">
      <div class="flex items-center gap-2.5">
        <div
          class="cursor-grab active:cursor-grabbing text-muted-fg hover:text-fg p-1 rounded-[4px] focus-visible:outline-2 focus-visible:outline-ring"
          title="Kéo thả để đổi thứ tự"
          aria-label="Kéo thả để đổi thứ tự tác vụ"
        >
          <GripVertical class="w-4 h-4" aria-hidden="true" />
        </div>

        <span class="inline-flex items-center justify-center px-2 py-0.5 rounded text-xs font-semibold bg-primary/10 text-primary border border-primary/20">
          #{{ index + 1 }}
        </span>

        <h4 class="text-sm font-semibold text-fg truncate">
          {{ task.label ? task.label : `Tác vụ #${index + 1}` }}
        </h4>
      </div>

      <!-- Action buttons: Keyboard Move Up, Move Down, Duplicate, Delete -->
      <div class="flex items-center gap-1">
        <button
          type="button"
          :disabled="disabled || index === 0"
          aria-label="Di chuyển tác vụ lên"
          title="Di chuyển lên (bàn phím)"
          class="p-1.5 rounded-[6px] text-muted-fg hover:text-fg hover:bg-muted disabled:opacity-30 disabled:cursor-not-allowed cursor-pointer focus-visible:outline-2 focus-visible:outline-ring"
          @click="emit('move-up')"
        >
          <ArrowUp class="w-4 h-4" aria-hidden="true" />
        </button>

        <button
          type="button"
          :disabled="disabled || index === totalTasks - 1"
          aria-label="Di chuyển tác vụ xuống"
          title="Di chuyển xuống (bàn phím)"
          class="p-1.5 rounded-[6px] text-muted-fg hover:text-fg hover:bg-muted disabled:opacity-30 disabled:cursor-not-allowed cursor-pointer focus-visible:outline-2 focus-visible:outline-ring"
          @click="emit('move-down')"
        >
          <ArrowDown class="w-4 h-4" aria-hidden="true" />
        </button>

        <button
          type="button"
          :disabled="disabled"
          aria-label="Nhân bản tác vụ"
          title="Nhân bản tác vụ này"
          class="p-1.5 rounded-[6px] text-muted-fg hover:text-fg hover:bg-muted disabled:opacity-30 disabled:cursor-not-allowed cursor-pointer focus-visible:outline-2 focus-visible:outline-ring"
          @click="emit('duplicate')"
        >
          <Copy class="w-4 h-4" aria-hidden="true" />
        </button>

        <button
          type="button"
          :disabled="disabled || totalTasks <= 1"
          aria-label="Xoá tác vụ"
          title="Xoá tác vụ"
          class="p-1.5 rounded-[6px] text-muted-fg hover:text-danger hover:bg-muted disabled:opacity-30 disabled:cursor-not-allowed cursor-pointer focus-visible:outline-2 focus-visible:outline-ring"
          @click="emit('remove')"
        >
          <Trash2 class="w-4 h-4" aria-hidden="true" />
        </button>
      </div>
    </div>

    <!-- Body of TaskCard -->
    <div class="p-4 space-y-4">
      <!-- Row 1: Task label -->
      <AppField
        label="Nhãn tác vụ"
        :required="true"
        description="Tên định danh tác vụ trong Job (VD: files, users, migrate)"
        :error="taskErrors['label']"
      >
        <template #default="{ id, error: isError, ariaDescribedby }">
          <AppInput
            :id="id"
            :model-value="task.label"
            placeholder="VD: migrate-db"
            :disabled="disabled"
            :error="isError"
            :aria-describedby="ariaDescribedby"
            @update:model-value="updateField('label', $event)"
          />
        </template>
      </AppField>

      <!-- Row 2: Args (ChipInput) -->
      <AppField
        label="Đối số chạy (Arguments)"
        description="Nhập đối số rồi nhấn Enter hoặc dấu phẩy để thêm thẻ. Có thể dán nhiều giá trị."
        :error="taskErrors['args']"
      >
        <template #default="{ id, error: isError, ariaDescribedby }">
          <ChipInput
            :id="id"
            :model-value="task.args || []"
            :disabled="disabled"
            :error="isError"
            :aria-describedby="ariaDescribedby"
            placeholder="VD: migrate, --module, files"
            @update:model-value="updateField('args', $event)"
          />
        </template>
      </AppField>

      <!-- Row 3: ConfigMap & Secret envFrom from allowlist -->
      <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
        <AppField
          label="Nguồn ConfigMap (envFrom)"
          description="Chỉ liệt kê ConfigMap thuộc allowlist của namespace đã chọn"
          :error="taskErrors['envFromConfigMaps']"
        >
          <template #default="{ id, error: isError, ariaDescribedby }">
            <AllowlistMultiSelect
              :id="id"
              :model-value="task.envFromConfigMaps || []"
              :options="configMapOptions"
              placeholder="Chọn ConfigMap từ allowlist..."
              empty-text="Chưa có ConfigMap nào trong allowlist của namespace này"
              :disabled="disabled"
              :error="isError"
              :aria-describedby="ariaDescribedby"
              @update:model-value="updateField('envFromConfigMaps', $event)"
            />
          </template>
        </AppField>

        <AppField
          label="Nguồn Secret (envFrom)"
          description="Chỉ liệt kê Secret thuộc allowlist của namespace đã chọn"
          :error="taskErrors['envFromSecrets']"
        >
          <template #default="{ id, error: isError, ariaDescribedby }">
            <AllowlistMultiSelect
              :id="id"
              :model-value="task.envFromSecrets || []"
              :options="secretOptions"
              placeholder="Chọn Secret từ allowlist..."
              empty-text="Chưa có Secret nào trong allowlist của namespace này"
              :disabled="disabled"
              :error="isError"
              :aria-describedby="ariaDescribedby"
              @update:model-value="updateField('envFromSecrets', $event)"
            />
          </template>
        </AppField>
      </div>

      <!-- Row 4: Environment Variables (EnvEditor) -->
      <AppField
        label="Biến môi trường cụ thể"
        description="Giá trị trực tiếp hoặc tham chiếu Secret từ allowlist. Giá trị secret không bao giờ được yêu cầu nhập hoặc lưu."
        :error="taskErrors['env']"
      >
        <template #default="{ error: isError }">
          <EnvEditor
            :model-value="task.env || []"
            :secret-options="secretOptions"
            :disabled="disabled"
            :error="isError"
            @update:model-value="updateField('env', $event as JobEnvVar[])"
          />
        </template>
      </AppField>

      <!-- Row 5: Timeout, BackoffLimit, TTL -->
      <div class="grid grid-cols-1 sm:grid-cols-3 gap-4 pt-2 border-t border-border/50">
        <AppField
          label="Thời gian tối đa (giây)"
          :required="true"
          description="Bắt buộc. Mặc định 1800s (30 phút)"
          :error="taskErrors['timeoutSeconds']"
        >
          <template #default="{ id, error: isError, ariaDescribedby }">
            <AppInput
              :id="id"
              type="number"
              :model-value="task.timeoutSeconds ?? ''"
              placeholder="1800"
              :disabled="disabled"
              :error="isError"
              :aria-describedby="ariaDescribedby"
              @update:model-value="onNumberInput('timeoutSeconds', $event)"
            />
          </template>
        </AppField>

        <AppField
          label="Số lần thử lại"
          description="Backoff limit. Mặc định 0"
          :error="taskErrors['backoffLimit']"
        >
          <template #default="{ id, error: isError, ariaDescribedby }">
            <AppInput
              :id="id"
              type="number"
              :model-value="task.backoffLimit ?? 0"
              placeholder="0"
              :disabled="disabled"
              :error="isError"
              :aria-describedby="ariaDescribedby"
              @update:model-value="onNumberInput('backoffLimit', $event)"
            />
          </template>
        </AppField>

        <AppField
          label="TTL sau khi xong (giây)"
          description="Tự xoá Job sau khi chạy xong. Mặc định 3600s"
          :error="taskErrors['ttlSeconds']"
        >
          <template #default="{ id, error: isError, ariaDescribedby }">
            <AppInput
              :id="id"
              type="number"
              :model-value="task.ttlSeconds ?? 3600"
              placeholder="3600"
              :disabled="disabled"
              :error="isError"
              :aria-describedby="ariaDescribedby"
              @update:model-value="onNumberInput('ttlSeconds', $event)"
            />
          </template>
        </AppField>
      </div>
    </div>
  </div>
</template>
