import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { mount, flushPromises } from '@vue/test-utils'
import AllowlistView from '../../src/views/config/AllowlistView.vue'
import { useAuthStore } from '../../src/stores/auth'
import { MockShipitApi } from '../../src/api/mock'
import { setApi } from '../../src/api'
import { router } from '../../src/router'

describe('AllowlistView (Merged Allowlist Tabs)', () => {
  let pinia: ReturnType<typeof createPinia>

  beforeEach(async () => {
    pinia = createPinia()
    setActivePinia(pinia)
    setApi(new MockShipitApi())

    const authStore = useAuthStore()
    authStore.user = { username: 'admin' }
    authStore.initialized = true

    await router.push('/settings/allowlist')
    await router.isReady()
  })

  it('defaults to Deployment tab and renders a single h1', async () => {
    const wrapper = mount(AllowlistView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()

    const h1s = wrapper.findAll('h1')
    expect(h1s).toHaveLength(1)
    expect(h1s[0].text()).toContain('Allowlist')

    const deployTab = wrapper.find('#tab-deployment')
    const jobTab = wrapper.find('#tab-job')
    expect(deployTab.attributes('aria-selected')).toBe('true')
    expect(jobTab.attributes('aria-selected')).toBe('false')

    expect(wrapper.find('#panel-deployment').exists()).toBe(true)
    expect(wrapper.find('#panel-job').exists()).toBe(false)
    expect(wrapper.text()).toContain('shipit-demo')
  })

  it('switches between Deployment and Job tabs and syncs query parameter', async () => {
    const wrapper = mount(AllowlistView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()

    await wrapper.find('#tab-job').trigger('click')
    await new Promise((resolve) => setTimeout(resolve, 150))
    await flushPromises()

    expect(router.currentRoute.value.query.tab).toBe('job')
    expect(wrapper.find('#tab-job').attributes('aria-selected')).toBe('true')
    expect(wrapper.find('#panel-job').exists()).toBe(true)
    expect(wrapper.find('#panel-deployment').exists()).toBe(false)
    expect(wrapper.text()).toContain('avina-files-config')
    expect(wrapper.findAll('h1')).toHaveLength(1)

    await wrapper.find('#tab-deployment').trigger('click')
    await new Promise((resolve) => setTimeout(resolve, 150))
    await flushPromises()

    expect(router.currentRoute.value.query.tab).toBe('deployment')
    expect(wrapper.find('#panel-deployment').exists()).toBe(true)
  })

  it('opens Job tab directly when ?tab=job is in URL', async () => {
    await router.push('/settings/allowlist?tab=job')
    await router.isReady()

    const wrapper = mount(AllowlistView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()

    expect(wrapper.find('#tab-job').attributes('aria-selected')).toBe('true')
    expect(wrapper.find('#panel-job').exists()).toBe(true)
    expect(wrapper.text()).toContain('avina-files-config')
  })
})
