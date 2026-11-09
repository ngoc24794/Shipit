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

  it('renders normal stderr run without red error styles and without ERR badge', () => {
    const normalStderrLines: LogLine[] = [
      { seq: 1, step: 'checkout', stream: 'stderr', text: "Cloning into '/tmp/shipit/run-3'...", at: '2026-11-09T08:00:00Z' },
      { seq: 2, step: 'build', stream: 'stderr', text: '#0 building with "default" instance using docker driver', at: '2026-11-09T08:00:01Z' },
      { seq: 3, step: 'build', stream: 'stderr', text: '#8 CACHED', at: '2026-11-09T08:00:02Z' },
      { seq: 4, step: 'build', stream: 'stderr', text: '#4 transferring context: 250B done', at: '2026-11-09T08:00:03Z' }
    ]

    const wrapper = mount(LogViewer, {
      props: {
        lines: normalStderrLines,
        status: 'success'
      }
    })

    // Must NOT contain ERR badge label
    expect(wrapper.text()).not.toContain('ERR')
    expect(wrapper.text()).not.toContain('[err]')
    expect(wrapper.text()).not.toContain('[ERR]')

    // Must NOT contain red error color classes on log line elements
    const redElements = wrapper.findAll('.text-rose-400, .text-danger')
    expect(redElements.length).toBe(0)

    // Muted lines should use muted text style (text-slate-400)
    const mutedElements = wrapper.findAll('.text-slate-400')
    // Toolbar heading also uses text-slate-400, so we check that line elements have text-slate-400
    expect(mutedElements.length).toBeGreaterThanOrEqual(4)
  })

  it('renders ERR badge for real error line and WARN badge for warning line', () => {
    const mixedLines: LogLine[] = [
      { seq: 1, step: 'build', stream: 'stderr', text: '#9 ERROR: failed to calculate checksum of ref ...: "/src": not found', at: '2026-11-09T08:00:01Z' },
      { seq: 2, step: 'build', stream: 'stderr', text: 'WARNING! Your password will be stored unencrypted in /home/devops/.docker/config.json.', at: '2026-11-09T08:00:02Z' },
      { seq: 3, step: 'build', stream: 'stderr', text: 'Cloning into /tmp/shipit/run-3...', at: '2026-11-09T08:00:03Z' }
    ]

    const wrapper = mount(LogViewer, {
      props: {
        lines: mixedLines,
        status: 'failed'
      }
    })

    // Line 1 should display ERR badge
    expect(wrapper.text()).toContain('ERR')
    // Line 2 should display WARN badge
    expect(wrapper.text()).toContain('WARN')

    // Find the badge spans
    const badges = wrapper.findAll('.w-10')
    expect(badges.length).toBe(3)
    expect(badges[0].text()).toBe('ERR')
    expect(badges[1].text()).toBe('WARN')
    expect(badges[2].text()).toBe('') // Normal stderr has empty badge column for alignment
  })

  it('sets title tooltip attribute to line stream name on each line', () => {
    const lines: LogLine[] = [
      { seq: 1, step: 'checkout', stream: 'system', text: 'Cloning https://...', at: '2026-11-09T08:00:00Z' },
      { seq: 2, step: 'checkout', stream: 'stdout', text: '160abc7', at: '2026-11-09T08:00:01Z' },
      { seq: 3, step: 'build', stream: 'stderr', text: '#8 CACHED', at: '2026-11-09T08:00:02Z' }
    ]

    const wrapper = mount(LogViewer, {
      props: {
        lines,
        status: 'running'
      }
    })

    const rows = wrapper.findAll('[role="log"] > div:not(.text-slate-500)')
    expect(rows.length).toBe(3)
    expect(rows[0].attributes('title')).toBe('system')
    expect(rows[1].attributes('title')).toBe('stdout')
    expect(rows[2].attributes('title')).toBe('stderr')
  })

  it('copies logs preserving original text without injecting ERR/WARN labels', async () => {
    const writeTextMock = vi.fn().mockResolvedValue(undefined)
    Object.assign(navigator, {
      clipboard: {
        writeText: writeTextMock
      }
    })

    const lines: LogLine[] = [
      { seq: 1, step: 'build', stream: 'stderr', text: '#9 ERROR: failed to solve', at: '2026-11-09T08:00:01Z' },
      { seq: 2, step: 'build', stream: 'stderr', text: 'WARNING! Unencrypted config', at: '2026-11-09T08:00:02Z' },
      { seq: 3, step: 'build', stream: 'stdout', text: 'Build completed', at: '2026-11-09T08:00:03Z' }
    ]

    const wrapper = mount(LogViewer, {
      props: {
        lines,
        status: 'failed'
      }
    })

    const copyBtn = wrapper.find('button[aria-label="Sao chép nhật ký"]')
    await copyBtn.trigger('click')

    expect(writeTextMock).toHaveBeenCalledTimes(1)
    const copiedText = writeTextMock.mock.calls[0][0] as string

    // Must contain original text and stream prefix
    expect(copiedText).toContain('[STDERR] #9 ERROR: failed to solve')
    expect(copiedText).toContain('[STDERR] WARNING! Unencrypted config')
    expect(copiedText).toContain('[STDOUT] Build completed')

    // Must NOT contain separate ERR or WARN badge tags inserted into copied text
    expect(copiedText).not.toContain('[ERR]')
    expect(copiedText).not.toContain('[WARN]')
  })
})
