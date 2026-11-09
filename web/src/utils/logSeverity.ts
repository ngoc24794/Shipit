export type LogSeverity = 'normal' | 'muted' | 'warning' | 'error' | 'system' | 'system-error'

export interface LogLineInput {
  stream: string
  text: string
}

function isErrorLine(text: string): boolean {
  // Case-sensitive ERROR / Error: / error:
  // Prevents false positives like "0 errors" or "error handling"
  if (/^\[?ERROR\]?(?:[:\s]|$)/.test(text)) return true
  if (/^Error:/.test(text)) return true
  if (/^error:/.test(text)) return true

  // Case-insensitive patterns for explicit failure indicators
  if (/^\[?fatal\]?[:\s]/i.test(text)) return true
  if (/^\[?FAILED\]?(?:[:\s]|$)/i.test(text)) return true
  if (/^panic:/i.test(text)) return true
  if (/^Unhandled exception\b/i.test(text)) return true
  if (/^[A-Za-z0-9_.]*Exception\b/i.test(text)) return true

  // Build instruction failures such as "COPY failed:", "RUN failed:", "failed:"
  if (/^(?:[A-Za-z0-9_-]+\s+)?failed:/i.test(text)) return true

  return false
}

function isWarningLine(text: string): boolean {
  // Case-insensitive warning indicators: warning:, WARNING!, WARN, [WARN]
  return /^(?:warning[:!]|\[?warn(?:ing)?\]?(?:[:!\s]|$))/i.test(text)
}

/**
 * Classifies a log line into a severity level based on its stream and text content.
 *
 * Rules:
 * - 'system': standard system stream lines. If text starts with FAILED: or system failure -> 'system-error'.
 * - 'error': real error indicators (ERROR, Error:, error:, fatal:, FAILED, panic:, Unhandled exception, Exception, COPY failed:)
 * - 'warning': warning indicators (warning:, WARNING!, WARN, [WARN])
 * - 'muted': other stderr lines (informational progress like git clone, BuildKit output)
 * - 'normal': other stdout lines
 */
export function classifyLogLine(line: LogLineInput): LogSeverity {
  if (line.stream === 'system') {
    const trimmed = line.text.trimStart()
    if (/^(?:FAILED\b|ERROR\b|fatal:)/i.test(trimmed)) {
      return 'system-error'
    }
    return 'system'
  }

  // Strip leading whitespace and BuildKit prefix `#<number> `
  const stripped = line.text.trimStart().replace(/^#\d+\s+/, '')

  if (isErrorLine(stripped)) {
    return 'error'
  }

  if (isWarningLine(stripped)) {
    return 'warning'
  }

  if (line.stream === 'stderr') {
    return 'muted'
  }

  return 'normal'
}
