import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { mount } from '@vue/test-utils'
import LoginView from '../src/views/LoginView.vue'
import { useAuthStore } from '../src/stores/auth'
import { MockShipitApi } from '../src/api/mock'
import { setApi } from '../src/api'
import { router } from '../src/router'

describe('LoginView Component', () => {
  let mockApi: MockShipitApi

  beforeEach(async () => {
    setActivePinia(createPinia())
    mockApi = new MockShipitApi()
    setApi(mockApi)
  })

  it('renders "Ghi nhớ đăng nhập (30 ngày)" checkbox, unchecked by default with accessible label', () => {
    const wrapper = mount(LoginView, {
      global: {
        plugins: [router]
      }
    })

    const checkbox = wrapper.find<HTMLInputElement>('input#login-remember-me')
    expect(checkbox.exists()).toBe(true)
    expect(checkbox.attributes('type')).toBe('checkbox')
    expect(checkbox.element.checked).toBe(false)

    const label = wrapper.find('label[for="login-remember-me"]')
    expect(label.exists()).toBe(true)
    expect(label.text()).toContain('Ghi nhớ đăng nhập (30 ngày)')
  })

  it('submits rememberMe: true when checkbox is checked', async () => {
    const authStore = useAuthStore()
    const loginSpy = vi.spyOn(authStore, 'login').mockResolvedValue()

    const wrapper = mount(LoginView, {
      global: {
        plugins: [router]
      }
    })

    await wrapper.find('input#login-username').setValue('admin')
    await wrapper.find('input#login-password').setValue('testpassword')

    const checkbox = wrapper.find<HTMLInputElement>('input#login-remember-me')
    await checkbox.setValue(true)
    expect(checkbox.element.checked).toBe(true)

    await wrapper.find('form').trigger('submit.prevent')

    expect(loginSpy).toHaveBeenCalledWith({
      username: 'admin',
      password: 'testpassword',
      rememberMe: true
    })
  })

  it('submits rememberMe: false when checkbox is unchecked', async () => {
    const authStore = useAuthStore()
    const loginSpy = vi.spyOn(authStore, 'login').mockResolvedValue()

    const wrapper = mount(LoginView, {
      global: {
        plugins: [router]
      }
    })

    await wrapper.find('input#login-username').setValue('admin')
    await wrapper.find('input#login-password').setValue('testpassword')

    const checkbox = wrapper.find<HTMLInputElement>('input#login-remember-me')
    expect(checkbox.element.checked).toBe(false)

    await wrapper.find('form').trigger('submit.prevent')

    expect(loginSpy).toHaveBeenCalledWith({
      username: 'admin',
      password: 'testpassword',
      rememberMe: false
    })
  })
})
