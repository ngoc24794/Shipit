<script setup lang="ts">
import { computed, ref } from 'vue'
import { X } from 'lucide-vue-next'
import AppSelect from '../AppSelect.vue'

const props = withDefaults(
  defineProps<{
    modelValue: string[]
    options: string[]
    placeholder?: string
    disabled?: boolean
    id?: string
    ariaDescribedby?: string
    error?: boolean | string
    emptyText?: string
  }>(),
  {
    placeholder: 'Chọn từ allowlist...',
    disabled: false,
    id: undefined,
    ariaDescribedby: undefined,
    error: false,
    emptyText: 'Không có mục nào trong allowlist của namespace này'
  }
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: string[]): void
}>()

const pendingSelection = ref('')

const availableOptions = computed(() => {
  return props.options.filter((opt) => !props.modelValue.includes(opt))
})

const selectOptions = computed(() => {
  return availableOptions.value.map((opt) => ({ label: opt, value: opt }))
})

function onSelect(val: string | number | null) {
  const str = val ? String(val) : ''
  if (!str) return
  if (!props.modelValue.includes(str)) {
    emit('update:modelValue', [...props.modelValue, str])
  }
  pendingSelection.value = ''
}

function remove(item: string) {
  if (props.disabled) return
  emit(
    'update:modelValue',
    props.modelValue.filter((v) => v !== item)
  )
}
</script>

<template>
  <div class="flex flex-col gap-2 w-full font-mono">
    <!-- Selected pills -->
    <div v-if="modelValue.length > 0" class="flex flex-wrap items-center gap-1.5">
      <span
        v-for="item in modelValue"
        :key="item"
        class="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-[2px] text-xs font-mono bg-bg-surface text-fg-strong border border-border"
      >
        <span>{{ item }}</span>
        <button
          v-if="!disabled"
          type="button"
          :aria-label="`Bỏ chọn ${item}`"
          class="text-fg-muted hover:text-danger p-0.5 rounded-[2px] transition-colors cursor-pointer focus-visible:outline-2 focus-visible:outline-border-focus"
          @click="remove(item)"
        >
          <X class="w-3 h-3" aria-hidden="true" />
        </button>
      </span>
    </div>

    <!-- Custom AppSelect to pick from allowed items -->
    <AppSelect
      v-if="options.length > 0"
      :id="id"
      :model-value="pendingSelection"
      :options="selectOptions"
      :disabled="disabled || availableOptions.length === 0"
      :error="Boolean(error)"
      :aria-describedby="ariaDescribedby"
      :placeholder="availableOptions.length === 0 ? 'Đã chọn toàn bộ danh sách cho phép' : placeholder"
      size="sm"
      @update:model-value="onSelect"
    />

    <!-- Empty state when no options in allowlist -->
    <p v-else class="text-xs text-fg-muted italic py-1">
      {{ emptyText }}
    </p>
  </div>
</template>
