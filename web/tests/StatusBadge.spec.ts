import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import StatusBadge from '../src/components/StatusBadge.vue'
import type { BadgeStatus } from '../src/components/StatusBadge.vue'

describe('StatusBadge', () => {
  const cases: { status: BadgeStatus; label: string }[] = [
    { status: 'queued', label: 'Đang chờ' },
    { status: 'running', label: 'Đang chạy' },
    { status: 'success', label: 'Thành công' },
    { status: 'failed', label: 'Thất bại' },
    { status: 'cancelled', label: 'Đã dừng' },
    { status: 'skipped', label: 'Bỏ qua' },
    { status: 'pending', label: 'Chưa chạy' }
  ]

  cases.forEach(({ status, label }) => {
    it(`renders correct label and icon for status "${status}"`, () => {
      const wrapper = mount(StatusBadge, {
        props: { status }
      })

      expect(wrapper.text()).toContain(label)
      // Check data-status attribute
      expect(wrapper.attributes('data-status')).toBe(status)
      // Check SVG icon exists
      const svg = wrapper.find('svg')
      expect(svg.exists()).toBe(true)

      if (status === 'running') {
        expect(svg.classes()).toContain('animate-spin')
      } else {
        expect(svg.classes()).not.toContain('animate-spin')
      }
    })
  })

  it('renders cancelled with neutral/amber color and without red error classes', () => {
    const wrapper = mount(StatusBadge, {
      props: { status: 'cancelled' }
    })
    expect(wrapper.text()).toContain('Đã dừng')
    expect(wrapper.classes().some((c) => c.includes('danger'))).toBe(false)
    expect(wrapper.classes().some((c) => c.includes('warn'))).toBe(true)
  })
})
