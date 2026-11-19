import { ref, watch, nextTick, onBeforeUnmount, type Ref, type CSSProperties } from 'vue'

export interface UsePopoverOptions {
  matchWidth?: boolean
  offset?: number
  minFlipSpace?: number
}

export function usePopover(
  triggerRef: Ref<HTMLElement | null>,
  popupRef: Ref<HTMLElement | null>,
  isOpen: Ref<boolean>,
  onClose: () => void,
  options: UsePopoverOptions = {}
) {
  const matchWidth = options.matchWidth ?? true
  const offset = options.offset ?? 4
  const minFlipSpace = options.minFlipSpace ?? 180

  const placement = ref<'bottom' | 'top'>('bottom')
  const popupStyle = ref<CSSProperties>({
    position: 'fixed',
    top: '0px',
    left: '0px',
    zIndex: 9999
  })

  function updatePosition() {
    const trigger = triggerRef.value
    if (!trigger) return

    const rect = trigger.getBoundingClientRect()
    const viewportH = window.innerHeight || 768
    const viewportW = window.innerWidth || 1366

    const spaceBelow = viewportH - rect.bottom - offset
    const spaceAbove = rect.top - offset
    const shouldFlip = spaceBelow < minFlipSpace && spaceAbove > spaceBelow

    placement.value = shouldFlip ? 'top' : 'bottom'

    const left = Math.max(4, Math.min(rect.left, viewportW - rect.width - 4))
    const style: CSSProperties = {
      position: 'fixed',
      left: `${left}px`,
      zIndex: 9999
    }

    if (matchWidth) {
      style.width = `${rect.width}px`
      style.minWidth = `${rect.width}px`
    }

    if (shouldFlip) {
      style.bottom = `${viewportH - rect.top + offset}px`
      style.maxHeight = `${Math.max(120, spaceAbove - 8)}px`
    } else {
      style.top = `${rect.bottom + offset}px`
      style.maxHeight = `${Math.max(120, spaceBelow - 8)}px`
    }

    popupStyle.value = style
  }

  function handlePointerDown(event: MouseEvent) {
    const target = event.target as Node | null
    if (!target) return
    if (triggerRef.value?.contains(target)) return
    if (popupRef.value?.contains(target)) return
    onClose()
  }

  function handleScroll(event: Event) {
    const target = event.target as Node | null
    if (target && popupRef.value && (popupRef.value === target || popupRef.value.contains(target))) {
      return
    }
    onClose()
  }

  function handleResize() {
    updatePosition()
  }

  function attachListeners() {
    document.addEventListener('mousedown', handlePointerDown, true)
    window.addEventListener('scroll', handleScroll, true)
    window.addEventListener('resize', handleResize)
  }

  function detachListeners() {
    document.removeEventListener('mousedown', handlePointerDown, true)
    window.removeEventListener('scroll', handleScroll, true)
    window.removeEventListener('resize', handleResize)
  }

  watch(isOpen, async (open) => {
    if (open) {
      await nextTick()
      updatePosition()
      attachListeners()
    } else {
      detachListeners()
    }
  })

  onBeforeUnmount(() => {
    detachListeners()
  })

  return {
    popupStyle,
    placement,
    updatePosition
  }
}
