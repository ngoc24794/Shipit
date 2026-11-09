import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import LogViewer from '../src/components/LogViewer.vue'
import type { LogLine } from '../src/api/types'

describe('LogViewer', () => {
  const sampleLines: LogLine[] = [
    { seq: 1, step: 'checkout', stream: 'system', text: 'Bắt đầu checkout...', at: '2026-11-09T08:00:00Z' },
    { seq: 2, step: 'checkout', stream: 'stdout', text: 'Cloning repo...', at: '2026-11-09T08:00:01Z' },
    { seq: 3, step: 'build', stream: 'stderr', text: 'Warning: build notice', at: '2026-11-09T08:00:05Z' }
  ]

  it('renders log lines with seq and stream styles', () => {
    const wrapper = mount(LogViewer, {
      props: {
        lines: sampleLines,
        status: 'running'
      }
    })

    expect(wrapper.text()).toContain('Bắt đầu checkout...')
    expect(wrapper.text()).toContain('Cloning repo...')
    expect(wrapper.text()).toContain('Warning: build notice')
    expect(wrapper.text()).toContain('1')
    expect(wrapper.text()).toContain('2')
    expect(wrapper.text()).toContain('3')

    const logContainer = wrapper.find('[role="log"]')
    expect(logContainer.exists()).toBe(true)
    expect(logContainer.attributes('aria-live')).toBe('off')
  })

  it('displays run completion status in status area', () => {
    const wrapper = mount(LogViewer, {
      props: {
        lines: sampleLines,
        status: 'success'
      }
    })

    const statusEl = wrapper.find('[role="status"]')
    expect(statusEl.exists()).toBe(true)
    expect(statusEl.text()).toContain('Run đã kết thúc: thành công')
  })

  it('handles auto-scroll and stop on scroll-up, and toggles with Follow button', async () => {
    const wrapper = mount(LogViewer, {
      props: {
        lines: sampleLines,
        status: 'running'
      }
    })

    const scrollContainer = wrapper.find('[role="log"]').element as HTMLElement
    Object.defineProperty(scrollContainer, 'scrollHeight', { value: 1000, writable: true })
    Object.defineProperty(scrollContainer, 'clientHeight', { value: 400, writable: true })
    Object.defineProperty(scrollContainer, 'scrollTop', { value: 600, writable: true }) // at bottom (1000 - 600 - 400 = 0 <= 25)

    // Simulate user scrolling up (scrollTop = 200, so remaining is 1000 - 200 - 400 = 400 > 25)
    scrollContainer.scrollTop = 200
    await wrapper.find('[role="log"]').trigger('scroll')

    const followButton = wrapper.find('button[aria-pressed]')
    expect(followButton.attributes('aria-pressed')).toBe('false')

    // Clicking Follow button resumes auto-scroll
    await followButton.trigger('click')
    expect(followButton.attributes('aria-pressed')).toBe('true')
    expect(scrollContainer.scrollTop).toBe(1000)
  })
})
