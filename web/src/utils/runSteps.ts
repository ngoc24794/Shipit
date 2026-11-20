import type { StepName } from '../api/types'

export type RunTargetKind = 'pipeline' | 'job'

export interface StepDefinition {
  name: StepName
  label: string
  locked?: boolean
}

export const PIPELINE_STEPS: readonly StepDefinition[] = [
  { name: 'checkout', label: 'Checkout', locked: true },
  { name: 'build', label: 'Build' },
  { name: 'push', label: 'Push' },
  { name: 'deploy', label: 'Deploy' }
] as const

export const JOB_STEPS: readonly StepDefinition[] = [
  { name: 'checkout', label: 'Checkout', locked: true },
  { name: 'build', label: 'Build' },
  { name: 'push', label: 'Push' },
  { name: 'runjob', label: 'Chạy Job' }
] as const

export function getStepsForKind(kind: RunTargetKind): readonly StepDefinition[] {
  return kind === 'job' ? JOB_STEPS : PIPELINE_STEPS
}

/**
 * Applies a checkbox toggle for `stepName` while enforcing the backend suffix rule:
 * - `checkout` is always required and cannot be unchecked.
 * - Unchecking a step also unchecks all subsequent steps.
 * - Checking a step enables it (and any earlier steps).
 */
export function applyStepToggle(
  kind: RunTargetKind,
  currentSkipSteps: readonly StepName[],
  stepName: StepName,
  shouldRun: boolean
): StepName[] {
  const steps = getStepsForKind(kind)
  const idx = steps.findIndex((s) => s.name === stepName)
  if (idx <= 0) {
    // checkout (idx === 0) is locked on; unknown steps leave state unchanged
    return [...currentSkipSteps]
  }

  if (!shouldRun) {
    // Unchecking step at idx skips idx .. end
    return steps.slice(idx).map((s) => s.name)
  }

  // Checking step at idx enables 0 .. idx; steps after idx remain skipped
  return steps.slice(idx + 1).map((s) => s.name)
}

/**
 * Returns true if the step checkbox should be disabled (checkout is locked,
 * or an earlier step is already unchecked/skipped).
 */
export function isStepCheckboxDisabled(
  kind: RunTargetKind,
  skipSteps: readonly StepName[],
  stepName: StepName
): boolean {
  const steps = getStepsForKind(kind)
  const idx = steps.findIndex((s) => s.name === stepName)
  if (idx <= 0) return true
  const prevStep = steps[idx - 1]
  return skipSteps.includes(prevStep.name)
}

/**
 * Formats a Vietnamese summary line for skipped steps.
 */
export function formatSkipSummary(
  kind: RunTargetKind,
  skipSteps: readonly StepName[]
): string {
  if (skipSteps.length === 0) {
    return 'Chạy đầy đủ tất cả các bước'
  }
  const steps = getStepsForKind(kind)
  const labels = steps
    .filter((s) => skipSteps.includes(s.name))
    .map((s) => s.label)
  return `Bỏ qua: ${labels.join(', ')}`
}

/**
 * Validates `skipSteps` against the S1/S2 backend contract:
 * - unknown or duplicate step names -> error
 * - skipping `checkout` -> error
 * - must be a trailing suffix of the ordered steps -> error
 */
export function validateSkipSteps(
  kind: RunTargetKind,
  skipSteps?: readonly StepName[]
): string | null {
  if (!skipSteps || skipSteps.length === 0) return null
  const steps = getStepsForKind(kind)
  const order = steps.map((s) => s.name)

  const seen = new Set<StepName>()
  for (const name of skipSteps) {
    if (!order.includes(name)) {
      return `Unknown step '${name}'.`
    }
    if (seen.has(name)) {
      return `Duplicate step '${name}'.`
    }
    seen.add(name)
  }

  if (seen.has('checkout')) {
    return 'Cannot skip checkout step.'
  }

  const expectedSuffix = order.slice(order.length - skipSteps.length)
  const isSuffix = expectedSuffix.every((s) => seen.has(s))
  if (!isSuffix) {
    return 'Skipped steps must form a trailing suffix of the step order.'
  }

  return null
}
