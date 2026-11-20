<script setup lang="ts">
import { FileText } from 'lucide-vue-next'
import type { RunTask, StepStatus } from '../../api/types'
import StatusBadge from '../StatusBadge.vue'

const props = withDefaults(
  defineProps<{
    tasks?: RunTask[]
    selectedPosition?: number | null
  }>(),
  {
    tasks: () => [],
    selectedPosition: null
  }
)

const emit = defineEmits<{
  (e: 'viewLog', taskPosition: number): void
}>()

function formatDuration(startedAt?: string | null, finishedAt?: string | null): string {
  if (!startedAt) return '-'
  const start = new Date(startedAt).getTime()
  if (isNaN(start)) return '-'
  const end = finishedAt ? new Date(finishedAt).getTime() : Date.now()
  const sec = Math.max(0, Math.floor((end - start) / 1000))
  if (sec < 60) return `${sec}s`
  const min = Math.floor(sec / 60)
  const remSec = sec % 60
  return `${min}m ${remSec}s`
}

function statusGlyph(status: StepStatus): { glyph: string; cls: string } {
  switch (status) {
    case 'success':
      return { glyph: '●', cls: 'text-phosphor' }
    case 'running':
      return { glyph: '▲', cls: 'text-accent' }
    case 'failed':
      return { glyph: '✕', cls: 'text-danger' }
    case 'cancelled':
      return { glyph: '○', cls: 'text-warn' }
    default:
      return { glyph: '◌', cls: 'text-fg-dim' }
  }
}
</script>

<template>
  <div
    class="w-full flex-1 min-h-0 overflow-auto rounded-[2px] border border-border bg-bg-elevated font-mono"
    data-testid="task-table-scroll"
  >
    <table class="min-w-[500px] w-full text-left text-xs tabular-nums border-collapse table-fixed">
      <colgroup>
        <col class="w-[72px]" />
        <col class="w-[130px]" />
        <col class="w-[116px]" />
        <col class="w-[52px]" />
        <col class="w-[84px]" />
        <col class="w-[46px]" />
      </colgroup>
      <thead class="sticky top-0 z-10 bg-bg-surface text-[10px] font-bold uppercase tracking-wider text-fg-muted border-b border-border-strong">
        <tr>
          <th scope="col" class="py-1.5 px-2 whitespace-nowrap overflow-hidden text-ellipsis">#</th>
          <th scope="col" class="py-1.5 px-2 whitespace-nowrap overflow-hidden text-ellipsis">Tác vụ</th>
          <th scope="col" class="py-1.5 px-2 whitespace-nowrap overflow-hidden text-ellipsis">Trạng thái</th>
          <th scope="col" class="py-1.5 px-2 whitespace-nowrap overflow-hidden text-ellipsis">Mã</th>
          <th scope="col" class="py-1.5 px-2 whitespace-nowrap overflow-hidden text-ellipsis">Thời lượng</th>
          <th scope="col" class="py-1.5 px-2 text-right whitespace-nowrap overflow-hidden text-ellipsis">Log</th>
        </tr>
      </thead>
      <tbody class="divide-y divide-border text-fg">
        <tr v-if="tasks.length === 0">
          <td colspan="6" class="py-4 px-3 text-center text-fg-muted text-xs italic">
            Chưa có tác vụ nào được cấu hình
          </td>
        </tr>
        <tr
          v-for="(task, idx) in tasks"
          :key="task.position"
          :data-task-position="task.position"
          :data-task-failed="task.status === 'failed' ? 'true' : undefined"
          :data-task-selected="selectedPosition === task.position ? 'true' : undefined"
          :class="[
            'cursor-pointer transition-colors',
            selectedPosition === task.position
              ? 'bg-bg-selected hover:bg-bg-selected'
              : task.status === 'failed'
                ? 'bg-danger-bg hover:bg-danger-bg'
                : 'hover:bg-bg-hover'
          ]"
          @click="emit('viewLog', task.position)"
        >
          <!-- Tree Glyph + STT -->
          <td
            :class="[
              'py-1.5 px-2 font-mono font-medium text-[11px] text-fg-muted whitespace-nowrap',
              task.status === 'failed' ? 'border-l-2 border-l-danger text-danger font-semibold' : ''
            ]"
          >
            <span class="text-fg-dim mr-1" aria-hidden="true">{{ idx === tasks.length - 1 ? '└─' : '├─' }}</span>
            <span :class="['mr-1', statusGlyph(task.status).cls]" aria-hidden="true">{{ statusGlyph(task.status).glyph }}</span>
            <span>#{{ task.position + 1 }}</span>
          </td>

          <!-- Nhãn -->
          <td class="py-1.5 px-2 font-medium text-fg-strong truncate" :title="task.label">
            {{ task.label }}
          </td>

          <!-- StatusBadge -->
          <td class="py-1.5 px-2 whitespace-nowrap">
            <StatusBadge
              :status="task.status"
              size="sm"
              :label="task.status === 'pending' ? 'Đang chờ' : (task.status === 'cancelled' ? 'Đã huỷ' : undefined)"
            />
          </td>

          <!-- Exit Code -->
          <td class="py-1.5 px-2 whitespace-nowrap">
            <span
              v-if="task.exitCode === 124"
              class="inline-flex items-center gap-1 font-mono text-[11px] px-1.5 py-0.5 rounded-[2px] bg-danger-bg text-danger border border-danger/30 font-semibold"
              title="Quá thời gian thực thi (mã 124)"
            >
              <span>124</span>
              <span>(Quá thời gian)</span>
            </span>
            <span
              v-else-if="task.exitCode !== null && task.exitCode !== undefined && task.exitCode !== 0"
              class="inline-flex items-center font-mono text-[11px] px-1.5 py-0.5 rounded-[2px] bg-danger-bg text-danger border border-danger/30 font-semibold"
            >
              {{ task.exitCode }}
            </span>
            <span
              v-else-if="task.exitCode === 0"
              class="inline-flex items-center font-mono text-[11px] px-1.5 py-0.5 rounded-[2px] bg-bg-surface text-fg-muted border border-border"
            >
              0
            </span>
            <span v-else class="text-fg-dim text-[11px]">-</span>
          </td>

          <!-- Thời lượng -->
          <td class="py-1.5 px-2 text-[11px] font-mono tabular-nums text-fg-muted whitespace-nowrap">
            {{ formatDuration(task.startedAt, task.finishedAt) }}
          </td>

          <!-- Nút Xem log tác vụ -->
          <td class="py-1.5 px-2 text-right whitespace-nowrap">
            <button
              type="button"
              class="inline-flex items-center justify-center p-1 rounded-[2px] text-fg-muted hover:text-phosphor hover:bg-phosphor-bg transition-colors cursor-pointer focus-visible:outline-2 focus-visible:outline-border-focus"
              :aria-label="`Xem log tác vụ ${task.label}`"
              title="Xem log tác vụ"
              @click.stop="emit('viewLog', task.position)"
            >
              <FileText class="w-3.5 h-3.5" aria-hidden="true" />
            </button>
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
