import { getApi } from './index'
import { ApiError, notifyUnauthorized } from './client'
import type { Problem, SearchResponse } from './types'

export async function searchAll(
  q: string,
  limit: number = 5,
  signal?: AbortSignal
): Promise<SearchResponse> {
  const api = getApi()
  if ('search' in api && typeof api.search === 'function') {
    return api.search(q, limit, signal)
  }

  const params = new URLSearchParams()
  params.set('q', q)
  params.set('limit', String(limit))

  const res = await fetch(`/api/search?${params.toString()}`, {
    headers: { Accept: 'application/json' },
    credentials: 'same-origin',
    signal
  })

  if (res.status === 401) {
    notifyUnauthorized()
    throw new ApiError(401, 'Chưa đăng nhập hoặc phiên làm việc đã hết hạn')
  }

  if (!res.ok) {
    let problem: Problem | undefined
    try {
      problem = (await res.json()) as Problem
    } catch {
      // ignore non-JSON
    }
    const message = problem?.detail || problem?.title || `Yêu cầu thất bại với mã lỗi ${res.status}`
    throw new ApiError(res.status, message, problem)
  }

  return (await res.json()) as SearchResponse
}
