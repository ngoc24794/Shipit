import { describe, it, expect, vi, afterEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import AppDialog from '../../src/components/AppDialog.vue'
import ConfirmDialog from '../../src/components/ConfirmDialog.vue'
import AppDrawer from '../../src/components/AppDrawer.vue'
import AppToast from '../../src/components/AppToast.vue'
import AppTooltip from '../../src/components/AppTooltip.vue'
import AppPopover from '../../src/components/AppPopover.vue'
import AppDropdownMenu, { type MenuItem } from '../../src/components/AppDropdownMenu.vue'
import ContextMenu from '../../src/components/ContextMenu.vue'
import { useToast } from '../../src/composables/useToast'

describe('AppDialog', () => {
  afterEach(() => {
    document.body.innerHTML = ''
    document.body.style.overflow = ''
  })

  it('renders with role="dialog", aria-modal, sizes, closes on Esc and overlay, and locks body scroll', async () => {
    const wrapper = mount(AppDialog, {
      attachTo: document.body,
      props: {
        modelValue: true,
        title: 'Chi tiết tác vụ',
        maxWidth: 'lg'
      },
      slots: {
        default: '<button id="inner-btn">OK</button>',
        footer: '<button id="footer-btn">Đóng</button>'
      }
    })
    await nextTick()

    const dialog = document.body.querySelector('[role="dialog"]') as HTMLElement
    expect(dialog).toBeTruthy()
    expect(dialog.getAttribute('aria-modal')).toBe('true')
    expect(dialog.getAttribute('aria-label')).toBe('Chi tiết tác vụ')
    expect(dialog.className).toContain('max-w-[740px]')
    expect(document.body.style.overflow).toBe('hidden')

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    await nextTick()
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual([false])
    expect(wrapper.emitted('close')).toHaveLength(1)

    await wrapper.setProps({ modelValue: false })
    await nextTick()
    expect(document.body.style.overflow).toBe('')
    wrapper.unmount()
  })
})

describe('ConfirmDialog', () => {
  afterEach(() => {
    document.body.innerHTML = ''
    document.body.style.overflow = ''
  })

  it('requires typed confirmation when confirmValue is provided', async () => {
    const wrapper = mount(ConfirmDialog, {
      attachTo: document.body,
      props: {
        modelValue: true,
        title: 'Xoá pipeline',
        danger: true,
        confirmValue: 'avina-api',
        confirmText: 'Xoá'
      }
    })
    await nextTick()

    const confirmBtn = Array.from(document.body.querySelectorAll('button')).find((b) =>
      b.textContent?.includes('Xoá')
    ) as HTMLButtonElement
    expect(confirmBtn.disabled).toBe(true)

    const input = document.body.querySelector('input[type="text"]') as HTMLInputElement
    input.value = 'avina-api'
    input.dispatchEvent(new Event('input'))
    await nextTick()

    expect(confirmBtn.disabled).toBe(false)
    confirmBtn.click()
    expect(wrapper.emitted('confirm')).toHaveLength(1)
    wrapper.unmount()
  })
})

describe('AppDrawer', () => {
  afterEach(() => {
    document.body.innerHTML = ''
    document.body.style.overflow = ''
  })

  it('renders left/right drawer with aria-modal and closes on Esc', async () => {
    const wrapper = mount(AppDrawer, {
      attachTo: document.body,
      props: {
        modelValue: true,
        title: 'Bộ lọc nâng cao',
        side: 'left',
        size: 'sm'
      },
      slots: {
        default: '<p>Nội dung drawer</p>'
      }
    })
    await nextTick()

    const dialog = document.body.querySelector('[role="dialog"]') as HTMLElement
    expect(dialog).toBeTruthy()
    expect(dialog.className).toContain('w-[360px]')
    expect(dialog.className).toContain('border-r')

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    await nextTick()
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual([false])
    wrapper.unmount()
  })
})

describe('AppToast & useToast', () => {
  afterEach(() => {
    vi.useRealTimers()
    const { toasts, clear, closeDrawer } = useToast()
    toasts.value = []
    clear()
    closeDrawer()
  })

  it('tracks entries, unreadCount, pauses auto-dismiss on hover, and opens console drawer', async () => {
    vi.useFakeTimers()
    const toast = useToast()
    const wrapper = mount(AppToast)

    toast.success('Đã kích hoạt pipeline', {
      duration: 2000,
      action: { label: 'Xem', onClick: vi.fn() }
    })
    await nextTick()

    expect(toast.unreadCount.value).toBe(1)
    expect(toast.entries.value).toHaveLength(1)
    expect(wrapper.text()).toContain('●')
    expect(wrapper.text()).toContain('Đã kích hoạt pipeline')
    expect(wrapper.text()).toContain('[Xem]')

    const toastEl = wrapper.get('[data-toast-type="success"]')
    await toastEl.trigger('mouseenter')
    await vi.advanceTimersByTimeAsync(3000)
    expect(toast.toasts.value).toHaveLength(1)

    await toastEl.trigger('mouseleave')
    await vi.advanceTimersByTimeAsync(2500)
    expect(toast.toasts.value).toHaveLength(0)

    toast.openDrawer()
    await nextTick()
    expect(toast.unreadCount.value).toBe(0)
    expect(wrapper.get('section[aria-label="Nhật ký thông báo"]').text()).toContain('Đã kích hoạt pipeline')
    wrapper.unmount()
  })
})

describe('AppTooltip', () => {
  afterEach(() => {
    document.body.innerHTML = ''
    vi.useRealTimers()
  })

  it('shows tooltip on focusin and links aria-describedby', async () => {
    const wrapper = mount(AppTooltip, {
      attachTo: document.body,
      props: { text: 'Chạy lại lượt này', delay: 0 },
      slots: { default: '<button type="button">Rerun</button>' }
    })

    await wrapper.get('span').trigger('focusin')
    await nextTick()

    const tip = document.body.querySelector('[role="tooltip"]')
    expect(tip?.textContent).toContain('Chạy lại lượt này')
    expect(wrapper.get('span').attributes('aria-describedby')).toBe(tip?.id)
    wrapper.unmount()
  })
})

describe('AppPopover', () => {
  afterEach(() => {
    document.body.innerHTML = ''
  })

  it('toggles popover on trigger click and closes on Esc', async () => {
    const wrapper = mount(AppPopover, {
      attachTo: document.body,
      slots: {
        trigger: '<template #trigger="{ toggle }"><button id="pop-btn" @click="toggle">Mở</button></template>',
        default: '<div id="pop-body">Nội dung popover</div>'
      }
    })

    await wrapper.get('#pop-btn').trigger('click')
    await nextTick()
    expect(document.body.querySelector('#pop-body')?.textContent).toContain('Nội dung popover')

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    await nextTick()
    expect(document.body.querySelector('#pop-body')).toBeNull()
    wrapper.unmount()
  })
})

describe('AppDropdownMenu & ContextMenu', () => {
  afterEach(() => {
    document.body.innerHTML = ''
  })

  const items: MenuItem[] = [
    { id: 'rerun', label: 'Chạy lại', shortcut: 'r' },
    { id: 'sep1', label: '', separator: true },
    { id: 'disabled', label: 'Khóa', disabled: true },
    { id: 'delete', label: 'Xoá', danger: true }
  ]

  it('supports keyboard roving and selection in AppDropdownMenu', async () => {
    const wrapper = mount(AppDropdownMenu, {
      attachTo: document.body,
      props: { items },
      slots: {
        trigger: '<template #trigger="{ toggle }"><button id="menu-btn" @click="toggle">Tác vụ</button></template>'
      }
    })

    await wrapper.get('#menu-btn').trigger('click')
    await nextTick()

    const menu = document.body.querySelector('[role="menu"]') as HTMLElement
    expect(menu).toBeTruthy()
    expect(menu.querySelectorAll('[role="menuitem"]')).toHaveLength(3)
    expect(menu.querySelectorAll('[role="separator"]')).toHaveLength(1)

    menu.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown' }))
    await nextTick()
    menu.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }))
    await nextTick()

    expect(wrapper.emitted('select')?.[0]?.[0]).toMatchObject({ id: 'delete' })
    wrapper.unmount()
  })

  it('opens ContextMenu on contextmenu event and emits select', async () => {
    const wrapper = mount(ContextMenu, {
      attachTo: document.body,
      props: { items },
      slots: {
        default: '<div id="ctx-target">Right click me</div>'
      }
    })

    await wrapper.get('#ctx-target').trigger('contextmenu', { clientX: 100, clientY: 120 })
    await nextTick()

    const menu = document.body.querySelector('[role="menu"]') as HTMLElement
    expect(menu).toBeTruthy()
    const firstItem = menu.querySelector('[role="menuitem"]') as HTMLButtonElement
    firstItem.click()
    await nextTick()

    expect(wrapper.emitted('select')?.[0]?.[0]).toMatchObject({ id: 'rerun' })
    wrapper.unmount()
  })
})
