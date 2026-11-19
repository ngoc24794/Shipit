import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { mount, flushPromises } from '@vue/test-utils'
import JobFormView from '../../src/views/jobs/JobFormView.vue'
import { useJobsAllowStore } from '../../src/stores/jobsAllow'
import { useAuthStore } from '../../src/stores/auth'
import { MockShipitApi } from '../../src/api/mock'
import { setApi } from '../../src/api'
import { router } from '../../src/router'
import { ApiError } from '../../src/api/client'
import type { JobInput } from '../../src/api/types'

describe('Job Form View & Store (Step-by-step Wizard)', () => {
  let mockApi: MockShipitApi
  let pinia: ReturnType<typeof createPinia>

  beforeEach(async () => {
    document.body.innerHTML = ''
    pinia = createPinia()
    setActivePinia(pinia)
    mockApi = new MockShipitApi()
    setApi(mockApi)
    const authStore = useAuthStore()
    authStore.user = { username: 'admin' }
    authStore.initialized = true
    await router.push('/jobs/new')
    await router.isReady()
  })

  it('allows free step navigation without validating until Save on final step', async () => {
    const wrapper = mount(JobFormView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()

    // Enter partial value on step 1 and move freely to step 2 without filling required fields
    await wrapper.find('input[placeholder*="VD: avina-dbmigrator"]').setValue('partial-job')
    await wrapper.findAll('button').find((b) => b.text().includes('Tiếp tục'))!.trigger('click')
    await wrapper.vm.$nextTick()

    expect(wrapper.text()).toContain('2. Build image')
    expect(wrapper.text()).not.toContain('Vui lòng nhập địa chỉ Git URL')
    expect(wrapper.find('button[data-step-id="source"]').attributes('data-step-status')).toBe('visited')

    // Jump directly to Step 5 (Review) via stepper click
    await wrapper.find('button[data-step-id="review"]').trigger('click')
    await wrapper.vm.$nextTick()
    expect(wrapper.text()).toContain('Kiểm tra & Xem trước YAML')

    // Step 1 value is preserved
    await wrapper.find('button[data-step-id="source"]').trigger('click')
    await wrapper.vm.$nextTick()
    expect((wrapper.find('input[placeholder*="VD: avina-dbmigrator"]').element as HTMLInputElement).value).toBe('partial-job')

    // Jump back to Step 5 and click Tạo Job -> validates all steps and marks error steps
    await wrapper.find('button[data-step-id="review"]').trigger('click')
    await wrapper.vm.$nextTick()
    await wrapper.findAll('button').find((b) => b.text().includes('Tạo Job'))!.trigger('click')
    await wrapper.vm.$nextTick()

    expect(wrapper.text()).toContain('Vui lòng nhập địa chỉ Git URL')
    expect(wrapper.find('button[data-step-id="source"]').attributes('data-step-status')).toBe('error')
  })

  it('restores step from ?step=3 query parameter', async () => {
    await router.push('/jobs/new?step=3')
    await router.isReady()

    const wrapper = mount(JobFormView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()

    expect(wrapper.text()).toContain('3. Nơi chạy')
    expect(wrapper.text()).toContain('avina-workspace')
  })

  it('populates dropdowns and ensures envFrom only contains names in allowlist', async () => {
    const allowStore = useJobsAllowStore()
    await allowStore.fetchEntries()

    await router.push('/jobs/new?step=3')
    await router.isReady()

    const wrapper = mount(JobFormView, {
      attachTo: document.body,
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()

    expect(wrapper.text()).toContain('avina-workspace')

    // Move to step 4 (Tác vụ)
    await router.replace('/jobs/new?step=4')
    await wrapper.vm.$nextTick()

    const comboboxes = wrapper.findAll('[role="combobox"]')
    const collectedOptions: string[] = []
    for (const cb of comboboxes) {
      await cb.trigger('click')
      await wrapper.vm.$nextTick()
      document.body.querySelectorAll('[role="option"]').forEach((opt) => {
        collectedOptions.push(opt.textContent?.trim() || '')
      })
      await cb.trigger('keydown', { key: 'Escape' })
    }

    expect(collectedOptions.some((t) => t.includes('avina-files-config'))).toBe(true)
    expect(collectedOptions.some((t) => t.includes('avina-users-config'))).toBe(true)
    expect(collectedOptions.some((t) => t.includes('avina-files-secret'))).toBe(true)
    expect(collectedOptions.some((t) => t.includes('avina-users-secret'))).toBe(true)
    expect(collectedOptions.some((t) => t.includes('root-secret') || t.includes('unallowed-secret'))).toBe(false)
    wrapper.unmount()
  })

  it('guarantees NO input exists for entering secret value (không có ô nhập giá trị secret)', async () => {
    await router.push('/jobs/new?step=4')
    await router.isReady()

    const wrapper = mount(JobFormView, {
      attachTo: document.body,
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()

    const addEnvBtn = wrapper.findAll('button').find((b) => b.text().includes('Thêm biến môi trường'))
    expect(addEnvBtn).toBeDefined()
    await addEnvBtn!.trigger('click')

    const sourceCombobox = wrapper.findAll('[role="combobox"]').find((cb) => cb.text().includes('Giá trị trực tiếp'))
    expect(sourceCombobox).toBeDefined()
    await sourceCombobox!.trigger('click')
    await wrapper.vm.$nextTick()

    const secretOpt = Array.from(document.body.querySelectorAll('[role="option"]')).find((o) =>
      o.textContent?.includes('Lấy từ Secret')
    ) as HTMLElement | undefined
    expect(secretOpt).toBeDefined()
    secretOpt!.click()
    await wrapper.vm.$nextTick()

    const inputs = wrapper.findAll('input')
    const placeholders = inputs.map((i) => i.attributes('placeholder') || '')
    const ariaLabels = inputs.map((i) => i.attributes('aria-label') || '')

    expect(placeholders.some((p) => p.toLowerCase().includes('key'))).toBe(true)
    expect(placeholders.some((p) => p.toLowerCase().includes('mật khẩu') || p.toLowerCase().includes('giá trị secret'))).toBe(false)
    expect(ariaLabels.some((a) => a.toLowerCase().includes('giá trị secret'))).toBe(false)
    wrapper.unmount()
  })

  it('enforces required timeout validation (timeout bắt buộc và > 0)', async () => {
    await router.push('/jobs/new?step=4')
    await router.isReady()

    const wrapper = mount(JobFormView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()

    const timeoutInput = wrapper.findAll('input').find((i) => i.attributes('placeholder') === '1800')
    expect(timeoutInput).toBeDefined()
    await timeoutInput!.setValue('')

    // Move to Step 5 and click Save to trigger full validation
    await wrapper.find('button[data-step-id="review"]').trigger('click')
    await wrapper.vm.$nextTick()
    await wrapper.findAll('button').find((b) => b.text().includes('Tạo Job'))!.trigger('click')
    await wrapper.vm.$nextTick()

    expect(wrapper.text()).toContain('Thời gian timeout bắt buộc và phải lớn hơn 0')
  })

  it('walks through all 5 steps and creates sample DbMigrator Job with 2 tasks', async () => {
    const createSpy = vi.spyOn(mockApi, 'createJob')

    const wrapper = mount(JobFormView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()

    // Step 1: Nguồn mã
    await wrapper.find('input[placeholder*="VD: avina-dbmigrator"]').setValue('avina-dbmigrator')
    await wrapper.find('input[placeholder*="http://git.example/AvinaWorkspace"]').setValue('http://git.example/AvinaWorkspace')
    await wrapper.find('input[placeholder="main"]').setValue('main')
    const gitCredSelect = wrapper.findAll('select').find((s) => s.text().includes('gitlab-token'))
    if (gitCredSelect) await gitCredSelect.setValue('2')

    await wrapper.findAll('button').find((b) => b.text().includes('Tiếp tục'))!.trigger('click')
    await wrapper.vm.$nextTick()
    expect(wrapper.text()).toContain('2. Build image')

    // Step 2: Build image
    const slaveSelect = wrapper.findAll('select').find((s) => s.text().includes('slave-staging-01'))
    if (slaveSelect) await slaveSelect.setValue('1')
    await wrapper.find('input[placeholder*="docker/Dockerfile.dbmigrator"]').setValue('docker/Dockerfile.dbmigrator')
    await wrapper.find('input[placeholder="avina-dbmigrator"]').setValue('avina-dbmigrator')
    await wrapper.find('input[placeholder="192.168.1.241"]').setValue('192.168.1.241')
    await wrapper.find('input[placeholder="."]').setValue('.')

    await wrapper.findAll('button').find((b) => b.text().includes('Tiếp tục'))!.trigger('click')
    await wrapper.vm.$nextTick()
    expect(wrapper.text()).toContain('3. Nơi chạy')

    // Step 3: Nơi chạy
    const nsSelect = wrapper.findAll('select').find((s) => s.text().includes('avina-workspace'))
    if (nsSelect) await nsSelect.setValue('avina-workspace')
    await wrapper.find('input[placeholder*="VD: dbmigrator"]').setValue('dbmigrator')
    const pullSelect = wrapper.findAll('select').find((s) => s.text().includes('Chọn pullSecret từ allowlist'))
    if (pullSelect) {
      await pullSelect.setValue('registry-secret')
      await pullSelect.trigger('change')
    }

    await wrapper.findAll('button').find((b) => b.text().includes('Tiếp tục'))!.trigger('click')
    await wrapper.vm.$nextTick()
    expect(wrapper.text()).toContain('4. Tác vụ')

    // Step 4: Tác vụ (one task edited at a time)
    await wrapper.find('input[placeholder="VD: migrate-db"]').setValue('files')
    await wrapper.findAll('button').find((b) => b.text().includes('Thêm tác vụ'))!.trigger('click')
    await wrapper.vm.$nextTick()
    await wrapper.find('input[placeholder="VD: migrate-db"]').setValue('users')

    await wrapper.findAll('button').find((b) => b.text().includes('Tiếp tục'))!.trigger('click')
    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()
    expect(wrapper.text()).toContain('Kiểm tra & Xem trước YAML')

    // Step 5: Save
    const submitBtn = wrapper.findAll('button').find((b) => b.text().includes('Tạo Job'))
    expect(submitBtn).toBeDefined()
    await submitBtn!.trigger('click')
    await new Promise((resolve) => setTimeout(resolve, 60))

    expect(createSpy).toHaveBeenCalled()
    const payload = createSpy.mock.calls[0][0] as JobInput
    expect(payload.name).toBe('avina-dbmigrator')
    expect(payload.gitUrl).toBe('http://git.example/AvinaWorkspace')
    expect(payload.dockerfilePath).toBe('docker/Dockerfile.dbmigrator')
    expect(payload.buildContext).toBe('.')
    expect(payload.namespace).toBe('avina-workspace')
    expect(payload.tasks.length).toBe(2)
    expect(payload.tasks[0].label).toBe('files')
    expect(payload.tasks[1].label).toBe('users')
  })

  it('updates position when reordering tasks and duplicates a task on Step 4', async () => {
    await router.push('/jobs/new?step=4')
    await router.isReady()

    const wrapper = mount(JobFormView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()

    await wrapper.find('input[placeholder="VD: migrate-db"]').setValue('first-task')
    await wrapper.find('button[aria-label="Nhân bản tác vụ"]').trigger('click')
    await wrapper.vm.$nextTick()

    expect((wrapper.find('input[placeholder="VD: migrate-db"]').element as HTMLInputElement).value).toBe('first-task-copy')
    await wrapper.find('button[aria-label="Di chuyển tác vụ lên"]').trigger('click')
    await wrapper.vm.$nextTick()

    const vm = wrapper.vm as any
    expect(vm.form.tasks[0].label).toBe('first-task-copy')
    expect(vm.form.tasks[1].label).toBe('first-task')
  })

  it('maps 400 validation error from API directly onto fields and marks error steps', async () => {
    vi.spyOn(mockApi, 'createJob').mockRejectedValueOnce(
      new ApiError(400, 'Validation failed', {
        title: 'Validation failed',
        errors: {
          name: ['Tên Job không hợp lệ từ máy chủ'],
          gitUrl: ['Git URL không thể kết nối']
        }
      })
    )

    await router.push('/jobs/new?step=5')
    await router.isReady()

    const wrapper = mount(JobFormView, {
      global: {
        plugins: [pinia, router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 60))
    await flushPromises()

    const vm = wrapper.vm as any
    vm.form.name = 'test-job'
    vm.form.gitUrl = 'http://git.example/test'
    vm.form.gitCredentialId = 2
    vm.form.slaveId = 1
    vm.form.imageName = 'test-image'
    vm.form.registryHost = '192.168.1.241'
    vm.form.namespace = 'avina-workspace'
    vm.form.containerName = 'test-container'

    const submitBtn = wrapper.findAll('button').find((b) => b.text().includes('Tạo Job'))
    await submitBtn!.trigger('click')
    await new Promise((resolve) => setTimeout(resolve, 60))

    expect(wrapper.text()).toContain('Tên Job không hợp lệ từ máy chủ')
    expect(wrapper.text()).toContain('Git URL không thể kết nối')
    expect(wrapper.find('button[data-step-id="source"]').attributes('data-step-status')).toBe('error')
  })
})
