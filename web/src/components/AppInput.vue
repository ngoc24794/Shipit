<script setup lang="ts">
import { computed, ref, useSlots } from 'vue'
import { X, ChevronUp, ChevronDown } from 'lucide-vue-next'

const props = withDefaults(
  defineProps<{
    modelValue?: string | number
    type?: string
    id?: string
    name?: string
    placeholder?: string
    disabled?: boolean
    readonly?: boolean
    error?: boolean | string
    help?: string
    ariaDescribedby?: string
    size?: 'sm' | 'md'
    clearable?: boolean
    maxlength?: number
    showCount?: boolean
    selectOnFocus?: boolean
    min?: number
    max?: number
    step?: number
    autocomplete?: string
  }>(),
  {
    modelValue: '',
    type: 'text',
    id: undefined,
    name: undefined,
    placeholder: undefined,
    disabled: false,
    readonly: false,
    error: false,
    help: undefined,
    ariaDescribedby: undefined,
    size: 'md',
    clearable: false,
    maxlength: undefined,
    showCount: false,
    selectOnFocus: false,
    min: undefined,
    max: undefined,
    step: 1,
    autocomplete: undefined
  }
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: string): void
  (e: 'blur', event: FocusEvent): void
  (e: 'focus', event: FocusEvent): void
  (e: 'clear'): void
}>()

const slots = useSlots()
const inputRef = ref<HTMLInputElement | null>(null)

const strValue = computed(() => (props.modelValue === null || props.modelValue === undefined ? '' : String(props.modelValue)))
const showClear = computed(() => props.clearable && !props.disabled && !props.readonly && strValue.value.length > 0)
const isNumber = computed(() => props.type === 'number')
const hasPrefix = computed(() => !!slots.prefix)
const hasSuffix = computed(() => !!slots.suffix || showClear.value || isNumber.value)

function onInput(event: Event) {
  const target = event.target as HTMLInputElement
  emit('update:modelValue', target.value)
}

function onFocus(event: FocusEvent) {
  if (props.selectOnFocus && inputRef.value) {
    inputRef.value.select()
  }
  emit('focus', event)
}

function clearValue() {
  emit('update:modelValue', '')
  emit('clear')
  inputRef.value?.focus()
}

function stepNumber(delta: number) {
  if (props.disabled || props.readonly) return
  const current = Number(strValue.value)
  const base = Number.isFinite(current) ? current : 0
  let next = base + delta * (props.step || 1)
  if (props.min !== undefined) next = Math.max(props.min, next)
  if (props.max !== undefined) next = Math.min(props.max, next)
  emit('update:modelValue', String(next))
}

function onKeydown(event: KeyboardEvent) {
  if (isNumber.value) {
    if (event.key === 'ArrowUp') {
      event.preventDefault()
      stepNumber(1)
    } else if (event.key === 'ArrowDown') {
      event.preventDefault()
      stepNumber(-1)
    }
  }
}

defineExpose({
  focus: () => inputRef.value?.focus(),
  select: () => inputRef.value?.select(),
  el: inputRef
})
</script>

<template>
  <div class="w-full flex flex-col gap-1">
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
      <span
        v-if="hasPrefix"
        class="inline-flex items-center pl-2 pr-1 text-fg-muted shrink-0 select-none"
      >
        <slot name="prefix" />
      </span>

      <input
        ref="inputRef"
        :id="id"
        :name="name"
        :type="type"
        :value="modelValue"
        :placeholder="placeholder"
        :disabled="disabled"
        :readonly="readonly"
        :maxlength="maxlength"
        :min="min"
        :max="max"
        :step="step"
        :autocomplete="autocomplete"
        :aria-invalid="!!error ? 'true' : undefined"
        :aria-describedby="ariaDescribedby"
        :class="[
          'flex-1 min-w-0 h-full bg-transparent border-0 outline-none focus-visible:outline-none text-fg-strong placeholder:text-fg-dim',
          'disabled:cursor-not-allowed',
          '[appearance:textfield] [&::-webkit-outer-spin-button]:appearance-none [&::-webkit-inner-spin-button]:appearance-none',
          hasPrefix ? 'pl-1' : 'pl-2',
          hasSuffix ? 'pr-1' : 'pr-2'
        ]"
        @input="onInput"
        @focus="onFocus"
        @blur="emit('blur', $event)"
        @keydown="onKeydown"
      />

      <span v-if="hasSuffix" class="inline-flex items-center gap-0.5 pr-1 text-fg-muted shrink-0">
        <button
          v-if="showClear"
          type="button"
          aria-label="Xoá nội dung"
          class="inline-flex items-center justify-center w-5 h-5 rounded-[2px] text-fg-muted hover:text-fg-strong hover:bg-bg-hover cursor-pointer"
          @click="clearValue"
        >
          <X class="w-3 h-3" aria-hidden="true" />
        </button>

        <span v-if="isNumber && !readonly" class="inline-flex flex-col border-l border-border pl-0.5">
          <button
            type="button"
            tabindex="-1"
            :disabled="disabled"
            aria-label="Tăng giá trị"
            class="inline-flex items-center justify-center w-4 h-3 text-fg-muted hover:text-fg-strong hover:bg-bg-hover cursor-pointer disabled:cursor-not-allowed"
            @click="stepNumber(1)"
          >
            <ChevronUp class="w-3 h-3" aria-hidden="true" />
          </button>
          <button
            type="button"
            tabindex="-1"
            :disabled="disabled"
            aria-label="Giảm giá trị"
            class="inline-flex items-center justify-center w-4 h-3 text-fg-muted hover:text-fg-strong hover:bg-bg-hover cursor-pointer disabled:cursor-not-allowed"
            @click="stepNumber(-1)"
          >
            <ChevronDown class="w-3 h-3" aria-hidden="true" />
          </button>
        </span>

        <slot name="suffix" />
      </span>
    </div>

    <div
      v-if="help || (showCount && maxlength)"
      class="flex items-center justify-between gap-2 text-[11px] text-fg-muted"
    >
      <span v-if="help" class="truncate">{{ help }}</span>
      <span v-else />
      <span v-if="showCount && maxlength" class="tabular-nums shrink-0 text-fg-dim">
        {{ strValue.length }}/{{ maxlength }}
      </span>
    </div>
  </div>
</template>
