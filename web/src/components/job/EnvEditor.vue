<script setup lang="ts">
import { Plus, Trash2, Lock } from 'lucide-vue-next'
import type { JobEnvVar } from '../../api/types'
import AppButton from '../AppButton.vue'
import AppInput from '../AppInput.vue'
import AppSelect from '../AppSelect.vue'

const props = withDefaults(
  defineProps<{
    modelValue: JobEnvVar[]
    secretOptions: string[]
    disabled?: boolean
    error?: boolean | string
  }>(),
  {
    disabled: false,
    error: false
  }
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: JobEnvVar[]): void
}>()

function addRow() {
  if (props.disabled) return
  const next: JobEnvVar[] = [
    ...props.modelValue,
    { name: '', value: '' }
  ]
  emit('update:modelValue', next)
}

function removeRow(index: number) {
  if (props.disabled) return
  const next = [...props.modelValue]
  next.splice(index, 1)
  emit('update:modelValue', next)
}

function updateName(index: number, name: string) {
  const next = [...props.modelValue]
  next[index] = { ...next[index], name }
  emit('update:modelValue', next)
}

function updateValue(index: number, value: string) {
  const next = [...props.modelValue]
  next[index] = { ...next[index], value, secretName: undefined, secretKey: undefined }
  emit('update:modelValue', next)
}

function updateSource(index: number, source: 'value' | 'secret') {
  const next = [...props.modelValue]
  if (source === 'value') {
    next[index] = {
      name: next[index].name,
      value: next[index].value ?? '',
      secretName: undefined,
      secretKey: undefined
    }
  } else {
    next[index] = {
      name: next[index].name,
      value: undefined,
      secretName: props.secretOptions[0] || '',
      secretKey: ''
    }
  }
  emit('update:modelValue', next)
}

function updateSecretName(index: number, secretName: string) {
  const next = [...props.modelValue]
  next[index] = { ...next[index], secretName }
  emit('update:modelValue', next)
}

function updateSecretKey(index: number, secretKey: string) {
  const next = [...props.modelValue]
  next[index] = { ...next[index], secretKey }
  emit('update:modelValue', next)
}
</script>

<template>
  <div class="flex flex-col gap-2.5 w-full font-mono">
    <div v-if="modelValue.length === 0" class="text-xs text-fg-muted italic py-1">
      Chưa có biến môi trường nào được cấu hình.
    </div>

    <div v-else class="space-y-2">
      <div
        v-for="(row, idx) in modelValue"
        :key="idx"
        class="p-2 rounded-[2px] bg-bg-surface border border-border flex flex-row items-center gap-2 flex-nowrap"
      >
        <!-- Variable Name -->
        <div class="flex-1 min-w-[140px]">
          <AppInput
            :model-value="row.name || ''"
            :disabled="disabled"
            size="sm"
            placeholder="Tên biến (VD: PORT)"
            aria-label="Tên biến môi trường"
            @update:model-value="updateName(idx, String($event))"
          />
        </div>

        <!-- Source Selector: Direct value vs From Secret -->
        <div class="w-[155px] flex-shrink-0">
          <AppSelect
            :model-value="row.secretName !== undefined && row.secretName !== null ? 'secret' : 'value'"
            :disabled="disabled"
            size="sm"
            aria-label="Nguồn biến môi trường"
            :options="[
              { label: 'Giá trị trực tiếp', value: 'value' },
              { label: 'Lấy từ Secret', value: 'secret' }
            ]"
            @update:model-value="updateSource(idx, ($event as 'value' | 'secret'))"
          />
        </div>

        <!-- Value Input OR Secret Selection -->
        <div class="flex-1 flex flex-row items-center gap-2 min-w-[180px]">
          <!-- Case A: Plain direct value -->
          <template v-if="row.secretName === undefined || row.secretName === null">
            <AppInput
              :model-value="row.value || ''"
              :disabled="disabled"
              size="sm"
              placeholder="Giá trị biến"
              aria-label="Giá trị biến môi trường"
              @update:model-value="updateValue(idx, String($event))"
            />
          </template>

          <!-- Case B: From Secret (Secret name dropdown from allowlist + Secret key input) -->
          <!-- NOTE: Never displays or requests the secret's secret value! -->
          <template v-else>
            <div class="w-1/2">
              <AppSelect
                :model-value="row.secretName || ''"
                :disabled="disabled || secretOptions.length === 0"
                size="sm"
                placeholder="-- Chọn Secret --"
                aria-label="Tên Secret trong allowlist"
                :options="secretOptions.map((sec) => ({ label: sec, value: sec }))"
                @update:model-value="updateSecretName(idx, String($event))"
              />
            </div>

            <div class="w-1/2">
              <AppInput
                :model-value="row.secretKey || ''"
                :disabled="disabled"
                size="sm"
                placeholder="Key (VD: password)"
                aria-label="Key trong Secret"
                @update:model-value="updateSecretKey(idx, String($event))"
              >
                <template #prefix>
                  <Lock class="w-3.5 h-3.5 text-fg-muted" aria-hidden="true" />
                </template>
              </AppInput>
            </div>
          </template>
        </div>

        <!-- Remove row button -->
        <AppButton
          v-if="!disabled"
          type="button"
          variant="ghost"
          size="sm"
          icon-only
          aria-label="Xoá biến môi trường"
          class="flex-shrink-0 hover:text-danger"
          @click="removeRow(idx)"
        >
          <Trash2 class="w-3.5 h-3.5" aria-hidden="true" />
        </AppButton>
      </div>
    </div>

    <!-- Add env var button -->
    <div class="pt-1">
      <AppButton
        type="button"
        variant="secondary"
        size="sm"
        :disabled="disabled"
        aria-label="Thêm biến môi trường"
        @click="addRow"
      >
        <Plus class="w-3.5 h-3.5 mr-1" aria-hidden="true" />
        <span>Thêm biến môi trường</span>
      </AppButton>
    </div>
  </div>
</template>
