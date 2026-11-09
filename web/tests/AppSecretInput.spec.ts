import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import AppSecretInput from '../src/components/AppSecretInput.vue'

describe('AppSecretInput', () => {
  it('renders password input by default', () => {
    const wrapper = mount(AppSecretInput)
    const input = wrapper.find('input')
    expect(input.attributes('type')).toBe('password')
  })

  it('toggles visibility when eye button is clicked', async () => {
    const wrapper = mount(AppSecretInput)
    const button = wrapper.find('button')
    const input = wrapper.find('input')

    expect(input.attributes('type')).toBe('password')
    expect(button.attributes('aria-label')).toBe('Hiện giá trị bí mật')

    await button.trigger('click')
    expect(input.attributes('type')).toBe('text')
    expect(button.attributes('aria-label')).toBe('Ẩn giá trị bí mật')

    await button.trigger('click')
    expect(input.attributes('type')).toBe('password')
    expect(button.attributes('aria-label')).toBe('Hiện giá trị bí mật')
  })

  it('never pre-fills old secret value and displays correct placeholder when hasExistingValue is true', () => {
    const wrapper = mount(AppSecretInput, {
      props: {
        hasExistingValue: true,
        modelValue: ''
      }
    })

    const input = wrapper.find('input')
    expect(input.element.value).toBe('')
    expect(input.attributes('placeholder')).toBe('Đã lưu — để trống để giữ nguyên')
  })

  it('emits update:modelValue when user inputs text', async () => {
    const wrapper = mount(AppSecretInput)
    const input = wrapper.find('input')

    await input.setValue('new-secret-123')
    expect(wrapper.emitted('update:modelValue')).toBeTruthy()
    expect(wrapper.emitted('update:modelValue')![0]).toEqual(['new-secret-123'])
  })
})
