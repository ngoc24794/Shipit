<script setup lang="ts">
import { computed, ref, watch, nextTick, onMounted } from 'vue'

const props = withDefaults(
  defineProps<{
    modelValue?: string
    id?: string
    name?: string
    placeholder?: string
    rows?: number
    autosize?: boolean
    disabled?: boolean
    readonly?: boolean
    error?: boolean | string
    help?: string
    maxlength?: number
    showCount?: boolean
    ariaDescribedby?: string
  }>(),
  {
    modelValue: '',
    id: undefined,
    name: undefined,
    placeholder: undefined,
    rows: 3,
    autosize: false,
    disabled: false,
    readonly: false,
    error: false,
    help: undefined,
    maxlength: undefined,
    showCount: false,
    ariaDescribedby: undefined
  }
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: string): void
  (e: 'blur', event: FocusEvent): void
  (e: 'focus', event: FocusEvent): void
}>()

const areaRef = ref<HTMLTextAreaElement | null>(null)
const length = computed(() => (props.modelValue ? String(props.modelValue).length : 0))

function resize() {
  if (!props.autosize || !areaRef.value) return
  areaRef.value.style.height = 'auto'
  areaRef.value.style.height = `${areaRef.value.scrollHeight}px`
}

function onInput(event: Event) {
  const target = event.target as HTMLTextAreaElement
  emit('update:modelValue', target.value)
  if (props.autosize) nextTick(resize)
}

watch(() => props.modelValue, () => {
  if (props.autosize) nextTick(resize)
})

onMounted(() => {
  if (props.autosize) resize()
})

defineExpose({
  focus: () => areaRef.value?.focus(),
  el: areaRef
})
</script>

<template>
  <div class="w-full flex flex-col gap-1">
    <textarea
      ref="areaRef"
      :id="id"
      :name="name"
      :rows="rows"
      :value="modelValue"
      :placeholder="placeholder"
      :disabled="disabled"
      :readonly="readonly"
      :maxlength="maxlength"
      :aria-invalid="!!error ? 'true' : undefined"
      :aria-describedby="ariaDescribedby"
      :class="[
        'w-full p-2 text-xs font-mono rounded-[2px] bg-bg-input text-fg-strong border transition-colors duration-[80ms]',
        'placeholder:text-fg-dim',
        'focus-visible:outline-2 focus-visible:outline-border-focus focus-visible:outline-offset-1',
        'disabled:opacity-50 disabled:bg-bg-surface disabled:cursor-not-allowed',
        autosize ? 'resize-none overflow-hidden' : 'resize-y',
        error ? 'border-danger focus-visible:outline-danger' : 'border-border focus-visible:border-border-focus'
      ]"
      @input="onInput"
      @blur="emit('blur', $event)"
      @focus="emit('focus', $event)"
    />
    <div
      v-if="help || (showCount && maxlength)"
      class="flex items-center justify-between gap-2 text-[11px] text-fg-muted"
    >
      <span v-if="help" class="truncate">{{ help }}</span>
      <span v-else />
      <span v-if="showCount && maxlength" class="tabular-nums shrink-0 text-fg-dim">
        {{ length }}/{{ maxlength }}
      </span>
    </div>
  </div>
</template>
