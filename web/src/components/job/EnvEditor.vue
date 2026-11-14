<script setup lang="ts">
import { Plus, Trash2, ChevronDown, Lock } from 'lucide-vue-next'
import type { JobEnvVar } from '../../api/types'
import AppButton from '../AppButton.vue'

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
  <div class="flex flex-col gap-3 w-full">
    <div v-if="modelValue.length === 0" class="text-xs text-muted-fg italic py-1">
      Chưa có biến môi trường nào được cấu hình.
    </div>

    <div v-else class="space-y-2.5">
      <div
        v-for="(row, idx) in modelValue"
        :key="idx"
        class="p-2.5 rounded-[6px] bg-muted/30 border border-border flex flex-col md:flex-row items-stretch md:items-center gap-2.5"
      >
        <!-- Variable Name -->
        <div class="flex-1 min-w-[140px]">
          <input
            type="text"
            :value="row.name || ''"
            :disabled="disabled"
            placeholder="Tên biến (VD: PORT)"
            aria-label="Tên biến môi trường"
            class="w-full h-9 px-3 text-sm rounded-[6px] border border-border bg-card text-fg placeholder:text-muted-fg/70 focus-visible:outline-2 focus-visible:outline-ring"
            @input="updateName(idx, ($event.target as HTMLInputElement).value)"
          />
        </div>

        <!-- Source Selector: Direct value vs From Secret -->
        <div class="w-full md:w-[150px] flex-shrink-0 relative">
          <select
            :value="row.secretName !== undefined && row.secretName !== null ? 'secret' : 'value'"
            :disabled="disabled"
            aria-label="Nguồn biến môi trường"
            class="w-full h-9 pl-2.5 pr-8 text-xs font-medium rounded-[6px] border border-border bg-card text-fg appearance-none cursor-pointer focus-visible:outline-2 focus-visible:outline-ring"
            @change="updateSource(idx, ($event.target as HTMLSelectElement).value as 'value' | 'secret')"
          >
            <option value="value">Giá trị trực tiếp</option>
            <option value="secret">Lấy từ Secret</option>
          </select>
          <div class="absolute inset-y-0 right-0 flex items-center pr-2 pointer-events-none text-muted-fg">
            <ChevronDown class="w-3.5 h-3.5" aria-hidden="true" />
          </div>
        </div>

        <!-- Value Input OR Secret Selection -->
        <div class="flex-1 flex flex-col sm:flex-row items-center gap-2 min-w-[180px]">
          <!-- Case A: Plain direct value -->
          <template v-if="row.secretName === undefined || row.secretName === null">
            <input
              type="text"
              :value="row.value || ''"
              :disabled="disabled"
              placeholder="Giá trị biến"
              aria-label="Giá trị biến môi trường"
              class="w-full h-9 px-3 text-sm rounded-[6px] border border-border bg-card text-fg placeholder:text-muted-fg/70 focus-visible:outline-2 focus-visible:outline-ring"
              @input="updateValue(idx, ($event.target as HTMLInputElement).value)"
            />
          </template>

          <!-- Case B: From Secret (Secret name dropdown from allowlist + Secret key input) -->
          <!-- NOTE: Never displays or requests the secret's secret value! -->
          <template v-else>
            <div class="w-full sm:w-1/2 relative">
              <select
                :value="row.secretName || ''"
                :disabled="disabled || secretOptions.length === 0"
                aria-label="Tên Secret trong allowlist"
                class="w-full h-9 pl-2.5 pr-7 text-xs font-mono rounded-[6px] border border-border bg-card text-fg appearance-none cursor-pointer focus-visible:outline-2 focus-visible:outline-ring"
                @change="updateSecretName(idx, ($event.target as HTMLSelectElement).value)"
              >
                <option value="" disabled>-- Chọn Secret --</option>
                <option v-for="sec in secretOptions" :key="sec" :value="sec">
                  {{ sec }}
                </option>
              </select>
              <div class="absolute inset-y-0 right-0 flex items-center pr-2 pointer-events-none text-muted-fg">
                <ChevronDown class="w-3.5 h-3.5" aria-hidden="true" />
              </div>
            </div>

            <div class="w-full sm:w-1/2 relative flex items-center">
              <input
                type="text"
                :value="row.secretKey || ''"
                :disabled="disabled"
                placeholder="Key (VD: password)"
                aria-label="Key trong Secret"
                class="w-full h-9 pl-7 pr-3 text-xs font-mono rounded-[6px] border border-border bg-card text-fg placeholder:text-muted-fg/70 focus-visible:outline-2 focus-visible:outline-ring"
                @input="updateSecretKey(idx, ($event.target as HTMLInputElement).value)"
              />
              <div class="absolute inset-y-0 left-0 flex items-center pl-2 pointer-events-none text-muted-fg">
                <Lock class="w-3.5 h-3.5" aria-hidden="true" />
              </div>
            </div>
          </template>
        </div>

        <!-- Remove row button -->
        <button
          v-if="!disabled"
          type="button"
          aria-label="Xoá biến môi trường"
          class="h-9 w-9 flex items-center justify-center rounded-[6px] text-muted-fg hover:text-danger hover:bg-card border border-transparent hover:border-border transition-colors cursor-pointer focus-visible:outline-2 focus-visible:outline-ring flex-shrink-0"
          @click="removeRow(idx)"
        >
          <Trash2 class="w-4 h-4" aria-hidden="true" />
        </button>
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
