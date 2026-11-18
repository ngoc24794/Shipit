import { onMounted, onBeforeUnmount, type Ref } from 'vue'
import { useRouter } from 'vue-router'

export function useUnsavedChangesGuard(
  isDirty: Ref<boolean>,
  message = 'Bạn có thay đổi chưa lưu. Bạn có chắc muốn rời khỏi trang?'
) {
  const router = useRouter()
  let removeRouteGuard: (() => void) | null = null

  function handleBeforeUnload(e: BeforeUnloadEvent) {
    if (!isDirty.value) return
    e.preventDefault()
    e.returnValue = ''
  }

  onMounted(() => {
    window.addEventListener('beforeunload', handleBeforeUnload)
    removeRouteGuard = router.beforeEach((to, from) => {
      if (to.fullPath === from.fullPath) return true
      if (!isDirty.value) return true
      return window.confirm(message)
    })
  })

  onBeforeUnmount(() => {
    window.removeEventListener('beforeunload', handleBeforeUnload)
    if (removeRouteGuard) {
      removeRouteGuard()
      removeRouteGuard = null
    }
  })
}
