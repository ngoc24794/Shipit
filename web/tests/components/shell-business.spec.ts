import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import SplitPane from '../../src/components/SplitPane.vue'
import CopyButton from '../../src/components/CopyButton.vue'
import KeyValueGrid from '../../src/components/KeyValueGrid.vue'
import RelativeTime from '../../src/components/RelativeTime.vue'
import DurationText from '../../src/components/DurationText.vue'
import TaskTable from '../../src/components/run/TaskTable.vue'

describe('Shell & Business Components', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  it('SplitPane supports keyboard resize and persists width in localStorage', async () => {
    const wrapper = mount(SplitPane, {
      props: {
        storageKey: 'test-split-key',
        defaultSize: 360,
        minSize: 280,
        maxSize: 520
      },
      slots: {
        left: '<div>Left</div>',
        right: '<div>Right</div>'
      }
    })

    const divider = wrapper.find('[data-testid="split-pane-divider"]')
    expect(divider.attributes('role')).toBe('separator')
    expect(divider.attributes('aria-valuenow')).toBe('360')

    await divider.trigger('keydown', { key: 'ArrowRight' })
    expect(divider.attributes('aria-valuenow')).toBe('376')
    expect(localStorage.getItem('test-split-key')).toBe('376')

    await divider.trigger('keydown', { key: 'Home' })
    expect(divider.attributes('aria-valuenow')).toBe('280')

    await divider.trigger('keydown', { key: 'End' })
    expect(divider.attributes('aria-valuenow')).toBe('520')
  })

  it('CopyButton copies value and emits copied event', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined)
    Object.assign(navigator, { clipboard: { writeText } })

    const wrapper = mount(CopyButton, {
      props: { value: 'sha-1234', label: 'Chép' }
    })

    await wrapper.find('[data-testid="copy-button"]').trigger('click')
    await flushPromises()

    expect(writeText).toHaveBeenCalledWith('sha-1234')
    expect(wrapper.emitted('copied')).toEqual([['sha-1234']])
    expect(wrapper.text()).toContain('Đã chép')
  })

  it('KeyValueGrid renders label/value pairs and copy buttons for copyable items', () => {
    const wrapper = mount(KeyValueGrid, {
      props: {
        items: [
          { key: 'sha', label: 'Tag SHA', value: 'abc1234', copyable: true },
          { key: 'branch', label: 'Nhánh', value: 'main', copyable: false }
        ]
      }
    })

    expect(wrapper.text()).toContain('Tag SHA')
    expect(wrapper.text()).toContain('abc1234')
    expect(wrapper.text()).toContain('Nhánh')
    expect(wrapper.text()).toContain('main')
    expect(wrapper.findAll('[data-testid="copy-button"]')).toHaveLength(1)
  })

  it('RelativeTime and DurationText format values properly', () => {
    const recentIso = new Date(Date.now() - 5 * 60 * 1000).toISOString()
    const rel = mount(RelativeTime, { props: { value: recentIso, live: false } })
    expect(rel.text()).toContain('5 phút trước')

    const dur = mount(DurationText, {
      props: {
        startedAt: '2026-11-20T00:00:00Z',
        finishedAt: '2026-11-20T00:02:15Z'
      }
    })
    expect(dur.text()).toBe('2m 15s')
  })

  it('TaskTable renders tmux tree glyphs and status glyphs', () => {
    const wrapper = mount(TaskTable, {
      props: {
        tasks: [
          { position: 0, label: 'migrate-files', status: 'success', exitCode: 0, startedAt: '2026-11-20T00:00:00Z', finishedAt: '2026-11-20T00:00:10Z' },
          { position: 1, label: 'migrate-users', status: 'failed', exitCode: 1, startedAt: '2026-11-20T00:00:10Z', finishedAt: '2026-11-20T00:00:20Z' }
        ]
      }
    })

    expect(wrapper.text()).toContain('├─')
    expect(wrapper.text()).toContain('└─')
    expect(wrapper.text()).toContain('●')
    expect(wrapper.text()).toContain('✕')
  })
})
