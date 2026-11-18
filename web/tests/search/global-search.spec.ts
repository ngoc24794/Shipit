import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { mount, flushPromises } from '@vue/test-utils'
import GlobalSearch from '../../src/components/search/GlobalSearch.vue'
import AppLayout from '../../src/layouts/AppLayout.vue'
import { useAuthStore } from '../../src/stores/auth'
import { useSearchStore, RECENT_STORAGE_KEY, MAX_RECENT_ITEMS } from '../../src/stores/search'
import { MockShipitApi } from '../../src/api/mock'
import { ApiError } from '../../src/api/client'
import { setApi } from '../../src/api'
import { router } from '../../src/router'

describe('GlobalSearch & Command Palette', () => {
  let pinia: ReturnType<typeof createPinia>
  let mockApi: MockShipitApi

  beforeEach(async () => {
    vi.useFakeTimers()
    localStorage.clear()
    pinia = createPinia()
    setActivePinia(pinia)
    mockApi = new MockShipitApi()
    setApi(mockApi)

    const authStore = useAuthStore()
    authStore.user = { username: 'admin' }
    authStore.initialized = true

    await router.push('/')
    await router.isReady()
  })

  afterEach(() => {
    vi.useRealTimers()
    document.body.innerHTML = ''
  })

  function mountGlobalSearch() {
    return mount(GlobalSearch, {
      attachTo: document.body,
      global: {
        plugins: [pinia, router]
      }
    })
  }

  it('opens and closes with Ctrl+K, Cmd+K, /, and Esc, and ignores / inside inputs', async () => {
    const wrapper = mountGlobalSearch()

    expect(document.body.querySelector('[data-testid="search-dialog"]')).toBeNull()

    // Ctrl+K opens
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', ctrlKey: true, bubbles: true }))
    await flushPromises()
    expect(document.body.querySelector('[data-testid="search-dialog"]')).not.toBeNull()

    // Esc closes
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    await flushPromises()
    expect(document.body.querySelector('[data-testid="search-dialog"]')).toBeNull()

    // Cmd+K opens
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', metaKey: true, bubbles: true }))
    await flushPromises()
    expect(document.body.querySelector('[data-testid="search-dialog"]')).not.toBeNull()

    // Esc closes
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    await flushPromises()
    expect(document.body.querySelector('[data-testid="search-dialog"]')).toBeNull()

    // '/' inside an <input> should NOT open
    const externalInput = document.createElement('input')
    document.body.appendChild(externalInput)
    externalInput.dispatchEvent(new KeyboardEvent('keydown', { key: '/', bubbles: true }))
    await flushPromises()
    expect(document.body.querySelector('[data-testid="search-dialog"]')).toBeNull()

    // '/' on window / non-input opens
    window.dispatchEvent(new KeyboardEvent('keydown', { key: '/', bubbles: true }))
    await flushPromises()
    expect(document.body.querySelector('[data-testid="search-dialog"]')).not.toBeNull()

    wrapper.unmount()
  })

  it('debounces 250ms and aborts previous in-flight search request', async () => {
    const signals: AbortSignal[] = []
    const searchSpy = vi.fn((q: string, _limit?: number, signal?: AbortSignal) => {
      if (signal) signals.push(signal)
      return Promise.resolve({
        query: q,
        groups: [
          {
            kind: 'pipeline' as const,
            label: 'Pipeline',
            items: [{ id: '1', title: `Pipeline ${q}`, subtitle: 'main', path: '/pipelines/1/edit' }]
          }
        ]
      })
    })
    setApi({ ...mockApi, search: searchSpy } as any)

    const wrapper = mountGlobalSearch()
    const trigger = wrapper.find('[data-testid="global-search-trigger"]')
    await trigger.trigger('click')
    await flushPromises()

    const input = document.body.querySelector('[data-testid="search-input"]') as HTMLInputElement
    expect(input).not.toBeNull()

    // Rapid typing before 250ms
    input.value = 's'
    input.dispatchEvent(new Event('input'))
    await vi.advanceTimersByTimeAsync(100)

    input.value = 'sh'
    input.dispatchEvent(new Event('input'))
    await vi.advanceTimersByTimeAsync(100)

    input.value = 'shipit'
    input.dispatchEvent(new Event('input'))
    await vi.advanceTimersByTimeAsync(200)

    // Still under 250ms since last input -> no call yet
    expect(searchSpy).not.toHaveBeenCalled()

    await vi.advanceTimersByTimeAsync(50)
    await flushPromises()

    expect(searchSpy).toHaveBeenCalledTimes(1)
    expect(searchSpy).toHaveBeenCalledWith('shipit', 5, expect.any(AbortSignal))

    // Now test aborting an in-flight request
    let resolveFirst: (val: any) => void
    const pendingPromise = new Promise((res) => {
      resolveFirst = res
    })
    const abortSpy = vi.fn((_q: string, _limit?: number, signal?: AbortSignal) => {
      if (signal) signals.push(signal)
      return pendingPromise
    })
    setApi({ ...mockApi, search: abortSpy } as any)

    input.value = 'first'
    input.dispatchEvent(new Event('input'))
    await vi.advanceTimersByTimeAsync(250)
    expect(abortSpy).toHaveBeenCalledTimes(1)
    expect(signals[1].aborted).toBe(false)

    // Type next query while first is still pending
    input.value = 'second'
    input.dispatchEvent(new Event('input'))
    expect(signals[1].aborted).toBe(true)

    resolveFirst!({ query: 'first', groups: [] })
    await flushPromises()
    wrapper.unmount()
  })

  it('supports keyboard navigation (ArrowDown, ArrowUp, Tab between groups, Enter) and highlights matches', async () => {
    const pushSpy = vi.spyOn(router, 'push')
    const wrapper = mountGlobalSearch()

    await wrapper.find('[data-testid="global-search-trigger"]').trigger('click')
    await flushPromises()

    const input = document.body.querySelector('[data-testid="search-input"]') as HTMLInputElement
    input.value = 'shipit'
    input.dispatchEvent(new Event('input'))

    await vi.advanceTimersByTimeAsync(250)
    await flushPromises()
    // MockShipitApi has a 20ms delay
    await vi.advanceTimersByTimeAsync(30)
    await flushPromises()

    const marks = document.body.querySelectorAll('[data-testid="search-results"] mark')
    expect(marks.length).toBeGreaterThan(0)

    const dialog = document.body.querySelector('[data-testid="search-dialog"]') as HTMLElement
    expect(document.body.querySelector('[data-testid="search-item-0"]')?.getAttribute('data-active')).toBe('true')

    // ArrowDown moves to item 1
    dialog.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }))
    await flushPromises()
    expect(document.body.querySelector('[data-testid="search-item-1"]')?.getAttribute('data-active')).toBe('true')

    // ArrowUp moves back to item 0
    dialog.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp', bubbles: true }))
    await flushPromises()
    expect(document.body.querySelector('[data-testid="search-item-0"]')?.getAttribute('data-active')).toBe('true')

    // Tab jumps to first item of next group
    dialog.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true }))
    await flushPromises()
    const activeEl = document.body.querySelector('[data-active="true"]')
    expect(activeEl?.getAttribute('data-testid')).not.toBe('search-item-0')

    // Enter navigates to active item's path and closes dialog
    dialog.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }))
    await flushPromises()

    expect(pushSpy).toHaveBeenCalled()
    expect(document.body.querySelector('[data-testid="search-dialog"]')).toBeNull()

    wrapper.unmount()
  })

  it('shows empty state and error state, and redirects to /login on 401', async () => {
    const pushSpy = vi.spyOn(router, 'push')
    const wrapper = mountGlobalSearch()
    await wrapper.find('[data-testid="global-search-trigger"]').trigger('click')
    await flushPromises()

    const input = document.body.querySelector('[data-testid="search-input"]') as HTMLInputElement

    // 1. Empty state
    input.value = 'khong-ton-tai-xyz-999'
    input.dispatchEvent(new Event('input'))
    await vi.advanceTimersByTimeAsync(300)
    await flushPromises()

    expect(document.body.querySelector('[data-testid="search-empty"]')?.textContent).toContain(
      'Không tìm thấy kết quả phù hợp'
    )

    // 2. Generic error state
    setApi({
      ...mockApi,
      search: vi.fn().mockRejectedValue(new ApiError(500, 'Lỗi máy chủ tìm kiếm'))
    } as any)

    input.value = 'error-query'
    input.dispatchEvent(new Event('input'))
    await vi.advanceTimersByTimeAsync(260)
    await flushPromises()

    expect(document.body.querySelector('[data-testid="search-error"]')?.textContent).toContain(
      'Lỗi máy chủ tìm kiếm'
    )

    // 3. 401 redirects to login
    setApi({
      ...mockApi,
      search: vi.fn().mockRejectedValue(new ApiError(401, 'Unauthorized'))
    } as any)

    input.value = 'unauth-query'
    input.dispatchEvent(new Event('input'))
    await vi.advanceTimersByTimeAsync(260)
    await flushPromises()

    expect(pushSpy).toHaveBeenCalledWith(expect.objectContaining({ name: 'login' }))
    expect(document.body.querySelector('[data-testid="search-dialog"]')).toBeNull()

    wrapper.unmount()
  })

  it('displays shortcuts and recent items (max 6) when empty, and filters shortcuts client-side', async () => {
    const wrapper = mountGlobalSearch()
    const store = useSearchStore()

    // Add 7 unique items -> keeps max 6
    for (let i = 1; i <= 7; i += 1) {
      store.addRecent({
        id: `item-${i}`,
        title: `Mục ${i}`,
        subtitle: null,
        path: `/runs/${i}`,
        kind: 'run'
      })
    }

    expect(store.recent).toHaveLength(MAX_RECENT_ITEMS)
    expect(store.recent[0].path).toBe('/runs/7')
    expect(JSON.parse(localStorage.getItem(RECENT_STORAGE_KEY) || '[]')).toHaveLength(MAX_RECENT_ITEMS)

    await wrapper.find('[data-testid="global-search-trigger"]').trigger('click')
    await flushPromises()

    expect(document.body.querySelector('[data-testid="search-group-recent"]')).not.toBeNull()
    expect(document.body.querySelector('[data-testid="search-group-shortcuts-nav"]')).not.toBeNull()
    expect(document.body.querySelector('[data-testid="search-group-shortcuts-actions"]')).not.toBeNull()

    // Filter shortcuts client-side immediately without waiting for API
    const searchSpy = vi.fn().mockResolvedValue({ query: 'Thêm máy build', groups: [] })
    setApi({ ...mockApi, search: searchSpy } as any)

    const input = document.body.querySelector('[data-testid="search-input"]') as HTMLInputElement
    input.value = 'Thêm máy build'
    input.dispatchEvent(new Event('input'))
    await flushPromises()

    const filteredShortcuts = document.body.querySelector('[data-testid="search-group-shortcuts-filtered"]')
    expect(filteredShortcuts).not.toBeNull()
    expect(filteredShortcuts?.textContent).toContain('Thêm máy build')
    expect(searchSpy).not.toHaveBeenCalled()

    wrapper.unmount()
  })

  it('MockShipitApi.search returns grouped results without credential secrets', async () => {
    vi.useRealTimers()
    const empty = await mockApi.search('   ')
    expect(empty.groups).toEqual([])

    const res = await mockApi.search('devops')
    expect(res.groups.length).toBeGreaterThan(0)
    const serialized = JSON.stringify(res)
    expect(serialized).not.toContain('BEGIN')
    expect(serialized).not.toContain('password')
  })

  it('has no global scrollbar when AppLayout and command palette are open', async () => {
    const wrapper = mount(AppLayout, {
      attachTo: document.body,
      global: {
        plugins: [pinia, router]
      },
      slots: {
        default: '<h1>Lượt chạy</h1>'
      }
    })

    await wrapper.find('[data-testid="global-search-trigger"]').trigger('click')
    await flushPromises()

    const root = wrapper.find('div.h-full')
    const main = wrapper.find('main')
    const dialog = document.body.querySelector('[data-testid="search-dialog"]') as HTMLElement
    const results = document.body.querySelector('[data-testid="search-results"]') as HTMLElement

    expect(root.classes()).toContain('overflow-hidden')
    expect(main.classes()).toContain('overflow-y-hidden')
    expect(dialog).not.toBeNull()
    expect(dialog.className).toContain('overflow-hidden')
    expect(dialog.className).toContain('w-[640px]')
    expect(dialog.className).toContain('max-h-[60%]')
    expect(results.className).toContain('overflow-y-auto')

    // Verify JS scrollHeight check on documentElement
    expect(document.documentElement.scrollHeight).toBeLessThanOrEqual(
      document.documentElement.clientHeight
    )

    wrapper.unmount()
  })
})
