import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import StepTimeline from '../src/components/StepTimeline.vue'
import type { RunStep } from '../src/api/types'

describe('StepTimeline', () => {
  const steps: RunStep[] = [
    {
      name: 'checkout',
      status: 'success',
      startedAt: '2026-11-09T08:00:00Z',
      finishedAt: '2026-11-09T08:00:15Z'
    },
    {
      name: 'build',
      status: 'running',
      startedAt: '2026-11-09T08:00:15Z',
      finishedAt: null
    },
    {
      name: 'push',
      status: 'pending',
      startedAt: null,
      finishedAt: null
    },
    {
      name: 'deploy',
      status: 'pending',
      startedAt: null,
      finishedAt: null
    }
  ]

  it('renders all four pipeline steps in order with appropriate labels and icons', () => {
    const wrapper = mount(StepTimeline, {
      props: { steps }
    })

    expect(wrapper.text()).toContain('Checkout')
    expect(wrapper.text()).toContain('Build')
    expect(wrapper.text()).toContain('Push')
    expect(wrapper.text()).toContain('Deploy')

    const stepElements = wrapper.findAll('li')
    expect(stepElements.length).toBe(4)

    expect(stepElements[0].attributes('data-step-name')).toBe('checkout')
    expect(stepElements[0].attributes('data-step-status')).toBe('success')
    expect(stepElements[0].text()).toContain('15s')

    expect(stepElements[1].attributes('data-step-name')).toBe('build')
    expect(stepElements[1].attributes('data-step-status')).toBe('running')
    expect(stepElements[1].text()).toContain('Đang chạy...')

    expect(stepElements[2].attributes('data-step-name')).toBe('push')
    expect(stepElements[2].attributes('data-step-status')).toBe('pending')

    expect(stepElements[3].attributes('data-step-name')).toBe('deploy')
    expect(stepElements[3].attributes('data-step-status')).toBe('pending')
  })
})
