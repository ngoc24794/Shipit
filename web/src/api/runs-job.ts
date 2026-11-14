import { getApi } from './index'
import type { Run } from './types'

export async function runJob(id: number): Promise<Run> {
  const api = getApi()
  if ('runJob' in api && typeof (api as any).runJob === 'function') {
    return (api as any).runJob(id)
  }
  if ('triggerJobRun' in api && typeof (api as any).triggerJobRun === 'function') {
    return (api as any).triggerJobRun(id)
  }

  const res = await fetch(`/api/jobs/${id}/runs`, {
    method: 'POST',
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json'
    },
    credentials: 'same-origin'
  })

  if (!res.ok) {
    let errMessage = `Yêu cầu thất bại với mã lỗi ${res.status}`
    try {
      const prob = await res.json()
      if (prob.detail || prob.title) {
        errMessage = prob.detail || prob.title
      }
    } catch {
      // ignore
    }
    throw new Error(errMessage)
  }

  return (await res.json()) as Run
}
