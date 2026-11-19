import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { mount, flushPromises } from '@vue/test-utils'
import AppLayout from '../../src/layouts/AppLayout.vue'
import { useAuthStore } from '../../src/stores/auth'
import { MockShipitApi } from '../../src/api/mock'
import { setApi } from '../../src/api'
import { router } from '../../src/router'

describe('AppLayout Sidebar & Topbar', () => {
  beforeEach(async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    setApi(new MockShipitApi())

    const authStore = useAuthStore()
    authStore.user = { username: 'admin' }
    authStore.initialized = true

    await router.push('/')
    await router.isReady()
  })

  it('renders grouped sidebar with Vận hành and Thiết lập sections and footer', () => {
    const wrapper = mount(AppLayout, {
      global: {
        plugins: [createPinia(), router]
      },
      slots: {
        default: '<h1>Nội dung trang</h1>'
      }
    })

    const nav = wrapper.find('aside nav')
    expect(nav.exists()).toBe(true)

    const navText = nav.text()
    expect(navText).toContain('Vận hành')
    expect(navText).toContain('Lượt chạy')
    expect(navText).toContain('Pipeline')
    expect(navText).toContain('Job')

    expect(navText).toContain('Thiết lập')
    expect(navText).toContain('Máy build')
    expect(navText).toContain('Thông tin xác thực')
    expect(navText).toContain('Allowlist')

    expect(navText).not.toContain('Deployment được phép')
    expect(navText).not.toContain('Allowlist Job')

    // Footer preserved
    const asideText = wrapper.find('aside').text()
    expect(asideText).toContain('Shipit v0.1')
    expect(asideText).toContain('Sẵn sàng')
  })

  it('does not render an h1 inside the topbar', () => {
    const wrapper = mount(AppLayout, {
      global: {
        plugins: [createPinia(), router]
      },
      slots: {
        default: '<h1>Lượt chạy</h1>'
      }
    })

    const header = wrapper.find('header')
    expect(header.exists()).toBe(true)
    expect(header.find('h1').exists()).toBe(false)
    expect(wrapper.findAll('h1')).toHaveLength(1)
  })

  it('uses h-full fixed desktop shell with no vertical scroll on main', () => {
    const wrapper = mount(AppLayout, {
      global: {
        plugins: [createPinia(), router]
      },
      slots: {
        default: '<h1>Lượt chạy</h1>'
      }
    })

    const root = wrapper.find('div.h-full')
    expect(root.exists()).toBe(true)
    expect(root.classes()).toContain('overflow-hidden')
    expect(root.classes()).toContain('flex-row')
    expect(root.classes()).not.toContain('h-dvh')
    expect(root.classes().some((c) => c.startsWith('md:') || c.startsWith('sm:') || c.startsWith('lg:'))).toBe(false)

    const aside = wrapper.find('aside')
    expect(aside.classes().some((c) => c.startsWith('md:') || c.startsWith('sm:'))).toBe(false)

    const main = wrapper.find('main')
    expect(main.exists()).toBe(true)
    expect(main.classes()).not.toContain('overflow-y-auto')
    expect(main.classes()).not.toContain('overflow-y-scroll')
    expect(main.classes()).toContain('overflow-y-hidden')
    expect(main.classes()).toContain('overflow-x-auto')
    expect(main.classes()).toContain('flex-1')
    expect(main.classes()).toContain('min-h-0')

    const innerContainer = main.find('div')
    expect(innerContainer.classes()).toContain('min-w-[1024px]')
    expect(innerContainer.classes()).toContain('flex-1')
    expect(innerContainer.classes()).toContain('min-h-0')
  })

  it('executes command bar commands (goto, theme) and renders bottom status bar', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const authStore = useAuthStore()
    authStore.user = { username: 'admin' }
    authStore.initialized = true

    const wrapper = mount(AppLayout, {
      global: {
        plugins: [pinia, router]
      },
      slots: {
        default: '<h1>Lượt chạy</h1>'
      }
    })

    const statusBar = wrapper.find('[data-testid="status-bar"]')
    expect(statusBar.exists()).toBe(true)
    expect(statusBar.text()).toContain('TRỰC TIẾP')
    expect(statusBar.text()).toContain('đang chạy')

    const cmdInput = wrapper.find('[data-testid="command-bar-input"]')
    await cmdInput.setValue('goto pipelines')
    await cmdInput.trigger('keydown', { key: 'Enter' })
    await flushPromises()
    await router.isReady()

    expect(router.currentRoute.value.path).toBe('/pipelines')
    expect(wrapper.find('[data-testid="status-bar-console"]').text()).toContain('/pipelines')
  })
})
