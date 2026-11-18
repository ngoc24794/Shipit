<script setup lang="ts">
import { FileText } from 'lucide-vue-next'
import type { RunTask } from '../../api/types'
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
</script>

<template>
  <div class="w-full flex-1 min-h-0 overflow-auto rounded-[6px] border border-border bg-card">
    <table class="w-full text-left text-xs tabular-nums border-collapse">
      <thead class="sticky top-0 z-10 bg-muted/95 backdrop-blur-xs text-[11px] font-semibold text-muted-fg border-b border-border">
        <tr>
          <th scope="col" class="py-1.5 px-2 w-10 whitespace-nowrap">#</th>
          <th scope="col" class="py-1.5 px-2 whitespace-nowrap">Tác vụ</th>
          <th scope="col" class="py-1.5 px-2 whitespace-nowrap">Trạng thái</th>
          <th scope="col" class="py-1.5 px-2 whitespace-nowrap">Mã</th>
          <th scope="col" class="py-1.5 px-2 whitespace-nowrap">Thời lượng</th>
          <th scope="col" class="py-1.5 px-2 text-right whitespace-nowrap">Log</th>
        </tr>
      </thead>
      <tbody class="divide-y divide-border text-fg">
        <tr v-if="tasks.length === 0">
          <td colspan="6" class="py-4 px-3 text-center text-muted-fg text-xs italic">
            Chưa có tác vụ nào được cấu hình
          </td>
        </tr>
        <tr
          v-for="task in tasks"
          :key="task.position"
          :data-task-position="task.position"
          :data-task-failed="task.status === 'failed' ? 'true' : undefined"
          :data-task-selected="selectedPosition === task.position ? 'true' : undefined"
          :class="[
            'cursor-pointer transition-colors',
            selectedPosition === task.position
              ? 'bg-primary/10 hover:bg-primary/15'
              : task.status === 'failed'
                ? 'bg-danger/10 hover:bg-danger/15'
                : 'hover:bg-muted/50'
          ]"
          @click="emit('viewLog', task.position)"
        >
          <!-- STT -->
          <td
            :class="[
              'py-1.5 px-2 font-mono font-medium text-[11px] text-muted-fg whitespace-nowrap',
              task.status === 'failed' ? 'border-l-2 border-l-danger text-danger font-semibold' : ''
            ]"
          >
            #{{ task.position + 1 }}
          </td>

          <!-- Nhãn -->
          <td class="py-1.5 px-2 font-medium text-fg max-w-[130px] truncate" :title="task.label">
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
              class="inline-flex items-center gap-1 font-mono text-[11px] px-1.5 py-0.5 rounded bg-danger/10 text-danger border border-danger/30 font-semibold"
              title="Quá thời gian thực thi (mã 124)"
            >
              <span>124</span>
              <span>(Quá thời gian)</span>
            </span>
            <span
              v-else-if="task.exitCode !== null && task.exitCode !== undefined && task.exitCode !== 0"
              class="inline-flex items-center font-mono text-[11px] px-1.5 py-0.5 rounded bg-danger/10 text-danger border border-danger/30 font-semibold"
            >
              {{ task.exitCode }}
            </span>
            <span
              v-else-if="task.exitCode === 0"
              class="inline-flex items-center font-mono text-[11px] px-1.5 py-0.5 rounded bg-muted text-muted-fg border border-border/60"
            >
              0
            </span>
            <span v-else class="text-muted-fg text-[11px]">-</span>
          </td>

          <!-- Thời lượng -->
          <td class="py-1.5 px-2 text-[11px] font-mono tabular-nums text-muted-fg whitespace-nowrap">
            {{ formatDuration(task.startedAt, task.finishedAt) }}
          </td>

          <!-- Nút Xem log tác vụ -->
          <td class="py-1.5 px-2 text-right whitespace-nowrap">
            <button
              type="button"
              class="inline-flex items-center justify-center p-1 rounded-[4px] text-muted-fg hover:text-primary hover:bg-primary/10 transition-colors cursor-pointer focus-visible:outline-2 focus-visible:outline-ring"
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
