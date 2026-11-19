import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { defineComponent } from 'vue'
import { mount, flushPromises } from '@vue/test-utils'
import { useShortcuts, resetShortcutsForTest } from '../../src/composables/useShortcuts'
import ShortcutsOverlay from '../../src/components/ShortcutsOverlay.vue'

describe('useShortcuts & ShortcutsOverlay', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    resetShortcutsForTest()
  })

  afterEach(() => {
    vi.useRealTimers()
    document.body.innerHTML = ''
  })

  it('registers single and chord shortcuts, ignores typing in inputs, and unregisters on unmount', () => {
    const onNew = vi.fn()
    const onGotoRuns = vi.fn()

    const Comp = defineComponent({
      setup() {
        const { registerMany } = useShortcuts()
        registerMany([
          { id: 'new', keys: 'n', label: 'Tạo mới', scope: 'Danh sách', handler: onNew },
          { id: 'goto-runs', keys: 'g r', label: 'Lượt chạy', scope: 'Điều hướng', handler: onGotoRuns }
        ])
        return {}
      },
      template: '<div />'
    })

    const wrapper = mount(Comp)

    // Single key 'n'
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'n', bubbles: true }))
    expect(onNew).toHaveBeenCalledTimes(1)

    // Typing inside input is ignored
    const input = document.createElement('input')
    document.body.appendChild(input)
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'n', bubbles: true }))
    expect(onNew).toHaveBeenCalledTimes(1)

    // Chord 'g' then 'r'
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'g', bubbles: true }))
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'r', bubbles: true }))
    expect(onGotoRuns).toHaveBeenCalledTimes(1)

    // Unmount removes handlers
    wrapper.unmount()
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'n', bubbles: true }))
    expect(onNew).toHaveBeenCalledTimes(1)
  })

  it('reports shortcut conflicts within overlapping scopes', () => {
    const Comp = defineComponent({
      setup() {
        const { register, conflicts } = useShortcuts()
        register({ id: 'first', keys: 'n', label: 'First', scope: 'Toàn cục', handler: () => {} })
        register({ id: 'second', keys: 'n', label: 'Second', scope: 'Toàn cục', handler: () => {} })
        return { conflicts }
      },
      template: '<div />'
    })

    const wrapper = mount(Comp)
    expect(wrapper.vm.conflicts).toHaveLength(1)
    expect(wrapper.vm.conflicts[0]).toMatchObject({
      keys: 'n',
      existingId: 'first',
      incomingId: 'second'
    })
    wrapper.unmount()
  })

  it('renders ShortcutsOverlay grouped by scope and closes on Escape', async () => {
    const Comp = defineComponent({
      components: { ShortcutsOverlay },
      setup() {
        const { register, openOverlay } = useShortcuts()
        register({ id: 'help', keys: '?', label: 'Hiện phím tắt', scope: 'Toàn cục', handler: openOverlay })
        return { openOverlay }
      },
      template: '<ShortcutsOverlay />'
    })

    const wrapper = mount(Comp, { attachTo: document.body })
    expect(document.body.querySelector('[data-testid="shortcuts-dialog"]')).toBeNull()

    wrapper.vm.openOverlay()
    await flushPromises()
    const dialog = document.body.querySelector('[data-testid="shortcuts-dialog"]')
    expect(dialog).not.toBeNull()
    expect(dialog?.textContent).toContain('Toàn cục')
    expect(dialog?.textContent).toContain('Hiện phím tắt')

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    await flushPromises()
    expect(document.body.querySelector('[data-testid="shortcuts-dialog"]')).toBeNull()
    wrapper.unmount()
  })
})
