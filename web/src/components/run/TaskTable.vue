<script setup lang="ts">
import { FileText } from 'lucide-vue-next'
import type { RunTask } from '../../api/types'
import StatusBadge from '../StatusBadge.vue'

const props = withDefaults(
  defineProps<{
    tasks?: RunTask[]
  }>(),
  {
    tasks: () => []
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
  <div class="w-full overflow-x-auto rounded-[8px] border border-border bg-card">
    <table class="w-full text-left text-sm tabular-nums border-collapse">
      <thead class="bg-muted/80 backdrop-blur-xs text-xs font-semibold text-muted-fg uppercase tracking-wider border-b border-border">
        <tr>
          <th scope="col" class="py-2.5 px-4 w-16">#</th>
          <th scope="col" class="py-2.5 px-4">Tác vụ</th>
          <th scope="col" class="py-2.5 px-4">Trạng thái</th>
          <th scope="col" class="py-2.5 px-4">Mã thoát</th>
          <th scope="col" class="py-2.5 px-4">Thời lượng</th>
          <th scope="col" class="py-2.5 px-4 text-right">Nhật ký</th>
        </tr>
      </thead>
      <tbody class="divide-y divide-border text-fg">
        <tr v-if="tasks.length === 0">
          <td colspan="6" class="py-4 px-4 text-center text-muted-fg text-xs italic">
            Chưa có tác vụ nào được cấu hình
          </td>
        </tr>
        <tr
          v-for="task in tasks"
          :key="task.position"
          :data-task-position="task.position"
          class="hover:bg-muted/50 transition-colors"
        >
          <!-- STT -->
          <td class="py-2.5 px-4 font-mono font-medium text-xs text-muted-fg whitespace-nowrap">
            #{{ task.position + 1 }}
          </td>

          <!-- Nhãn -->
          <td class="py-2.5 px-4 font-medium text-fg whitespace-nowrap">
            {{ task.label }}
          </td>

          <!-- StatusBadge -->
          <td class="py-2.5 px-4 whitespace-nowrap">
            <StatusBadge
              :status="task.status"
              size="sm"
              :label="task.status === 'pending' ? 'Đang chờ' : (task.status === 'cancelled' ? 'Đã huỷ' : undefined)"
            />
          </td>

          <!-- Exit Code -->
          <td class="py-2.5 px-4 whitespace-nowrap">
            <span
              v-if="task.exitCode === 124"
              class="inline-flex items-center gap-1 font-mono text-xs px-2 py-0.5 rounded bg-danger/10 text-danger border border-danger/30 font-semibold"
              title="Quá thời gian thực thi (mã 124)"
            >
              <span>124</span>
              <span>(Quá thời gian)</span>
            </span>
            <span
              v-else-if="task.exitCode !== null && task.exitCode !== undefined && task.exitCode !== 0"
              class="inline-flex items-center font-mono text-xs px-2 py-0.5 rounded bg-danger/10 text-danger border border-danger/30 font-semibold"
            >
              {{ task.exitCode }}
            </span>
            <span
              v-else-if="task.exitCode === 0"
              class="inline-flex items-center font-mono text-xs px-2 py-0.5 rounded bg-muted text-muted-fg border border-border/60"
            >
              0
            </span>
            <span v-else class="text-muted-fg text-xs">-</span>
          </td>

          <!-- Thời lượng -->
          <td class="py-2.5 px-4 text-xs font-mono tabular-nums text-muted-fg whitespace-nowrap">
            {{ formatDuration(task.startedAt, task.finishedAt) }}
          </td>

          <!-- Nút Xem log tác vụ -->
          <td class="py-2.5 px-4 text-right whitespace-nowrap">
            <button
              type="button"
              class="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-[6px] text-xs font-medium text-muted-fg hover:text-fg hover:bg-muted border border-border transition-colors cursor-pointer focus-visible:outline-2 focus-visible:outline-ring"
              :aria-label="`Xem log tác vụ ${task.label}`"
              @click="emit('viewLog', task.position)"
            >
              <FileText class="w-3.5 h-3.5" aria-hidden="true" />
              <span>Xem log tác vụ</span>
            </button>
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
