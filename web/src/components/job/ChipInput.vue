<script setup lang="ts">
import { ref } from 'vue'
import { X } from 'lucide-vue-next'

const props = withDefaults(
  defineProps<{
    modelValue: string[]
    placeholder?: string
    disabled?: boolean
    id?: string
    ariaDescribedby?: string
    error?: boolean | string
  }>(),
  {
    placeholder: 'Nhập đối số rồi nhấn Enter hoặc dấu phẩy...',
    disabled: false,
    id: undefined,
    ariaDescribedby: undefined,
    error: false
  }
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: string[]): void
}>()

const inputValue = ref('')
const inputRef = ref<HTMLInputElement | null>(null)

function addTokens(raw: string) {
  if (!raw) return
  // Split by comma or newline
  const tokens = raw
    .split(/[,\n]/)
    .map((t) => t.trim())
    .filter((t) => t.length > 0)

  if (tokens.length === 0) return

  const next = [...props.modelValue]
  for (const token of tokens) {
    next.push(token)
  }
  emit('update:modelValue', next)
  inputValue.value = ''
}

function onKeydown(e: KeyboardEvent) {
  if (props.disabled) return

  if (e.key === 'Enter') {
    e.preventDefault()
    addTokens(inputValue.value)
  } else if (e.key === ',') {
    e.preventDefault()
    addTokens(inputValue.value)
  } else if (e.key === 'Backspace' && inputValue.value === '') {
    if (props.modelValue.length > 0) {
      removeChip(props.modelValue.length - 1)
    }
  }
}

function onPaste(e: ClipboardEvent) {
  if (props.disabled) return
  const pasted = e.clipboardData?.getData('text')
  if (pasted && (pasted.includes(',') || pasted.includes('\n') || pasted.includes(' '))) {
    e.preventDefault()
    // Split by comma, newline, or whitespace if multiple
    const tokens = pasted
      .split(/[,\n\r]+/)
      .map((t) => t.trim())
      .filter((t) => t.length > 0)

    if (tokens.length > 0) {
      emit('update:modelValue', [...props.modelValue, ...tokens])
      inputValue.value = ''
    }
  }
}

function onBlur() {
  if (inputValue.value.trim()) {
    addTokens(inputValue.value)
  }
}

function removeChip(index: number) {
  if (props.disabled) return
  const next = [...props.modelValue]
  next.splice(index, 1)
  emit('update:modelValue', next)
}

function focusInput() {
  inputRef.value?.focus()
}
</script>

<template>
  <div
    :class="[
      'min-h-[42px] w-full px-2.5 py-1.5 rounded-[6px] border bg-card text-fg transition-colors flex flex-wrap items-center gap-1.5 cursor-text',
      error
        ? 'border-danger focus-within:ring-2 focus-within:ring-danger'
        : 'border-border focus-within:ring-2 focus-within:ring-ring focus-within:border-transparent',
      disabled ? 'opacity-50 cursor-not-allowed bg-muted/50' : 'hover:border-muted-fg/50'
    ]"
    @click="focusInput"
  >
    <span
      v-for="(chip, idx) in modelValue"
      :key="idx"
      class="inline-flex items-center gap-1 px-2 py-0.5 rounded-[4px] text-xs font-mono bg-muted text-fg border border-border/60 max-w-full break-all select-none"
    >
      <span>{{ chip }}</span>
      <button
        v-if="!disabled"
        type="button"
        :aria-label="`Xoá đối số ${chip}`"
        class="text-muted-fg hover:text-danger hover:bg-card p-0.5 rounded-[2px] transition-colors cursor-pointer focus-visible:outline-2 focus-visible:outline-ring"
        @click.stop="removeChip(idx)"
      >
        <X class="w-3 h-3" aria-hidden="true" />
      </button>
    </span>

    <input
      :id="id"
      ref="inputRef"
      v-model="inputValue"
      type="text"
      :placeholder="modelValue.length === 0 ? placeholder : ''"
      :disabled="disabled"
      :aria-describedby="ariaDescribedby"
      :aria-invalid="!!error ? 'true' : undefined"
      class="flex-1 min-w-[140px] bg-transparent text-sm text-fg outline-none border-none py-1 placeholder:text-muted-fg/70"
      @keydown="onKeydown"
      @paste="onPaste"
      @blur="onBlur"
    />
  </div>
</template>
