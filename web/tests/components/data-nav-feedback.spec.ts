import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import AppTabs from '../../src/components/AppTabs.vue'
import AppBreadcrumb from '../../src/components/AppBreadcrumb.vue'
import AppTable from '../../src/components/AppTable.vue'
import EmptyState from '../../src/components/EmptyState.vue'
import ErrorState from '../../src/components/ErrorState.vue'
import AppSkeleton from '../../src/components/AppSkeleton.vue'
import AppProgressBar from '../../src/components/AppProgressBar.vue'
import AppBadge from '../../src/components/AppBadge.vue'
import FormStepper from '../../src/components/FormStepper.vue'

describe('AppTabs', () => {
  it('renders tabs with counts and supports arrow key navigation', async () => {
    const wrapper = mount(AppTabs, {
      props: {
        modelValue: 'jobs',
        tabs: [
          { id: 'jobs', label: 'Job allowlist', count: 12 },
          { id: 'targets', label: 'Deployment targets', count: 4 },
          { id: 'locked', label: 'Locked', disabled: true }
        ]
      }
    })

    const tabs = wrapper.findAll('[role="tab"]')
    expect(tabs).toHaveLength(3)
    expect(tabs[0].attributes('aria-selected')).toBe('true')
    expect(tabs[0].text()).toContain('12')

    await tabs[0].trigger('keydown', { key: 'ArrowRight' })
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual(['targets'])
  })
})

describe('AppBreadcrumb', () => {
  it('renders breadcrumb items and marks last as current page', () => {
    const wrapper = mount(AppBreadcrumb, {
      props: {
        items: [
          { label: 'Pipelines', to: '/pipelines' },
          { label: 'avina-api' }
        ]
      },
      global: {
        stubs: {
          RouterLink: {
            template: '<a :href="to"><slot /></a>',
            props: ['to']
          }
        }
      }
    })

    expect(wrapper.get('nav[aria-label="Breadcrumb"]').text()).toContain('Pipelines')
    expect(wrapper.get('[aria-current="page"]').text()).toBe('avina-api')
  })
})

describe('AppTable (selection, keyboard nav, loading, empty, error)', () => {
  const rows = [
    { id: 1, name: 'avina-api' },
    { id: 2, name: 'avina-dbmigrator' }
  ]

  it('supports row selection and j/k/Enter keyboard navigation', async () => {
    const wrapper = mount(AppTable, {
      props: {
        rows,
        selectable: true,
        selectedKeys: [],
        keyboardNav: true,
        activeIndex: 0
      },
      slots: {
        'header-columns': '<th>Tên</th>',
        row: '<template #row="{ row }"><td>{{ row.name }}</td></template>'
      }
    })

    const checkboxes = wrapper.findAll('[role="checkbox"]')
    expect(checkboxes).toHaveLength(3) // 1 header + 2 rows
    await checkboxes[1].trigger('click')
    expect(wrapper.emitted('update:selectedKeys')?.[0]).toEqual([[1]])

    await wrapper.trigger('keydown', { key: 'j' })
    expect(wrapper.emitted('update:activeIndex')?.[0]).toEqual([1])

    await wrapper.trigger('keydown', { key: 'Enter' })
    expect(wrapper.emitted('row-activate')?.[0]).toEqual([rows[0], 0])
  })

  it('renders loading skeleton rows, empty state, and error state', async () => {
    const wrapper = mount(AppTable, {
      props: {
        loading: true,
        skeletonRows: 2,
        skeletonCols: 3
      }
    })
    expect(wrapper.findAll('tbody tr[aria-busy="true"]')).toHaveLength(2)

    await wrapper.setProps({ loading: false, empty: true, emptyTitle: 'Không có bản ghi' })
    expect(wrapper.text()).toContain('Không có bản ghi')

    await wrapper.setProps({ empty: false, error: 'Lỗi kết nối API' })
    expect(wrapper.text()).toContain('Lỗi kết nối API')
    await wrapper.get('button').trigger('click')
    expect(wrapper.emitted('retry')).toHaveLength(1)
  })
})

describe('EmptyState, ErrorState, AppSkeleton, AppProgressBar, AppBadge', () => {
  it('renders EmptyState and ErrorState with retry', async () => {
    const empty = mount(EmptyState, { props: { title: 'Trống', description: 'Chưa có mục nào' } })
    expect(empty.text()).toContain('Trống')
    expect(empty.text()).toContain('Chưa có mục nào')

    const err = mount(ErrorState, { props: { message: 'Timeout' } })
    expect(err.get('[role="alert"]').text()).toContain('Timeout')
    await err.get('button').trigger('click')
    expect(err.emitted('retry')).toHaveLength(1)
  })

  it('renders AppSkeleton variants with dim block characters', () => {
    const skel = mount(AppSkeleton, { props: { variant: 'table', count: 2, columns: 3 } })
    expect(skel.attributes('aria-busy')).toBe('true')
    expect(skel.text()).toContain('████')
  })

  it('renders AppProgressBar determinate and indeterminate', () => {
    const bar = mount(AppProgressBar, { props: { value: 65, label: 'Đang build' } })
    expect(bar.get('[role="progressbar"]').attributes('aria-valuenow')).toBe('65')
    expect(bar.text()).toContain('65%')

    const ind = mount(AppProgressBar, { props: { indeterminate: true } })
    expect(ind.get('[role="progressbar"]').attributes('aria-valuenow')).toBeUndefined()
  })

  it('renders AppBadge with count and variants', () => {
    const badge = mount(AppBadge, { props: { variant: 'phosphor', count: 42 } })
    expect(badge.text()).toBe('42')
    expect(badge.classes().some((c) => c.includes('text-phosphor'))).toBe(true)
  })
})

describe('FormStepper', () => {
  it('renders D1 wizard tabs with error marks and shortcut hint', async () => {
    const wrapper = mount(FormStepper, {
      props: {
        currentStep: 0,
        steps: [
          { id: 'info', label: 'Thông tin', status: 'current' },
          { id: 'repo', label: 'Repository', status: 'error' },
          { id: 'yaml', label: 'YAML', status: 'pending' }
        ]
      }
    })

    const errorBtn = wrapper.get('[data-step-id="repo"]')
    expect(errorBtn.text()).toContain('!')
    expect(wrapper.text()).toContain('[←/→]')

    await errorBtn.trigger('click')
    expect(wrapper.emitted('select')?.[0]).toEqual([1])
  })
})
