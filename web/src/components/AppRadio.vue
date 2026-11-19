<script setup lang="ts">
import { inject, computed, ref, onMounted, onBeforeUnmount } from 'vue'
import { RADIO_GROUP_KEY, type RadioGroupContext } from './AppRadioGroup.vue'

const props = withDefaults(
  defineProps<{
    value: string | number
    modelValue?: string | number
    label?: string
    description?: string
    id?: string
    disabled?: boolean
  }>(),
  {
    modelValue: undefined,
    label: undefined,
    description: undefined,
    id: undefined,
    disabled: false
  }
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: string | number): void
  (e: 'change', value: string | number): void
}>()

const group = inject<RadioGroupContext | null>(RADIO_GROUP_KEY, null)
const controlRef = ref<HTMLElement | null>(null)

const isDisabled = computed(() => props.disabled || Boolean(group?.disabled.value))
const currentVal = computed(() => (group ? group.modelValue.value : props.modelValue))
const isChecked = computed(() => currentVal.value === props.value)

const tabindex = computed(() => {
  if (isDisabled.value) return -1
  if (!group) return 0
  if (isChecked.value) return 0
  if (currentVal.value === undefined) return 0
  return -1
})

function select() {
  if (isDisabled.value) return
  if (group) {
    group.selectValue(props.value)
  } else {
    emit('update:modelValue', props.value)
    emit('change', props.value)
  }
}

function onKeydown(event: KeyboardEvent) {
  if (isDisabled.value) return
  if (event.key === ' ') {
    event.preventDefault()
    select()
    return
  }
  if (!group) return
  if (event.key === 'ArrowDown' || event.key === 'ArrowRight') {
    event.preventDefault()
    group.moveFocus(props.value, 1)
  } else if (event.key === 'ArrowUp' || event.key === 'ArrowLeft') {
    event.preventDefault()
    group.moveFocus(props.value, -1)
  }
}

onMounted(() => {
  if (group && controlRef.value) {
    group.registerOption(props.value, controlRef.value, isDisabled.value)
  }
})

onBeforeUnmount(() => {
  if (group) {
    group.unregisterOption(props.value)
  }
})
</script>

<template>
  <label
    :for="id"
    :class="[
      'inline-flex items-start gap-2 font-mono select-none group',
      isDisabled ? 'opacity-50 cursor-not-allowed' : 'cursor-pointer'
    ]"
    @click.prevent="select"
  >
    <span
      ref="controlRef"
      :id="id"
      role="radio"
      :tabindex="tabindex"
      :aria-checked="isChecked ? 'true' : 'false'"
      :aria-disabled="isDisabled ? 'true' : undefined"
      :class="[
        'inline-flex items-center justify-center text-xs font-bold leading-5 shrink-0 rounded-[2px] transition-colors duration-[80ms]',
        'focus-visible:outline-2 focus-visible:outline-border-focus focus-visible:outline-offset-2',
        isChecked ? 'text-phosphor' : 'text-fg-muted group-hover:text-fg-strong'
      ]"
      @keydown="onKeydown"
    >
      {{ isChecked ? '(o)' : '( )' }}
    </span>
    <span v-if="label || description || $slots.default" class="flex flex-col gap-0.5 leading-5">
      <span class="text-xs text-fg group-hover:text-fg-strong">
        <slot>{{ label }}</slot>
      </span>
      <span v-if="description" class="text-[11px] text-fg-muted leading-snug">
        {{ description }}
      </span>
    </span>
  </label>
</template>
