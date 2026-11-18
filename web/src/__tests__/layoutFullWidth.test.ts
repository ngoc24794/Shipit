import { describe, it, expect } from 'vitest'
import fs from 'node:fs'
import path from 'node:path'

const srcRoot = path.resolve(__dirname, '..')

function collectVueFiles(dir: string): string[] {
  const results: string[] = []
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name)
    if (entry.isDirectory()) {
      results.push(...collectVueFiles(full))
    } else if (entry.isFile() && entry.name.endsWith('.vue')) {
      results.push(full)
    }
  }
  return results
}

describe('Full-width layout constraints (UI5)', () => {
  it('AppLayout.vue does not constrain content with max-w-* or mx-auto and keeps min-w-[1024px]', () => {
    const content = fs.readFileSync(path.join(srcRoot, 'layouts/AppLayout.vue'), 'utf8')
    expect(content).not.toMatch(/\bmax-w-/)
    expect(content).not.toMatch(/\bmx-auto\b/)
    expect(content).toContain('min-w-[1024px]')
  })

  it('FormWizardLayout.vue does not constrain wizard width with max-w-* or container mx-auto', () => {
    const content = fs.readFileSync(path.join(srcRoot, 'components/FormWizardLayout.vue'), 'utf8')
    expect(content).not.toMatch(/\bmax-w-/)
    expect(content).not.toMatch(/\bw-full\s+mx-auto\b/)
    expect(content).not.toMatch(/\bflex-1[^"']*mx-auto\b/)
  })

  it('No view in web/src/views uses container-level max-w-* or mx-auto to narrow the page', () => {
    const viewsDir = path.join(srcRoot, 'views')
    const vueFiles = collectVueFiles(viewsDir)
    expect(vueFiles.length).toBeGreaterThan(0)

    const violations: string[] = []
    for (const file of vueFiles) {
      const rel = path.relative(srcRoot, file).replace(/\\/g, '/')
      // LoginView and NotFoundView are standalone centered pages outside AppLayout
      if (rel === 'views/LoginView.vue' || rel === 'views/NotFoundView.vue') continue

      const content = fs.readFileSync(file, 'utf8')
      const lines = content.split(/\r?\n/)
      lines.forEach((line, idx) => {
        // Container-level max-w-* (3xl..7xl, [1440px], etc.)
        if (/\bmax-w-(?:2xl|3xl|4xl|5xl|6xl|7xl|\[\d{3,}px\])\b/.test(line)) {
          // Allow table cell truncation max-w-[...] inside <td> or <span>
          if (!/<(td|span)\b/.test(line) && !/max-w-\[\d{3,}px\]\s+truncate/.test(line)) {
            violations.push(`${rel}:${idx + 1}: ${line.trim()}`)
          }
        }
        // Container-level mx-auto (w-full mx-auto or h-full ... mx-auto)
        if (/\b(?:w-full|h-full|flex-1)[^"']*\bmx-auto\b/.test(line)) {
          violations.push(`${rel}:${idx + 1}: ${line.trim()}`)
        }
      })
    }

    expect(violations).toEqual([])
  })

  it('No view or layout uses responsive breakpoint prefixes (sm:/md:/lg:/xl:)', () => {
    const targets = [
      path.join(srcRoot, 'layouts/AppLayout.vue'),
      path.join(srcRoot, 'components/FormWizardLayout.vue'),
      ...collectVueFiles(path.join(srcRoot, 'views'))
    ]

    const violations: string[] = []
    for (const file of targets) {
      const rel = path.relative(srcRoot, file).replace(/\\/g, '/')
      const content = fs.readFileSync(file, 'utf8')
      const lines = content.split(/\r?\n/)
      lines.forEach((line, idx) => {
        if (/(?:^|[\s"'])(?:sm|md|lg|xl):[a-zA-Z0-9_-]+/.test(line)) {
          violations.push(`${rel}:${idx + 1}: ${line.trim()}`)
        }
      })
    }

    expect(violations).toEqual([])
  })
})
