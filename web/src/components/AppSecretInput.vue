<script setup lang="ts">
import { ref, computed } from 'vue'
import { Eye, EyeOff } from 'lucide-vue-next'

const props = withDefaults(
  defineProps<{
    modelValue?: string
    id?: string
    name?: string
    placeholder?: string
    hasExistingValue?: boolean
    disabled?: boolean
    error?: boolean | string
    ariaDescribedby?: string
  }>(),
  {
    modelValue: '',
    id: undefined,
    name: undefined,
    placeholder: undefined,
    hasExistingValue: false,
    disabled: false,
    error: false,
    ariaDescribedby: undefined
  }
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: string): void
  (e: 'blur', event: FocusEvent): void
  (e: 'focus', event: FocusEvent): void
}>()

const isVisible = ref(false)

const effectivePlaceholder = computed(() => {
  if (props.placeholder !== undefined) {
    return props.placeholder
  }
  return props.hasExistingValue ? 'Đã lưu — để trống để giữ nguyên' : 'Nhập giá trị bí mật'
})

function toggleVisibility() {
  isVisible.value = !isVisible.value
}

function onInput(event: Event) {
  const target = event.target as HTMLInputElement
  emit('update:modelValue', target.value)
}
</script>

<template>
  <div class="relative w-full">
    <input
      :id="id"
      :name="name"
      :type="isVisible ? 'text' : 'password'"
      :value="modelValue"
      :placeholder="effectivePlaceholder"
      :disabled="disabled"
      :aria-invalid="!!error ? 'true' : undefined"
      :aria-describedby="ariaDescribedby"
      autocomplete="new-password"
      :class="[
        'w-full h-10 pl-3 pr-10 text-base sm:text-sm rounded-[6px] bg-card text-fg border transition-colors font-mono',
        'placeholder:text-muted-fg/60 placeholder:font-sans',
        'focus-visible:outline-2 focus-visible:outline-ring focus-visible:outline-offset-2',
        'disabled:opacity-50 disabled:bg-muted disabled:cursor-not-allowed',
        error ? 'border-danger focus-visible:outline-danger' : 'border-border focus-visible:border-primary'
      ]"
      @input="onInput"
      @blur="emit('blur', $event)"
      @focus="emit('focus', $event)"
    />
    <button
      type="button"
      :disabled="disabled"
      :aria-label="isVisible ? 'Ẩn giá trị bí mật' : 'Hiện giá trị bí mật'"
      class="absolute inset-y-0 right-0 flex items-center justify-center w-10 text-muted-fg hover:text-fg focus-visible:outline-2 focus-visible:outline-ring rounded-r-[6px] transition-colors cursor-pointer disabled:cursor-not-allowed disabled:opacity-50"
      @click="toggleVisibility"
    >
      <EyeOff v-if="isVisible" class="w-4 h-4" aria-hidden="true" />
      <Eye v-else class="w-4 h-4" aria-hidden="true" />
    </button>
  </div>
</template>
