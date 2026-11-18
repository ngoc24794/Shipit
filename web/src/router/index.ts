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
    path: '/pipelines/new',
    name: 'pipeline-new',
    component: () => import('../views/config/PipelineFormView.vue'),
    meta: { title: 'Thêm pipeline' }
  },
  {
    path: '/pipelines/:id/edit',
    name: 'pipeline-edit',
    component: () => import('../views/config/PipelineFormView.vue'),
    meta: { title: 'Sửa pipeline' }
  },
  {
    path: '/jobs',
    name: 'jobs',
    component: () => import('../views/jobs/JobsView.vue'),
    meta: { title: 'Job' }
  },
  {
    path: '/jobs/new',
    name: 'job-new',
    component: () => import('../views/jobs/JobFormView.vue'),
    meta: { title: 'Tạo Job' }
  },
  {
    path: '/jobs/:id',
    name: 'job-edit',
    component: () => import('../views/jobs/JobFormView.vue'),
    meta: { title: 'Cấu hình Job' }
  },
  {
    path: '/settings/allowlist',
    name: 'allowlist',
    component: () => import('../views/config/AllowlistView.vue'),
    meta: { title: 'Allowlist' }
  },
  {
    path: '/settings/job-allow',
    redirect: { path: '/settings/allowlist', query: { tab: 'job' } }
  },
  {
    path: '/job-allow',
    redirect: { path: '/settings/allowlist', query: { tab: 'job' } }
  },
  {
    path: '/deploy-targets',
    redirect: { path: '/settings/allowlist', query: { tab: 'deployment' } }
  },
  {
    path: '/slaves',
    name: 'slaves',
    component: () => import('../views/config/SlavesView.vue'),
    meta: { title: 'Máy build' }
  },
  {
    path: '/slaves/new',
    name: 'slave-new',
    component: () => import('../views/config/SlaveFormView.vue'),
    meta: { title: 'Thêm máy build' }
  },
  {
    path: '/slaves/:id/edit',
    name: 'slave-edit',
    component: () => import('../views/config/SlaveFormView.vue'),
    meta: { title: 'Sửa máy build' }
  },
  {
    path: '/credentials',
    name: 'credentials',
    component: () => import('../views/config/CredentialsView.vue'),
    meta: { title: 'Thông tin xác thực' }
  },
  {
    path: '/credentials/new',
    name: 'credential-new',
    component: () => import('../views/config/CredentialFormView.vue'),
    meta: { title: 'Thêm thông tin xác thực' }
  },
  {
    path: '/credentials/:id/edit',
    name: 'credential-edit',
    component: () => import('../views/config/CredentialFormView.vue'),
    meta: { title: 'Sửa thông tin xác thực' }
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
