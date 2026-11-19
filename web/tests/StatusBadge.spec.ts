import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import StatusBadge from '../src/components/StatusBadge.vue'
import type { BadgeStatus } from '../src/components/StatusBadge.vue'

describe('StatusBadge', () => {
  const cases: { status: BadgeStatus; label: string; glyph: string }[] = [
    { status: 'queued', label: 'Đang chờ', glyph: '◌' },
    { status: 'running', label: 'Đang chạy', glyph: '▲' },
    { status: 'success', label: 'Thành công', glyph: '●' },
    { status: 'failed', label: 'Thất bại', glyph: '✕' },
    { status: 'cancelled', label: 'Đã huỷ', glyph: '○' },
    { status: 'skipped', label: 'Bỏ qua', glyph: '─' },
    { status: 'pending', label: 'Chưa chạy', glyph: '○' }
  ]

  cases.forEach(({ status, label, glyph }) => {
    it(`renders correct D1 glyph and label for status "${status}"`, () => {
      const wrapper = mount(StatusBadge, {
        props: { status }
      })

      expect(wrapper.text()).toContain(glyph)
      expect(wrapper.text()).toContain(label)
      expect(wrapper.attributes('data-status')).toBe(status)

      const glyphEl = wrapper.find('span[aria-hidden="true"]')
      expect(glyphEl.exists()).toBe(true)

      if (status === 'running') {
        expect(glyphEl.classes()).toContain('live-pulse')
      } else {
        expect(glyphEl.classes()).not.toContain('live-pulse')
      }
    })
  })

  it('renders cancelled with neutral muted color and without red error classes', () => {
    const wrapper = mount(StatusBadge, {
      props: { status: 'cancelled' }
    })
    expect(wrapper.text()).toContain('Đã huỷ')
    expect(wrapper.classes().some((c) => c.includes('danger'))).toBe(false)
    expect(wrapper.classes().some((c) => c.includes('fg-muted'))).toBe(true)
  })
})
