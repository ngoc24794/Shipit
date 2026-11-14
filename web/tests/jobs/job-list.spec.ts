import { describe, it, expect, beforeEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { mount } from '@vue/test-utils'
import JobsView from '../../src/views/jobs/JobsView.vue'
import { useJobsStore } from '../../src/stores/jobs'
import { useToast } from '../../src/composables/useToast'
import { MockShipitApi } from '../../src/api/mock'
import { setApi } from '../../src/api'
import { router } from '../../src/router'
import { ApiError } from '../../src/api/client'

describe('JobsView (Danh sách Job)', () => {
  let mockApi: MockShipitApi

  beforeEach(async () => {
    document.body.innerHTML = ''
    setActivePinia(createPinia())
    mockApi = new MockShipitApi()
    setApi(mockApi)
    await router.push('/jobs')
    await router.isReady()
  })

  it('renders table with jobs, namespace, task count, and repo branch', async () => {
    const store = useJobsStore()
    await store.fetchJobs()

    const wrapper = mount(JobsView, {
      global: {
        plugins: [createPinia(), router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 50))

    expect(wrapper.text()).toContain('avina-dbmigrator')
    expect(wrapper.text()).toContain('avina-workspace')
    expect(wrapper.text()).toContain('http://git.example/AvinaWorkspace')
    expect(wrapper.text()).toContain('(main)')
    // Tasks count: 2
    expect(wrapper.text()).toContain('2')
  })

  it('triggers job run when clicking "Chạy ngay" and navigates to run detail', async () => {
    const runSpy = vi.spyOn(mockApi, 'triggerJobRun')
    const pushSpy = vi.spyOn(router, 'push')

    const wrapper = mount(JobsView, {
      global: {
        plugins: [createPinia(), router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 50))

    const runBtn = wrapper.findAll('button').find((b) => b.text().includes('Chạy ngay'))
    expect(runBtn).toBeDefined()
    await runBtn!.trigger('click')
    await new Promise((resolve) => setTimeout(resolve, 60))

    expect(runSpy).toHaveBeenCalledWith(1)
    expect(pushSpy).toHaveBeenCalledWith(expect.stringContaining('/runs/'))
  })

  it('opens delete ConfirmDialog and deletes job upon confirmation', async () => {
    const deleteSpy = vi.spyOn(mockApi, 'deleteJob')

    const wrapper = mount(JobsView, {
      global: {
        plugins: [createPinia(), router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 50))

    const deleteBtn = wrapper.findAll('button').find((b) => b.text().includes('Xoá'))
    expect(deleteBtn).toBeDefined()
    await deleteBtn!.trigger('click')
    await wrapper.vm.$nextTick()

    // Confirm dialog should be open
    const confirmBtn = Array.from(document.body.querySelectorAll('button')).find((b) =>
      b.textContent?.includes('Xoá Job')
    )
    expect(confirmBtn).toBeDefined()
    await confirmBtn!.click()
    await new Promise((resolve) => setTimeout(resolve, 50))

    expect(deleteSpy).toHaveBeenCalledWith(1)
  })

  it('handles 409 conflict when deleting job with active run', async () => {
    vi.spyOn(mockApi, 'deleteJob').mockRejectedValueOnce(
      new ApiError(409, 'Không thể xoá Job khi đang có lượt chạy hoạt động')
    )

    const wrapper = mount(JobsView, {
      global: {
        plugins: [createPinia(), router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 50))

    const deleteBtn = wrapper.findAll('button').find((b) => b.text().includes('Xoá'))
    await deleteBtn!.trigger('click')
    await wrapper.vm.$nextTick()

    const confirmBtn = Array.from(document.body.querySelectorAll('button')).find((b) =>
      b.textContent?.includes('Xoá Job')
    )
    await confirmBtn!.click()
    await new Promise((resolve) => setTimeout(resolve, 50))

    const toast = useToast()
    expect(toast.toasts.value.some((t) => t.message.includes('Không thể xoá Job khi đang có lượt chạy'))).toBe(true)
  })

  it('renders EmptyState when there are no jobs', async () => {
    vi.spyOn(mockApi, 'getJobs').mockResolvedValueOnce([])

    const wrapper = mount(JobsView, {
      global: {
        plugins: [createPinia(), router]
      }
    })

    await new Promise((resolve) => setTimeout(resolve, 50))

    expect(wrapper.text()).toContain('Chưa có Job nào')
  })
})
