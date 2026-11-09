import type { RunStatus, StepStatus } from '../../api/types'

export function formatDateTime(isoString?: string | null): string {
  if (!isoString) return '-'
  try {
    const d = new Date(isoString)
    if (isNaN(d.getTime())) return '-'
    const pad = (n: number) => n.toString().padStart(2, '0')
    const hours = pad(d.getHours())
    const minutes = pad(d.getMinutes())
    const seconds = pad(d.getSeconds())
    const day = pad(d.getDate())
    const month = pad(d.getMonth() + 1)
    const year = d.getFullYear()
    return `${hours}:${minutes}:${seconds} ${day}/${month}/${year}`
  } catch {
    return '-'
  }
}

export function formatDuration(
  startedAt?: string | null,
  finishedAt?: string | null,
  now: number = Date.now()
): string {
  if (!startedAt) return '-'
  const start = new Date(startedAt).getTime()
  if (isNaN(start)) return '-'
  const end = finishedAt ? new Date(finishedAt).getTime() : now
  const sec = Math.max(0, Math.floor((end - start) / 1000))
  if (sec < 60) return `${sec}s`
  const min = Math.floor(sec / 60)
  const remSec = sec % 60
  if (min < 60) return `${min}m ${remSec}s`
  const hours = Math.floor(min / 60)
  const remMin = min % 60
  return `${hours}h ${remMin}m ${remSec}s`
}

export function formatFailureReason(reason?: string | null): string {
  if (!reason) return ''
  const trimmed = reason.trim()
  if (trimmed.toLowerCase() === 'interrupted') {
    return 'Lượt chạy bị gián đoạn đột ngột (tiến trình dừng hoặc hệ thống khởi động lại)'
  }
  if (trimmed.toLowerCase() === 'cancelled by user') {
    return 'Đã dừng bởi người dùng'
  }
  return trimmed
}

export function formatRunStatus(status: RunStatus): string {
  switch (status) {
    case 'queued':
      return 'Đang chờ'
    case 'running':
      return 'Đang chạy'
    case 'success':
      return 'Thành công'
    case 'failed':
      return 'Thất bại'
    case 'cancelled':
      return 'Đã dừng'
    default:
      return status
  }
}

export function formatStepStatus(status: StepStatus): string {
  switch (status) {
    case 'pending':
      return 'Chưa chạy'
    case 'running':
      return 'Đang chạy'
    case 'success':
      return 'Thành công'
    case 'failed':
      return 'Thất bại'
    case 'skipped':
      return 'Bỏ qua'
    case 'cancelled':
      return 'Đã dừng'
    default:
      return status
  }
}
