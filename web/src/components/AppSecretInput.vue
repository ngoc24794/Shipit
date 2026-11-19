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
    size?: 'sm' | 'md'
  }>(),
  {
    modelValue: '',
    id: undefined,
    name: undefined,
    placeholder: undefined,
    hasExistingValue: false,
    disabled: false,
    error: false,
    ariaDescribedby: undefined,
    size: 'md'
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
  return props.hasExistingValue ? 'Đã lưu, để trống để giữ nguyên' : 'Nhập giá trị bí mật'
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
  <div
    :class="[
      'flex items-center w-full rounded-[2px] bg-bg-input text-fg-strong border font-mono transition-colors duration-[80ms]',
      size === 'sm' ? 'h-6 text-[11.5px]' : 'h-7 text-xs',
      disabled ? 'opacity-50 bg-bg-surface cursor-not-allowed' : '',
      error
        ? 'border-danger focus-within:border-danger focus-within:outline-2 focus-within:outline-danger focus-within:outline-offset-1'
        : 'border-border focus-within:border-border-focus'
    ]"
  >
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
      class="flex-1 min-w-0 h-full pl-2 pr-1 bg-transparent border-0 outline-none focus-visible:outline-none font-mono text-fg-strong placeholder:text-fg-dim disabled:cursor-not-allowed"
      @input="onInput"
      @blur="emit('blur', $event)"
      @focus="emit('focus', $event)"
    />
    <button
      type="button"
      :disabled="disabled"
      :aria-label="isVisible ? 'Ẩn giá trị bí mật' : 'Hiện giá trị bí mật'"
      class="inline-flex items-center gap-1 h-full px-2 border-l border-border text-[11px] text-fg-muted hover:text-fg-strong hover:bg-bg-hover focus-visible:outline-2 focus-visible:outline-border-focus cursor-pointer disabled:cursor-not-allowed disabled:opacity-50 shrink-0"
      @click="toggleVisibility"
    >
      <EyeOff v-if="isVisible" class="w-3.5 h-3.5" aria-hidden="true" />
      <Eye v-else class="w-3.5 h-3.5" aria-hidden="true" />
      <span>{{ isVisible ? '[Ẩn]' : '[Hiện]' }}</span>
    </button>
  </div>
</template>
