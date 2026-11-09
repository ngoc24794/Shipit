import { createRouter, createWebHistory, type RouteRecordRaw } from 'vue-router'
import { useAuthStore } from '../stores/auth'

const routes: RouteRecordRaw[] = [
  {
    path: '/login',
    name: 'login',
    component: () => import('../views/LoginView.vue'),
    meta: { title: 'Đăng nhập' }
  },
  {
    path: '/',
    name: 'runs',
    component: () => import('../views/runs/RunsView.vue'),
    meta: { title: 'Lượt chạy' }
  },
  {
    path: '/runs/:id',
    name: 'run-detail',
    component: () => import('../views/runs/RunDetailView.vue'),
    meta: { title: 'Chi tiết lượt chạy' }
  },
  {
    path: '/pipelines',
    name: 'pipelines',
    component: () => import('../views/config/PipelinesView.vue'),
    meta: { title: 'Pipeline' }
  },
  {
    path: '/deploy-targets',
    name: 'deploy-targets',
    component: () => import('../views/config/DeployTargetsView.vue'),
    meta: { title: 'Deployment được phép' }
  },
  {
    path: '/slaves',
    name: 'slaves',
    component: () => import('../views/config/SlavesView.vue'),
    meta: { title: 'Máy build' }
  },
  {
    path: '/credentials',
    name: 'credentials',
    component: () => import('../views/config/CredentialsView.vue'),
    meta: { title: 'Thông tin xác thực' }
  },
  {
    path: '/:pathMatch(.*)*',
    name: 'not-found',
    component: () => import('../views/NotFoundView.vue'),
    meta: { title: 'Không tìm thấy trang' }
  }
]

export const router = createRouter({
  history: createWebHistory(),
  routes
})

router.beforeEach(async (to, _from, next) => {
  const authStore = useAuthStore()

  if (!authStore.initialized) {
    await authStore.checkAuth()
  }

  const isAuthenticated = authStore.isAuthenticated

  if (to.name !== 'login' && !isAuthenticated) {
    next({ name: 'login', query: to.fullPath !== '/' ? { redirect: to.fullPath } : undefined })
  } else if (to.name === 'login' && isAuthenticated) {
    next({ path: '/' })
  } else {
    next()
  }
})

export default router
