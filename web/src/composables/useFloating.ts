import { ref, onBeforeUnmount, watch, type Ref } from 'vue'

export type FloatingPlacement =
  | 'top'
  | 'top-start'
  | 'top-end'
  | 'bottom'
  | 'bottom-start'
  | 'bottom-end'
  | 'left'
  | 'left-start'
  | 'left-end'
  | 'right'
  | 'right-start'
  | 'right-end'

export interface VirtualPoint {
  x: number
  y: number
}

export interface UseFloatingOptions {
  placement?: Ref<FloatingPlacement> | FloatingPlacement
  offset?: number
  flip?: boolean
  padding?: number
}

function getBaseSide(p: FloatingPlacement): 'top' | 'bottom' | 'left' | 'right' {
  return p.split('-')[0] as 'top' | 'bottom' | 'left' | 'right'
}

function getAlign(p: FloatingPlacement): 'start' | 'end' | 'center' {
  const parts = p.split('-')
  return (parts[1] as 'start' | 'end' | undefined) ?? 'center'
}

function computeCoords(
  anchorRect: DOMRect,
  floatingRect: DOMRect,
  placement: FloatingPlacement,
  offset: number
): { x: number; y: number } {
  const side = getBaseSide(placement)
  const align = getAlign(placement)

  let x = 0
  let y = 0

  if (side === 'top') {
    y = anchorRect.top - floatingRect.height - offset
  } else if (side === 'bottom') {
    y = anchorRect.bottom + offset
  } else if (side === 'left') {
    x = anchorRect.left - floatingRect.width - offset
  } else {
    x = anchorRect.right + offset
  }

  if (side === 'top' || side === 'bottom') {
    if (align === 'start') {
      x = anchorRect.left
    } else if (align === 'end') {
      x = anchorRect.right - floatingRect.width
    } else {
      x = anchorRect.left + (anchorRect.width - floatingRect.width) / 2
    }
  } else {
    if (align === 'start') {
      y = anchorRect.top
    } else if (align === 'end') {
      y = anchorRect.bottom - floatingRect.height
    } else {
      y = anchorRect.top + (anchorRect.height - floatingRect.height) / 2
    }
  }

  return { x, y }
}

function flipPlacement(placement: FloatingPlacement): FloatingPlacement {
  const side = getBaseSide(placement)
  const align = getAlign(placement)
  const opposite: Record<string, string> = {
    top: 'bottom',
    bottom: 'top',
    left: 'right',
    right: 'left'
  }
  return (align === 'center' ? opposite[side] : `${opposite[side]}-${align}`) as FloatingPlacement
}

export function useFloating(
  anchorRef: Ref<HTMLElement | null> | Ref<VirtualPoint | null>,
  floatingRef: Ref<HTMLElement | null>,
  isOpen: Ref<boolean>,
  options: UseFloatingOptions = {}
) {
  const offset = options.offset ?? 4
  const enableFlip = options.flip ?? true
  const padding = options.padding ?? 8

  const x = ref(0)
  const y = ref(0)
  const actualPlacement = ref<FloatingPlacement>(
    typeof options.placement === 'string'
      ? options.placement
      : options.placement?.value ?? 'bottom-start'
  )

  function getAnchorRect(): DOMRect | null {
    const anchor = anchorRef.value
    if (!anchor) return null
    if (anchor instanceof HTMLElement) {
      return anchor.getBoundingClientRect()
    }
    return new DOMRect(anchor.x, anchor.y, 0, 0)
  }

  function updatePosition() {
    const anchorRect = getAnchorRect()
    const floatingEl = floatingRef.value
    if (!anchorRect || !floatingEl) return

    const floatingRect = floatingEl.getBoundingClientRect()
    const preferred: FloatingPlacement =
      typeof options.placement === 'string'
        ? options.placement
        : options.placement?.value ?? 'bottom-start'

    const vw = window.innerWidth
    const vh = window.innerHeight

    let chosen = preferred
    let coords = computeCoords(anchorRect, floatingRect, chosen, offset)

    if (enableFlip) {
      const side = getBaseSide(chosen)
      const overflowsTop = side === 'top' && coords.y < padding
      const overflowsBottom = side === 'bottom' && coords.y + floatingRect.height > vh - padding
      const overflowsLeft = side === 'left' && coords.x < padding
      const overflowsRight = side === 'right' && coords.x + floatingRect.width > vw - padding

      if (overflowsTop || overflowsBottom || overflowsLeft || overflowsRight) {
        const flipped = flipPlacement(chosen)
        const flippedCoords = computeCoords(anchorRect, floatingRect, flipped, offset)
        const fSide = getBaseSide(flipped)
        const fitsFlipped =
          (fSide === 'top' && flippedCoords.y >= padding) ||
          (fSide === 'bottom' && flippedCoords.y + floatingRect.height <= vh - padding) ||
          (fSide === 'left' && flippedCoords.x >= padding) ||
          (fSide === 'right' && flippedCoords.x + floatingRect.width <= vw - padding)
        if (fitsFlipped) {
          chosen = flipped
          coords = flippedCoords
        }
      }
    }

    // Clamp inside viewport
    const maxX = Math.max(padding, vw - floatingRect.width - padding)
    const maxY = Math.max(padding, vh - floatingRect.height - padding)
    x.value = Math.round(Math.max(padding, Math.min(maxX, coords.x)))
    y.value = Math.round(Math.max(padding, Math.min(maxY, coords.y)))
    actualPlacement.value = chosen
  }

  function onViewportChange() {
    if (isOpen.value) {
      updatePosition()
    }
  }

  watch(isOpen, (open) => {
    if (open) {
      window.addEventListener('resize', onViewportChange)
      window.addEventListener('scroll', onViewportChange, true)
    } else {
      window.removeEventListener('resize', onViewportChange)
      window.removeEventListener('scroll', onViewportChange, true)
    }
  })

  onBeforeUnmount(() => {
    window.removeEventListener('resize', onViewportChange)
    window.removeEventListener('scroll', onViewportChange, true)
  })

  return {
    x,
    y,
    placement: actualPlacement,
    updatePosition
  }
}
