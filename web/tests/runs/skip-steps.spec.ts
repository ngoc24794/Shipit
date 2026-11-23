import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { nextTick } from 'vue'
import { mount } from '@vue/test-utils'
import {
  applyStepToggle,
  formatSkipSummary,
  isStepCheckboxDisabled,
  validateSkipSteps
} from '../../src/utils/runSteps'
import RunDialog from '../../src/components/RunDialog.vue'
import { HttpShipitApi } from '../../src/api/client'
import { MockShipitApi } from '../../src/api/mock'

describe('Run Skip Steps (S2)', () => {
  describe('runSteps helper & suffix rule', () => {
    it('enforces trailing suffix rule when toggling pipeline steps', () => {
      // Unchecking deploy skips only deploy
      expect(applyStepToggle('pipeline', [], 'deploy', false)).toEqual(['deploy'])
      // Unchecking push skips push and deploy
      expect(applyStepToggle('pipeline', [], 'push', false)).toEqual(['push', 'deploy'])
      // Unchecking build skips build, push, deploy
      expect(applyStepToggle('pipeline', [], 'build', false)).toEqual(['build', 'push', 'deploy'])
      // Checkout cannot be unchecked
      expect(applyStepToggle('pipeline', [], 'checkout', false)).toEqual([])
      // Re-checking build enables build while keeping push, deploy skipped
      expect(applyStepToggle('pipeline', ['build', 'push', 'deploy'], 'build', true)).toEqual([
        'push',
        'deploy'
      ])
    })

    it('disables checkout always and disables steps after an unchecked step', () => {
      expect(isStepCheckboxDisabled('pipeline', [], 'checkout')).toBe(true)
      expect(isStepCheckboxDisabled('pipeline', [], 'build')).toBe(false)
      expect(isStepCheckboxDisabled('pipeline', ['build', 'push', 'deploy'], 'push')).toBe(true)
      expect(isStepCheckboxDisabled('pipeline', ['build', 'push', 'deploy'], 'deploy')).toBe(true)
      expect(isStepCheckboxDisabled('job', ['push', 'runjob'], 'runjob')).toBe(true)
    })

    it('formats Vietnamese skip summary', () => {
      expect(formatSkipSummary('pipeline', [])).toBe('Chạy đầy đủ tất cả các bước')
      expect(formatSkipSummary('pipeline', ['deploy'])).toBe('Bỏ qua: Deploy')
      expect(formatSkipSummary('pipeline', ['push', 'deploy'])).toBe('Bỏ qua: Push, Deploy')
      expect(formatSkipSummary('job', ['runjob'])).toBe('Bỏ qua: Chạy Job')
    })

    it('validates skipSteps against contract', () => {
      expect(validateSkipSteps('pipeline', [])).toBeNull()
      expect(validateSkipSteps('pipeline', ['deploy'])).toBeNull()
      expect(validateSkipSteps('pipeline', ['push', 'deploy'])).toBeNull()
      expect(validateSkipSteps('job', ['push', 'runjob'])).toBeNull()
      expect(validateSkipSteps('pipeline', ['checkout'])).toContain('checkout')
      expect(validateSkipSteps('pipeline', ['build'])).toContain('suffix')
      expect(validateSkipSteps('pipeline', ['deploy', 'deploy'])).toContain('Duplicate')
    })
  })

  describe('RunDialog & RunStepsSelector', () => {
    it('renders steps in order, enforces suffix uncheck/disable, and emits skipSteps on confirm', async () => {
      const wrapper = mount(RunDialog, {
        props: {
          modelValue: true,
          kind: 'pipeline',
          targetName: 'avina-api'
        },
        attachTo: document.body
      })
      await nextTick()

      const checkoutRow = document.body.querySelector('[data-testid="run-step-row-checkout"]')!
      const pushRow = document.body.querySelector('[data-testid="run-step-row-push"]')!
      const deployRow = document.body.querySelector('[data-testid="run-step-row-deploy"]')!
      const summary = document.body.querySelector('[data-testid="run-steps-summary"]')!

      expect(checkoutRow).toBeTruthy()
      expect(summary.textContent).toContain('Chạy đầy đủ tất cả các bước')

      // Checkout checkbox is disabled
      const checkoutCb = checkoutRow.querySelector('[role="checkbox"]') as HTMLElement
      expect(checkoutCb.getAttribute('aria-disabled')).toBe('true')

      // Uncheck Push -> Push & Deploy both skipped, Deploy disabled
      const pushCb = pushRow.querySelector('[role="checkbox"]') as HTMLElement
      pushCb.click()
      await nextTick()

      const deployCb = deployRow.querySelector('[role="checkbox"]') as HTMLElement
      expect(deployCb.getAttribute('aria-checked')).toBe('false')
      expect(deployCb.getAttribute('aria-disabled')).toBe('true')
      expect(summary.textContent).toContain('Bỏ qua: Push, Deploy')

      // Click Chạy -> emits confirm with ['push', 'deploy']
      const confirmBtn = document.body.querySelector('[data-testid="run-dialog-confirm"]') as HTMLButtonElement
      confirmBtn.click()
      await nextTick()

      expect(wrapper.emitted('confirm')).toEqual([[['push', 'deploy']]])
      wrapper.unmount()
    })

    it('renders Job steps with "Chạy Job" as final step', async () => {
      const wrapper = mount(RunDialog, {
        props: {
          modelValue: true,
          kind: 'job',
          targetName: 'db-migrator'
        },
        attachTo: document.body
      })
      await nextTick()

      const runjobRow = document.body.querySelector('[data-testid="run-step-row-runjob"]')!
      expect(runjobRow).toBeTruthy()
      expect(runjobRow.textContent).toContain('Chạy Job')

      const runjobCb = runjobRow.querySelector('[role="checkbox"]') as HTMLElement
      runjobCb.click()
      await nextTick()

      const summary = document.body.querySelector('[data-testid="run-steps-summary"]')!
      expect(summary.textContent).toContain('Bỏ qua: Chạy Job')

      const confirmBtn = document.body.querySelector('[data-testid="run-dialog-confirm"]') as HTMLButtonElement
      confirmBtn.click()
      await nextTick()

      expect(wrapper.emitted('confirm')).toEqual([[['runjob']]])
      wrapper.unmount()
    })

    it('pre-fills defaultBranch and emits edited branch or tag on confirm', async () => {
      const wrapper = mount(RunDialog, {
        props: {
          modelValue: false,
          kind: 'pipeline',
          targetName: 'avina-api',
          defaultBranch: 'dev'
        },
        attachTo: document.body
      })
      await wrapper.setProps({ modelValue: true })
      await nextTick()

      const input = document.body.querySelector('[data-testid="run-dialog-branch"] input') as HTMLInputElement
      expect(input).toBeTruthy()
      expect(input.value).toBe('dev')

      input.value = 'v1.2.0'
      input.dispatchEvent(new Event('input'))
      await nextTick()

      const confirmBtn = document.body.querySelector('[data-testid="run-dialog-confirm"]') as HTMLButtonElement
      confirmBtn.click()
      await nextTick()

      expect(wrapper.emitted('confirm')).toEqual([[[], 'v1.2.0']])
      wrapper.unmount()
    })
  })

  describe('HttpShipitApi request body', () => {
    afterEach(() => {
      vi.restoreAllMocks()
    })

    it('omits body when nothing is skipped and sends {skipSteps} only when skipped', async () => {
      const fetchMock = vi.fn().mockResolvedValue({
        ok: true,
        status: 200,
        headers: new Headers({ 'content-type': 'application/json' }),
        json: async () => ({ id: 10, status: 'queued', createdAt: '2026-11-20T00:00:00Z' })
      })
      vi.stubGlobal('fetch', fetchMock)

      const api = new HttpShipitApi()

      await api.triggerRun(5)
      expect(fetchMock).toHaveBeenLastCalledWith(
        '/api/pipelines/5/runs',
        expect.not.objectContaining({ body: expect.anything() })
      )

      await api.triggerRun(5, { skipSteps: [] })
      expect(fetchMock).toHaveBeenLastCalledWith(
        '/api/pipelines/5/runs',
        expect.not.objectContaining({ body: expect.anything() })
      )

      await api.triggerRun(5, { skipSteps: ['deploy'] })
      expect(fetchMock).toHaveBeenLastCalledWith(
        '/api/pipelines/5/runs',
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({ skipSteps: ['deploy'] })
        })
      )

      await api.triggerJobRun(7, { skipSteps: ['push', 'runjob'] })
      expect(fetchMock).toHaveBeenLastCalledWith(
        '/api/jobs/7/runs',
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({ skipSteps: ['push', 'runjob'] })
        })
      )

      await api.triggerRun(5, { gitBranch: 'v1.0.0' })
      expect(fetchMock).toHaveBeenLastCalledWith(
        '/api/pipelines/5/runs',
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({ gitBranch: 'v1.0.0' })
        })
      )

      await api.triggerJobRun(7, { skipSteps: ['runjob'], gitBranch: 'release/2.0' })
      expect(fetchMock).toHaveBeenLastCalledWith(
        '/api/jobs/7/runs',
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({ skipSteps: ['runjob'], gitBranch: 'release/2.0' })
        })
      )
    })
  })

  describe('MockShipitApi skipSteps', () => {
    let mockApi: MockShipitApi

    beforeEach(async () => {
      mockApi = new MockShipitApi()
      await mockApi.login({ username: 'admin', password: 'admin123' })
    })

    it('marks skipped steps on created pipeline and job runs and rejects invalid non-suffix skips', async () => {
      const idlePipeline = await mockApi.createPipeline({
        name: 'idle-skip-test',
        gitUrl: 'https://git.example.com/idle.git',
        gitBranch: 'main',
        gitCredentialId: 2,
        slaveId: 1,
        imageName: 'idle-app',
        registryHost: 'registry.example.com',
        registryCredentialId: null,
        deployNamespace: 'default',
        deployDeployment: 'idle-app',
        deployContainer: 'app'
      })
      const pRun = await mockApi.triggerRun(idlePipeline.id, { skipSteps: ['push', 'deploy'] })
      const pDetail = await mockApi.getRun(pRun.id)
      expect(pDetail.steps.find((s) => s.name === 'checkout')?.status).toBe('pending')
      expect(pDetail.steps.find((s) => s.name === 'build')?.status).toBe('pending')
      expect(pDetail.steps.find((s) => s.name === 'push')?.status).toBe('skipped')
      expect(pDetail.steps.find((s) => s.name === 'deploy')?.status).toBe('skipped')

      // Job 1
      const jRun = await mockApi.triggerJobRun(1, { skipSteps: ['runjob'] })
      const jDetail = await mockApi.getRun(jRun.id)
      expect(jDetail.steps.find((s) => s.name === 'push')?.status).toBe('pending')
      expect(jDetail.steps.find((s) => s.name === 'runjob')?.status).toBe('skipped')
      expect(jDetail.tasks?.every((t) => t.status === 'skipped')).toBe(true)

      await expect(mockApi.triggerRun(idlePipeline.id, { skipSteps: ['build'] })).rejects.toThrow(/suffix/i)
    })
  })
})
