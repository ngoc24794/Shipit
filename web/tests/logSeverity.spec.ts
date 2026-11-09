import { describe, it, expect } from 'vitest'
import { classifyLogLine } from '../src/utils/logSeverity'

describe('classifyLogLine', () => {
  it('classifies normal stderr progress lines as muted', () => {
    expect(
      classifyLogLine({
        stream: 'stderr',
        text: "Cloning into '/tmp/shipit/run-3'..."
      })
    ).toBe('muted')

    expect(
      classifyLogLine({
        stream: 'stderr',
        text: '#0 building with "default" instance using docker driver'
      })
    ).toBe('muted')

    expect(
      classifyLogLine({
        stream: 'stderr',
        text: '#8 CACHED'
      })
    ).toBe('muted')

    expect(
      classifyLogLine({
        stream: 'stderr',
        text: '#4 transferring context: 250B done'
      })
    ).toBe('muted')
  })

  it('classifies warning lines on stderr or stdout as warning', () => {
    expect(
      classifyLogLine({
        stream: 'stderr',
        text: 'warning: redirecting to http://x/y.git/'
      })
    ).toBe('warning')

    expect(
      classifyLogLine({
        stream: 'stderr',
        text: 'WARNING! Your password will be stored unencrypted in /home/devops/.docker/config.json.'
      })
    ).toBe('warning')

    expect(
      classifyLogLine({
        stream: 'stdout',
        text: '[WARN] Low memory condition detected'
      })
    ).toBe('warning')

    expect(
      classifyLogLine({
        stream: 'stderr',
        text: 'WARN: Deprecated feature used'
      })
    ).toBe('warning')
  })

  it('classifies actual error lines as error', () => {
    expect(
      classifyLogLine({
        stream: 'stderr',
        text: '#9 ERROR: failed to calculate checksum of ref ...: "/src": not found'
      })
    ).toBe('error')

    expect(
      classifyLogLine({
        stream: 'stderr',
        text: 'ERROR: failed to solve: failed to read dockerfile'
      })
    ).toBe('error')

    expect(
      classifyLogLine({
        stream: 'stderr',
        text: 'fatal: repository not found'
      })
    ).toBe('error')

    expect(
      classifyLogLine({
        stream: 'stderr',
        text: 'COPY failed: file not found in build context'
      })
    ).toBe('error')

    expect(
      classifyLogLine({
        stream: 'stdout',
        text: 'panic: runtime error: index out of range'
      })
    ).toBe('error')

    expect(
      classifyLogLine({
        stream: 'stderr',
        text: 'Unhandled exception. System.InvalidOperationException: Operation is not valid'
      })
    ).toBe('error')

    expect(
      classifyLogLine({
        stream: 'stderr',
        text: 'Exception in thread "main" java.lang.NullPointerException'
      })
    ).toBe('error')
  })

  it('does not classify lines with error in the middle of a sentence as error', () => {
    expect(
      classifyLogLine({
        stream: 'stderr',
        text: 'Build finished with 0 errors and 2 warnings'
      })
    ).toBe('muted')

    expect(
      classifyLogLine({
        stream: 'stderr',
        text: 'error handling routine initialized'
      })
    ).toBe('muted')

    expect(
      classifyLogLine({
        stream: 'stdout',
        text: 'check error codes in documentation'
      })
    ).toBe('normal')
  })

  it('classifies standard stdout lines as normal', () => {
    expect(
      classifyLogLine({
        stream: 'stdout',
        text: 'Login Succeeded'
      })
    ).toBe('normal')

    expect(
      classifyLogLine({
        stream: 'stdout',
        text: '160abc7'
      })
    ).toBe('normal')
  })

  it('classifies system stream lines correctly', () => {
    expect(
      classifyLogLine({
        stream: 'system',
        text: 'Cloning https://... into /tmp/shipit/run-3'
      })
    ).toBe('system')

    expect(
      classifyLogLine({
        stream: 'system',
        text: 'FAILED: docker build exited with code 1'
      })
    ).toBe('system-error')

    expect(
      classifyLogLine({
        stream: 'system',
        text: 'ERROR: slave connection timed out'
      })
    ).toBe('system-error')
  })
})
