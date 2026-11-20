import { describe, it, expect, beforeEach, vi } from 'vitest'
import { defineComponent, ref, nextTick, KeepAlive } from 'vue'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import App from '../../src/App.vue'
import AppLayout from '../../src/layouts/AppLayout.vue'
import AppTable from '../../src/components/AppTable.vue'
import { useAuthStore } from '../../src/stores/auth'
import { useShortcuts, resetShortcutsForTest } from '../../src/composables/useShortcuts'
import appRouter from '../../src/router'

describe('Persistent AppLayout Shell & KeepAlive', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    resetShortcutsForTest()
    const auth = useAuthStore()
    auth.user = { id: 1, username: 'admin', role: 'admin' } as any
  })

  it('keeps header, aside, and footer DOM nodes identical across authenticated route navigations', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: appRouter.options.routes
    })
    await router.push('/')
    await router.isReady()

    const wrapper = mount(App, {
      global: {
        plugins: [router]
      }
    })
    await flushPromises()

    const headerEl = wrapper.find('header').element
    const asideEl = wrapper.find('aside').element
    const footerEl = wrapper.find('footer').element

    expect(headerEl).toBeTruthy()
    expect(asideEl).toBeTruthy()
    expect(footerEl).toBeTruthy()

    for (const path of ['/pipelines', '/jobs', '/slaves', '/credentials', '/settings/allowlist', '/']) {
      await router.push(path)
      await flushPromises()
      await nextTick()

      expect(wrapper.find('header').element).toBe(headerEl)
      expect(wrapper.find('aside').element).toBe(asideEl)
      expect(wrapper.find('footer').element).toBe(footerEl)
    }

    // Navigating to /login should unmount AppLayout shell
    await router.push('/login')
    await flushPromises()
    await nextTick()
    expect(wrapper.find('aside').exists()).toBe(false)

    wrapper.unmount()
  })

  it('pauses useShortcuts on KeepAlive deactivation and restores on reactivation', async () => {
    const handler = vi.fn()

    const ViewA = defineComponent({
      name: 'ViewA',
      setup() {
        const count = ref(0)
        const { register } = useShortcuts()
        register({
          id: 'view-a-action',
          keys: 'n',
          label: 'View A Action',
          scope: 'View A',
          handler
        })
        return { count }
      },
      template: '<div data-testid="view-a" @click="count++">{{ count }}</div>'
    })

    const ViewB = defineComponent({
      name: 'ViewB',
      template: '<div data-testid="view-b">View B</div>'
    })

    const Host = defineComponent({
      components: { KeepAlive, ViewA, ViewB },
      setup() {
        const active = ref<'a' | 'b'>('a')
        const { bindings } = useShortcuts()
        return { active, bindings }
      },
      template: `
        <KeepAlive :include="['ViewA']">
          <ViewA v-if="active === 'a'" />
          <ViewB v-else />
        </KeepAlive>
      `
    })

    const wrapper = mount(Host)
    await nextTick()

    expect(wrapper.vm.bindings.some((b: any) => b.id === 'view-a-action')).toBe(true)

    // Mutate state in ViewA then switch to ViewB
    await wrapper.find('[data-testid="view-a"]').trigger('click')
    expect(wrapper.find('[data-testid="view-a"]').text()).toBe('1')

    wrapper.vm.active = 'b'
    await nextTick()

    // Shortcut must be unregistered while ViewA is deactivated
    expect(wrapper.vm.bindings.some((b: any) => b.id === 'view-a-action')).toBe(false)

    // Switch back to ViewA: state and shortcut must be restored
    wrapper.vm.active = 'a'
    await nextTick()

    expect(wrapper.find('[data-testid="view-a"]').text()).toBe('1')
    expect(wrapper.vm.bindings.some((b: any) => b.id === 'view-a-action')).toBe(true)

    wrapper.unmount()
  })

  it('preserves AppTable scrollTop across KeepAlive deactivation and reactivation', async () => {
    const TableView = defineComponent({
      name: 'TableView',
      components: { AppTable },
      setup() {
        const rows = Array.from({ length: 30 }, (_, i) => ({ id: i + 1, name: `Row ${i + 1}` }))
        return { rows }
      },
      template: `
        <AppTable :rows="rows">
          <template #header-columns><th>Name</th></template>
          <template #row="{ row }"><td>{{ row.name }}</td></template>
        </AppTable>
      `
    })

    const OtherView = defineComponent({
      name: 'OtherView',
      template: '<div>Other</div>'
    })

    const Host = defineComponent({
      components: { KeepAlive, TableView, OtherView },
      setup() {
        const showTable = ref(true)
        return { showTable }
      },
      template: `
        <KeepAlive :include="['TableView']">
          <TableView v-if="showTable" />
          <OtherView v-else />
        </KeepAlive>
      `
    })

    const wrapper = mount(Host)
    await nextTick()

    const scrollContainer = wrapper.find('.overflow-auto').element as HTMLElement
    scrollContainer.scrollTop = 240

    wrapper.vm.showTable = false
    await nextTick()

    wrapper.vm.showTable = true
    await nextTick()
    await nextTick()

    const restoredContainer = wrapper.find('.overflow-auto').element as HTMLElement
    expect(restoredContainer.scrollTop).toBe(240)

    wrapper.unmount()
  })

  it('preserves RunsView filter, search, and pageSize across navigation away and bare rail return', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: appRouter.options.routes
    })
    await router.push('/')
    await router.isReady()

    const wrapper = mount(App, {
      global: { plugins: [router] }
    })
    await new Promise((r) => setTimeout(r, 150))
    await flushPromises()
    await nextTick()

    const initialRows = wrapper.findAll('tbody tr').length
    expect(initialRows).toBeGreaterThan(0)

    // Apply pipeline filter & search via route query (same as selecting in toolbar)
    await router.replace({ path: '/', query: { target: 'pipeline:1', pageSize: '10' } })
    await new Promise((r) => setTimeout(r, 150))
    await flushPromises()
    await nextTick()

    const filteredRows = wrapper.findAll('tbody tr').length
    expect(filteredRows).toBeGreaterThan(0)
    expect(filteredRows).toBeLessThanOrEqual(initialRows)

    // Navigate away to /pipelines via rail-style bare route
    await router.push('/pipelines')
    await flushPromises()
    await nextTick()
    expect(router.currentRoute.value.path).toBe('/pipelines')

    // Return to / via bare rail link (no query params)
    await router.push('/')
    await new Promise((r) => setTimeout(r, 150))
    await flushPromises()
    await nextTick()

    // Filter and query must be restored
    expect(router.currentRoute.value.query.target).toBe('pipeline:1')
    expect(router.currentRoute.value.query.pageSize).toBe('10')
    expect(wrapper.findAll('tbody tr').length).toBe(filteredRows)

    wrapper.unmount()
  })

  it('preserves PipelinesView search, sort, and page across navigation away and bare return', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: appRouter.options.routes
    })
    await router.push('/pipelines')
    await router.isReady()

    const wrapper = mount(App, {
      global: { plugins: [router] }
    })
    await flushPromises()
    await nextTick()

    // Apply search + sort on PipelinesView
    await router.replace({
      path: '/pipelines',
      query: { q: 'api', sortBy: 'name', sortDir: 'desc' }
    })
    await flushPromises()
    await nextTick()

    // Navigate away to /jobs and back to bare /pipelines
    await router.push('/jobs')
    await flushPromises()
    await nextTick()

    await router.push('/pipelines')
    await flushPromises()
    await nextTick()

    expect(router.currentRoute.value.query.q).toBe('api')
    expect(router.currentRoute.value.query.sortBy).toBe('name')
    expect(router.currentRoute.value.query.sortDir).toBe('desc')

    wrapper.unmount()
  })

  it('preserves AllowlistView tab across navigation away and bare return', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: appRouter.options.routes
    })
    await router.push('/settings/allowlist')
    await router.isReady()

    const wrapper = mount(App, {
      global: { plugins: [router] }
    })
    await flushPromises()
    await nextTick()

    await wrapper.find('#tab-job').trigger('click')
    await flushPromises()
    await nextTick()
    expect(router.currentRoute.value.query.tab).toBe('job')

    await router.push('/pipelines')
    await flushPromises()
    await nextTick()

    await router.push('/settings/allowlist')
    await flushPromises()
    await nextTick()

    expect(router.currentRoute.value.query.tab).toBe('job')

    wrapper.unmount()
  })
})
