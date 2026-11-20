<script setup lang="ts">
import { ref, watch, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'

export interface TabItem {
  id: string
  label: string
  count?: number | string
  disabled?: boolean
}

const props = withDefaults(
  defineProps<{
    tabs: TabItem[]
    modelValue: string
    queryParam?: string
    ariaLabel?: string
  }>(),
  {
    queryParam: undefined,
    ariaLabel: 'Tabs'
  }
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: string): void
  (e: 'change', value: string): void
}>()

const tabRefs = ref<HTMLButtonElement[]>([])

let route: ReturnType<typeof useRoute> | null = null
let router: ReturnType<typeof useRouter> | null = null
try {
  route = useRoute()
  router = useRouter()
} catch {
  // Router may not be installed in isolated unit tests
}

function selectTab(id: string) {
  const tab = props.tabs.find((t) => t.id === id)
  if (!tab || tab.disabled) return
  if (id !== props.modelValue) {
    emit('update:modelValue', id)
    emit('change', id)
  }
  if (props.queryParam && route && router) {
    const currentQueryVal = route.query[props.queryParam]
    if (currentQueryVal !== id) {
      router.replace({
        query: { ...route.query, [props.queryParam]: id }
      })
    }
  }
}

onMounted(() => {
  if (props.queryParam && route) {
    const qVal = route.query[props.queryParam]
    if (typeof qVal === 'string' && props.tabs.some((t) => t.id === qVal && !t.disabled)) {
      if (qVal !== props.modelValue) {
        emit('update:modelValue', qVal)
      }
    }
  }
})

if (props.queryParam && route) {
  watch(
    () => route!.query[props.queryParam!],
    (qVal) => {
      if (typeof qVal === 'string' && qVal !== props.modelValue) {
        if (props.tabs.some((t) => t.id === qVal && !t.disabled)) {
          emit('update:modelValue', qVal)
        }
      }
    }
  )
}

function onKeydown(e: KeyboardEvent, idx: number) {
  const enabledIndices = props.tabs
    .map((t, i) => (!t.disabled ? i : -1))
    .filter((i) => i !== -1)
  if (enabledIndices.length === 0) return

  const pos = enabledIndices.indexOf(idx)
  let targetIdx = -1

  if (e.key === 'ArrowRight') {
    targetIdx = enabledIndices[(pos + 1) % enabledIndices.length]
  } else if (e.key === 'ArrowLeft') {
    targetIdx = enabledIndices[(pos - 1 + enabledIndices.length) % enabledIndices.length]
  } else if (e.key === 'Home') {
    targetIdx = enabledIndices[0]
  } else if (e.key === 'End') {
    targetIdx = enabledIndices[enabledIndices.length - 1]
  }

  if (targetIdx !== -1) {
    e.preventDefault()
    tabRefs.value[targetIdx]?.focus()
    selectTab(props.tabs[targetIdx].id)
  }
}
</script>

<template>
  <div
    role="tablist"
    :aria-label="ariaLabel"
    class="shrink-0 flex items-center gap-1 border-b border-border overflow-x-auto font-mono"
  >
    <button
      v-for="(tab, idx) in tabs"
      :key="tab.id"
      :ref="(el) => { if (el) tabRefs[idx] = el as HTMLButtonElement }"
      type="button"
      role="tab"
      :aria-selected="tab.id === modelValue"
      :tabindex="tab.id === modelValue ? 0 : -1"
      :disabled="tab.disabled"
      :class="[
        'inline-flex items-center gap-1.5 px-3 py-1.5 text-[12.5px] border border-transparent border-b-2 whitespace-nowrap transition-colors duration-[80ms] focus-visible:outline-2 focus-visible:outline-border-focus',
        tab.disabled
          ? 'opacity-45 cursor-not-allowed text-fg-dim'
          : tab.id === modelValue
            ? 'text-phosphor border-b-phosphor font-bold cursor-pointer'
            : 'text-fg-muted hover:text-fg-strong cursor-pointer'
      ]"
      @click="selectTab(tab.id)"
      @keydown="onKeydown($event, idx)"
    >
      <span>{{ tab.label }}</span>
      <span
        v-if="tab.count !== undefined"
        :class="[
          'px-1.5 py-0 text-[10.5px] rounded-[2px] tabular-nums border',
          tab.id === modelValue
            ? 'bg-[var(--phosphor-bg)] border-phosphor/30 text-phosphor'
            : 'bg-bg-surface border-border text-fg-muted'
        ]"
      >
        {{ tab.count }}
      </span>
    </button>
  </div>
</template>
