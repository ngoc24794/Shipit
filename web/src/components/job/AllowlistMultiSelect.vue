<script setup lang="ts">
import { computed } from 'vue'
import { X, ChevronDown } from 'lucide-vue-next'

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

const availableOptions = computed(() => {
  return props.options.filter((opt) => !props.modelValue.includes(opt))
})

function onSelect(e: Event) {
  const target = e.target as HTMLSelectElement
  const val = target.value
  if (!val) return
  if (!props.modelValue.includes(val)) {
    emit('update:modelValue', [...props.modelValue, val])
  }
  target.value = ''
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
  <div class="flex flex-col gap-2 w-full">
    <!-- Selected pills -->
    <div v-if="modelValue.length > 0" class="flex flex-wrap items-center gap-1.5">
      <span
        v-for="item in modelValue"
        :key="item"
        class="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-[6px] text-xs font-mono bg-muted text-fg border border-border"
      >
        <span>{{ item }}</span>
        <button
          v-if="!disabled"
          type="button"
          :aria-label="`Bỏ chọn ${item}`"
          class="text-muted-fg hover:text-danger p-0.5 rounded-[2px] transition-colors cursor-pointer focus-visible:outline-2 focus-visible:outline-ring"
          @click="remove(item)"
        >
          <X class="w-3 h-3" aria-hidden="true" />
        </button>
      </span>
    </div>

    <!-- Dropdown to select from allowed items -->
    <div v-if="options.length > 0" class="relative w-full">
      <select
        :id="id"
        :disabled="disabled || availableOptions.length === 0"
        :aria-describedby="ariaDescribedby"
        :aria-invalid="!!error ? 'true' : undefined"
        :class="[
          'w-full h-10 pl-3 pr-9 py-2 text-sm rounded-[6px] border bg-card text-fg appearance-none cursor-pointer transition-colors',
          'focus-visible:outline-2 focus-visible:outline-ring focus-visible:outline-offset-2',
          error
            ? 'border-danger focus-visible:outline-danger'
            : 'border-border focus-visible:border-transparent',
          disabled || availableOptions.length === 0 ? 'opacity-50 cursor-not-allowed bg-muted/50' : 'hover:border-muted-fg/50'
        ]"
        @change="onSelect"
      >
        <option value="" disabled selected>
          {{ availableOptions.length === 0 ? 'Đã chọn toàn bộ danh sách cho phép' : placeholder }}
        </option>
        <option v-for="opt in availableOptions" :key="opt" :value="opt">
          {{ opt }}
        </option>
      </select>

      <div class="absolute inset-y-0 right-0 flex items-center pr-3 pointer-events-none text-muted-fg">
        <ChevronDown class="w-4 h-4" aria-hidden="true" />
      </div>
    </div>

    <!-- Empty state when no options in allowlist -->
    <p v-else class="text-xs text-muted-fg italic py-1">
      {{ emptyText }}
    </p>
  </div>
</template>
