import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { mount } from '@vue/test-utils'
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

  it('does not render an h1 inside the desktop topbar', () => {
    const wrapper = mount(AppLayout, {
      global: {
        plugins: [createPinia(), router]
      },
      slots: {
        default: '<h1>Lượt chạy</h1>'
      }
    })

    const desktopHeader = wrapper.findAll('header')[1]
    expect(desktopHeader.exists()).toBe(true)
    expect(desktopHeader.find('h1').exists()).toBe(false)
    expect(wrapper.findAll('h1')).toHaveLength(1)
  })

  it('uses 100dvh shell with overflow-hidden and main as the only scrolling region', () => {
    const wrapper = mount(AppLayout, {
      global: {
        plugins: [createPinia(), router]
      },
      slots: {
        default: '<h1>Lượt chạy</h1>'
      }
    })

    const root = wrapper.find('div.h-dvh')
    expect(root.exists()).toBe(true)
    expect(root.classes()).toContain('overflow-hidden')

    const main = wrapper.find('main')
    expect(main.exists()).toBe(true)
    expect(main.classes()).toContain('overflow-y-auto')
    expect(main.classes()).toContain('flex-1')
    expect(main.classes()).toContain('min-h-0')
  })
})
