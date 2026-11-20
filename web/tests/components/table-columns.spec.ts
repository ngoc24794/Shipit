import { describe, it, expect } from 'vitest'
import fs from 'node:fs'
import path from 'node:path'

const viewsRoot = path.resolve(__dirname, '../../src/views')

describe('List table column width definitions (H1)', () => {
  const actionViews = [
    { file: 'config/PipelinesView.vue', minActionWidth: 255 },
    { file: 'jobs/JobsView.vue', minActionWidth: 255 },
    { file: 'config/SlavesView.vue', minActionWidth: 295 },
    { file: 'config/CredentialsView.vue', minActionWidth: 140 },
    { file: 'config/DeployTargetsView.vue', minActionWidth: 90 },
    { file: 'jobs/JobAllowListView.vue', minActionWidth: 90 }
  ]

  it.each(actionViews)('$file sets explicit width >= $minActionWidth px on Thao tác column', ({ file, minActionWidth }) => {
    const content = fs.readFileSync(path.join(viewsRoot, file), 'utf8')
    const match = content.match(/<th[^>]*w-\[(\d+)px\][^>]*>\s*Thao tác/)
    expect(match, `Expected explicit w-[Npx] on Thao tác header in ${file}`).not.toBeNull()
    const widthPx = Number.parseInt(match![1], 10)
    expect(widthPx).toBeGreaterThanOrEqual(minActionWidth)
  })
})
