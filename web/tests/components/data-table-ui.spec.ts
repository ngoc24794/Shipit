import { describe, it, expect, vi, afterEach } from 'vitest'
import { mount } from '@vue/test-utils'
import DataToolbar from '../../src/components/DataToolbar.vue'
import DataPagination from '../../src/components/DataPagination.vue'
import AppSelect from '../../src/components/AppSelect.vue'

describe('DataToolbar', () => {
  afterEach(() => {
    vi.useRealTimers()
  })

  it('debounces search input by 300ms and clears immediately on clear button', async () => {
    vi.useFakeTimers()
    const wrapper = mount(DataToolbar, {
      props: {
        q: '',
        startItem: 1,
        endItem: 20,
        total: 134,
        hasActiveFilters: false
      }
    })

    expect(wrapper.get('[data-testid="result-count"]').text()).toBe('Hiển thị 1 đến 20 trong 134')

    const input = wrapper.get('input[type="text"]')
    await input.setValue('shipit')
    expect(wrapper.emitted('update:q')).toBeUndefined()

    await vi.advanceTimersByTimeAsync(299)
    expect(wrapper.emitted('update:q')).toBeUndefined()

    await vi.advanceTimersByTimeAsync(1)
    expect(wrapper.emitted('update:q')).toEqual([['shipit']])

    const clearSearchBtn = wrapper.get('button[aria-label="Xoá từ khoá tìm kiếm"]')
    await clearSearchBtn.trigger('click')
    expect(wrapper.emitted('update:q')?.[1]).toEqual([''])
  })

  it('renders segmented and select filters and emits clear all', async () => {
    const wrapper = mount(DataToolbar, {
      props: {
        q: 'abc',
        filters: [
          {
            key: 'kind',
            label: 'Loại',
            type: 'segmented',
            options: [
              { label: 'Tất cả', value: '' },
              { label: 'Pipeline', value: 'pipeline' },
              { label: 'Job', value: 'job' }
            ]
          },
          {
            key: 'status',
            label: 'Trạng thái',
            type: 'select',
            options: [
              { label: 'Tất cả trạng thái', value: '' },
              { label: 'Thành công', value: 'success' }
            ]
          }
        ],
        filterValues: { kind: '', status: '' },
        startItem: 1,
        endItem: 10,
        total: 10,
        hasActiveFilters: true
      }
    })

    const jobBtn = wrapper.findAll('button').find((b) => b.text() === 'Job')!
    await jobBtn.trigger('click')
    expect(wrapper.emitted('update:filter')?.[0]).toEqual(['kind', 'job'])

    const selectComp = wrapper.findComponent(AppSelect)
    selectComp.vm.$emit('update:modelValue', 'success')
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('update:filter')?.[1]).toEqual(['status', 'success'])

    const clearAllBtn = wrapper.findAll('button').find((b) => b.text().includes('Xoá bộ lọc'))!
    await clearAllBtn.trigger('click')
    expect(wrapper.emitted('clear')).toHaveLength(1)
  })

  it('sizes select filter wrapper to fit longest option label without fixed 175px clipping', () => {
    const wrapper = mount(DataToolbar, {
      props: {
        q: '',
        searchPlaceholder: 'Tìm tên, tag...',
        filters: [
          {
            key: 'target',
            label: 'Pipeline / Job',
            type: 'select',
            options: [
              { label: 'Tất cả Pipeline / Job', value: '' },
              { label: 'avina-api', value: 'avina-api' }
            ]
          }
        ],
        filterValues: { target: '' }
      }
    })

    const selectWrap = wrapper.findComponent(AppSelect).element.parentElement as HTMLElement
    const widthPx = Number.parseInt(selectWrap.style.width, 10)
    expect(widthPx).toBeGreaterThanOrEqual(195)
    expect(wrapper.get('input[type="text"]').attributes('title')).toBe('Tìm tên, tag...')
  })
})

describe('DataPagination', () => {
  it('navigates pages, changes page size, jumps to page, and displays loading state', async () => {
    const wrapper = mount(DataPagination, {
      props: {
        page: 5,
        pageSize: 20,
        total: 200,
        totalPages: 10,
        loading: false
      }
    })

    await wrapper.get('button[aria-label="Trang đầu"]').trigger('click')
    await wrapper.get('button[aria-label="Trang trước"]').trigger('click')
    await wrapper.get('button[aria-label="Trang sau"]').trigger('click')
    await wrapper.get('button[aria-label="Trang cuối"]').trigger('click')

    expect(wrapper.emitted('update:page')).toEqual([[1], [4], [6], [10]])

    const pageSizeSelect = wrapper.findComponent(AppSelect)
    pageSizeSelect.vm.$emit('update:modelValue', 50)
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('update:pageSize')).toEqual([[50]])

    const jumpInput = wrapper.get('input[aria-label="Nhảy tới trang"]')
    await jumpInput.setValue('8')
    await wrapper.get('form').trigger('submit')
    expect(wrapper.emitted('update:page')?.[4]).toEqual([8])

    await wrapper.setProps({ loading: true })
    expect(wrapper.text()).toContain('Đang tải...')
    expect(wrapper.get('button[aria-label="Trang đầu"]').attributes('disabled')).toBeDefined()
  })
})
