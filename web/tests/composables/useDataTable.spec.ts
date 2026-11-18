import { describe, it, expect, vi, beforeEach } from 'vitest'
import { defineComponent, ref } from 'vue'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import { useDataTable } from '../../src/composables/useDataTable'

interface Item {
  id: number
  name: string
  status: string
}

function makeItems(count: number): Item[] {
  return Array.from({ length: count }, (_, i) => ({
    id: i + 1,
    name: `item-${String(i + 1).padStart(2, '0')}`,
    status: i % 2 === 0 ? 'success' : 'failed'
  }))
}

describe('useDataTable', () => {
  let router: ReturnType<typeof createRouter>

  beforeEach(async () => {
    router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', component: { template: '<div />' } }
      ]
    })
    await router.push('/')
    await router.isReady()
  })

  it('filters, sorts, and paginates in client mode and resets page on filter change', async () => {
    const source = ref(makeItems(35))
    let table!: ReturnType<typeof useDataTable<Item>>

    const Comp = defineComponent({
      setup() {
        table = useDataTable<Item>({
          mode: 'client',
          items: source,
          defaultFilters: { status: '' },
          searchFields: ['name'],
          filterFns: {
            status: (item, val) => item.status === val
          },
          defaultPageSize: 10
        })
        return { table }
      },
      template: '<div />'
    })

    mount(Comp, { global: { plugins: [router] } })

    expect(table.total.value).toBe(35)
    expect(table.totalPages.value).toBe(4)
    expect(table.items.value).toHaveLength(10)
    expect(table.startItem.value).toBe(1)
    expect(table.endItem.value).toBe(10)

    table.setPage(3)
    await flushPromises()
    expect(table.page.value).toBe(3)
    expect(table.items.value[0].id).toBe(21)
    expect(router.currentRoute.value.query.page).toBe('3')

    // Filtering resets page back to 1
    table.filters.status = 'success'
    await flushPromises()
    expect(table.page.value).toBe(1)
    expect(table.total.value).toBe(18)
    expect(router.currentRoute.value.query.status).toBe('success')

    // Search
    table.q.value = 'item-03'
    await flushPromises()
    expect(table.total.value).toBe(1)
    expect(table.items.value[0].name).toBe('item-03')

    // Sort descending
    table.clearFilters()
    await flushPromises()
    table.toggleSort('id') // asc
    expect(table.sortDir.value).toBe('asc')
    table.toggleSort('id') // desc
    expect(table.sortDir.value).toBe('desc')
    expect(table.items.value[0].id).toBe(35)
  })

  it('hydrates from URL query and reacts to browser navigation while preserving unrelated params', async () => {
    await router.push('/?tab=job&q=alpha&status=failed&page=2&pageSize=10')
    await router.isReady()

    const source = ref(makeItems(30))
    let table!: ReturnType<typeof useDataTable<Item>>

    const Comp = defineComponent({
      setup() {
        table = useDataTable<Item>({
          mode: 'client',
          items: source,
          defaultFilters: { status: '' },
          searchFields: ['name'],
          filterFns: { status: (i, v) => i.status === v }
        })
        return { table }
      },
      template: '<div />'
    })

    mount(Comp, { global: { plugins: [router] } })

    expect(table.q.value).toBe('alpha')
    expect(table.filters.status).toBe('failed')
    expect(table.page.value).toBe(2)
    expect(table.pageSize.value).toBe(10)

    // Update search and ensure `tab=job` is preserved
    table.q.value = 'beta'
    await flushPromises()
    expect(router.currentRoute.value.query.tab).toBe('job')
    expect(router.currentRoute.value.query.q).toBe('beta')

    // Simulate external URL change (e.g. Back button)
    await router.push('/?tab=job&q=gamma&page=3&pageSize=20')
    await flushPromises()
    expect(table.q.value).toBe('gamma')
    expect(table.page.value).toBe(3)
    expect(table.pageSize.value).toBe(20)
  })

  it('supports server mode with X-Total-Count and legacy client-slice fallback', async () => {
    const allItems = makeItems(25)
    const fetchFn = vi.fn(async ({ page, pageSize }) => ({
      items: allItems.slice((page - 1) * pageSize, page * pageSize),
      total: allItems.length,
      serverPaginated: true
    }))

    let table!: ReturnType<typeof useDataTable<Item>>

    const Comp = defineComponent({
      setup() {
        table = useDataTable<Item>({
          mode: 'server',
          defaultFilters: { status: '' },
          defaultPageSize: 10,
          fetchFn,
          immediate: true
        })
        return { table }
      },
      template: '<div />'
    })

    mount(Comp, { global: { plugins: [router] } })
    await flushPromises()

    expect(fetchFn).toHaveBeenCalledTimes(1)
    expect(table.total.value).toBe(25)
    expect(table.items.value).toHaveLength(10)

    table.setPage(2)
    await flushPromises()
    expect(fetchFn).toHaveBeenLastCalledWith(
      expect.objectContaining({ page: 2, pageSize: 10 })
    )
    expect(table.items.value[0].id).toBe(11)

    // Legacy server fallback (serverPaginated: false -> returns all 25 items)
    fetchFn.mockResolvedValueOnce({
      items: allItems,
      total: allItems.length,
      serverPaginated: false
    })
    table.setPage(3)
    await flushPromises()
    expect(table.items.value).toHaveLength(5)
    expect(table.items.value[0].id).toBe(21)
  })
})
