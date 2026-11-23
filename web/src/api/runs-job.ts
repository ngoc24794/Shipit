import { getApi } from './index'
import type { Run, TriggerRunOptions } from './types'

export async function runJob(id: number, options?: TriggerRunOptions): Promise<Run> {
  const api = getApi()
  if ('runJob' in api && typeof (api as any).runJob === 'function') {
    return options ? (api as any).runJob(id, options) : (api as any).runJob(id)
  }
  if ('triggerJobRun' in api && typeof (api as any).triggerJobRun === 'function') {
    return options ? (api as any).triggerJobRun(id, options) : (api as any).triggerJobRun(id)
  }

  const payload: Record<string, unknown> = {}
  if (options?.skipSteps && options.skipSteps.length > 0) {
    payload.skipSteps = options.skipSteps
  }
  const gitBranch = options?.gitBranch?.trim()
  if (gitBranch) {
    payload.gitBranch = gitBranch
  }
  const init: RequestInit = {
    method: 'POST',
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json'
    },
    credentials: 'same-origin'
  }
  if (Object.keys(payload).length > 0) {
    init.body = JSON.stringify(payload)
  }

  const res = await fetch(`/api/jobs/${id}/runs`, init)

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
