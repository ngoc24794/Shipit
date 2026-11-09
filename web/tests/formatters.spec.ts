import { describe, it, expect } from 'vitest'
import {
  formatRunStatus,
  formatStepStatus,
  formatFailureReason,
  formatDateTime,
  formatDuration
} from '../src/views/runs/formatters'

describe('Run formatters', () => {
  it('formats cancelled status as "Đã dừng"', () => {
    expect(formatRunStatus('cancelled')).toBe('Đã dừng')
    expect(formatStepStatus('cancelled')).toBe('Đã dừng')
  })

  it('formats other run and step statuses correctly', () => {
    expect(formatRunStatus('queued')).toBe('Đang chờ')
    expect(formatRunStatus('running')).toBe('Đang chạy')
    expect(formatRunStatus('success')).toBe('Thành công')
    expect(formatRunStatus('failed')).toBe('Thất bại')

    expect(formatStepStatus('pending')).toBe('Chưa chạy')
    expect(formatStepStatus('skipped')).toBe('Bỏ qua')
  })

  it('formats failure reasons correctly', () => {
    expect(formatFailureReason('interrupted')).toBe(
      'Lượt chạy bị gián đoạn đột ngột (tiến trình dừng hoặc hệ thống khởi động lại)'
    )
    expect(formatFailureReason('cancelled by user')).toBe('Đã dừng bởi người dùng')
    expect(formatFailureReason('Some custom error')).toBe('Some custom error')
    expect(formatFailureReason(null)).toBe('')
  })

  it('formats datetime and duration correctly', () => {
    expect(formatDateTime(null)).toBe('-')
    expect(formatDateTime('invalid-date')).toBe('-')
    expect(formatDateTime('2026-11-09T08:00:00Z')).toMatch(/\d{2}:\d{2}:\d{2} \d{1,2}\/\d{1,2}\/\d{4}/)

    expect(formatDuration(null, null)).toBe('-')
    expect(formatDuration('2026-11-09T08:00:00Z', '2026-11-09T08:00:25Z')).toBe('25s')
    expect(formatDuration('2026-11-09T08:00:00Z', '2026-11-09T08:02:15Z')).toBe('2m 15s')
  })
})
