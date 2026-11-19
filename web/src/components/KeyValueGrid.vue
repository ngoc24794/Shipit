<script setup lang="ts">
import CopyButton from './CopyButton.vue'

export interface KeyValueItem {
  key?: string
  label: string
  value?: string | number | null
  copyable?: boolean
  mono?: boolean
  title?: string
  tone?: 'default' | 'phosphor' | 'accent' | 'warn' | 'danger'
}

withDefaults(
  defineProps<{
    items: KeyValueItem[]
    labelWidth?: string
  }>(),
  {
    labelWidth: '110px'
  }
)

function toneClass(tone?: KeyValueItem['tone']): string {
  switch (tone) {
    case 'phosphor':
      return 'text-phosphor'
    case 'accent':
      return 'text-accent'
    case 'warn':
      return 'text-warn'
    case 'danger':
      return 'text-danger'
    default:
      return 'text-fg-strong'
  }
}
</script>

<template>
  <dl
    class="grid gap-x-2 gap-y-1.5 p-2 bg-bg-surface border border-border rounded-[2px] text-xs"
    :style="{ gridTemplateColumns: `${labelWidth} 1fr` }"
    data-testid="key-value-grid"
  >
    <template v-for="(item, idx) in items" :key="item.key || `${item.label}-${idx}`">
      <dt class="text-fg-muted whitespace-nowrap truncate flex items-center" :title="item.label">
        {{ item.label }}
      </dt>
      <dd
        :class="[
          'min-w-0 flex items-center justify-between gap-1.5',
          toneClass(item.tone),
          item.mono !== false ? 'font-mono tabular-nums' : ''
        ]"
      >
        <span
          class="truncate min-w-0"
          :title="item.title ?? (item.value !== null && item.value !== undefined ? String(item.value) : '')"
        >
          <slot :name="`value-${item.key || idx}`" :item="item">
            {{ item.value !== null && item.value !== undefined && item.value !== '' ? item.value : '-' }}
          </slot>
        </span>
        <CopyButton
          v-if="item.copyable && item.value !== null && item.value !== undefined && item.value !== ''"
          :value="String(item.value)"
          :aria-label="`Sao chép ${item.label}`"
        />
      </dd>
    </template>
  </dl>
</template>
