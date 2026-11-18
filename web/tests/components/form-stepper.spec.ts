import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { mount } from '@vue/test-utils'
import FormStepper, { type WizardStep } from '../../src/components/FormStepper.vue'
import FormWizardLayout from '../../src/components/FormWizardLayout.vue'
import { useAuthStore } from '../../src/stores/auth'
import { MockShipitApi } from '../../src/api/mock'
import { setApi } from '../../src/api'
import { router } from '../../src/router'

describe('FormStepper & FormWizardLayout', () => {
  const sampleSteps: WizardStep[] = [
    { id: 's1', label: 'Bước 1', status: 'complete' },
    { id: 's2', label: 'Bước 2', status: 'current' },
    { id: 's3', label: 'Bước 3', status: 'error' },
    { id: 's4', label: 'Bước 4', status: 'pending' }
  ]

  beforeEach(async () => {
    setActivePinia(createPinia())
    setApi(new MockShipitApi())
    const authStore = useAuthStore()
    authStore.user = { username: 'admin' }
    authStore.initialized = true
    await router.push('/pipelines/new')
    await router.isReady()
  })

  it('renders step statuses and emits select on click and keyboard arrows', async () => {
    const wrapper = mount(FormStepper, {
      props: {
        steps: sampleSteps,
        currentStep: 1,
        canNavigateTo: (idx: number) => idx <= 2
      }
    })

    const buttons = wrapper.findAll('button')
    expect(buttons).toHaveLength(4)
    expect(buttons[0].attributes('data-step-status')).toBe('complete')
    expect(buttons[1].attributes('aria-current')).toBe('step')
    expect(buttons[2].attributes('data-step-status')).toBe('error')
    expect(buttons[3].attributes('disabled')).toBeDefined()

    await buttons[0].trigger('click')
    expect(wrapper.emitted('select')?.[0]).toEqual([0])

    await buttons[1].trigger('keydown', { key: 'ArrowRight' })
    expect(wrapper.emitted('select')?.[1]).toEqual([2])

    await buttons[1].trigger('keydown', { key: 'ArrowLeft' })
    expect(wrapper.emitted('select')?.[2]).toEqual([0])
  })

  it('FormWizardLayout renders Tiếp tục on middle steps and saveLabel on last step with internal scroll', () => {
    const middleWrapper = mount(FormWizardLayout, {
      props: {
        title: 'Thêm pipeline',
        backTo: '/pipelines',
        breadcrumbParent: 'Pipeline',
        breadcrumbCurrent: 'Thêm mới',
        steps: sampleSteps,
        currentStep: 1,
        saveLabel: 'Tạo pipeline'
      },
      global: {
        plugins: [createPinia(), router]
      },
      slots: {
        default: '<div class="step-body">Nội dung bước 2</div>'
      }
    })

    expect(middleWrapper.text()).toContain('Quay lại')
    expect(middleWrapper.text()).toContain('Tiếp tục')
    expect(middleWrapper.text()).not.toContain('Tạo pipeline')

    const lastWrapper = mount(FormWizardLayout, {
      props: {
        title: 'Thêm pipeline',
        backTo: '/pipelines',
        breadcrumbParent: 'Pipeline',
        breadcrumbCurrent: 'Thêm mới',
        steps: sampleSteps,
        currentStep: 3,
        saveLabel: 'Tạo pipeline'
      },
      global: {
        plugins: [createPinia(), router]
      },
      slots: {
        default: '<div class="step-body">Nội dung bước cuối</div>'
      }
    })

    expect(lastWrapper.text()).toContain('Tạo pipeline')
    expect(lastWrapper.text()).not.toContain('Tiếp tục')
  })
})
