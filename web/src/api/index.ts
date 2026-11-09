import type { App, InjectionKey } from 'vue'
import { inject } from 'vue'
import type { ShipitApi } from './client'
import { HttpShipitApi } from './client'
import { MockShipitApi } from './mock'

export * from './types'
export * from './client'
export * from './mock'

export const API_KEY: InjectionKey<ShipitApi> = Symbol('ShipitApi')

let defaultApiInstance: ShipitApi | null = null

export function getApi(): ShipitApi {
  if (!defaultApiInstance) {
    const useMock = import.meta.env.VITE_USE_MOCK === 'true'
    defaultApiInstance = useMock ? new MockShipitApi() : new HttpShipitApi()
  }
  return defaultApiInstance
}

export function setApi(api: ShipitApi | null): void {
  defaultApiInstance = api
}

export function useApi(): ShipitApi {
  const injected = inject(API_KEY, null)
  return injected || getApi()
}

export function setupApi(app: App, apiInstance?: ShipitApi): ShipitApi {
  const api = apiInstance || getApi()
  app.provide(API_KEY, api)
  return api
}
