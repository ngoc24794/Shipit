<script setup lang="ts">
import { computed, ref, watch, onActivated } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ShieldCheck, ListChecks, Rocket, RefreshCw } from 'lucide-vue-next'
import AppButton from '../../components/AppButton.vue'
import { useApi } from '../../api'
import { useToast } from '../../composables/useToast'
import DeployTargetsView from './DeployTargetsView.vue'
import JobAllowListView from '../jobs/JobAllowListView.vue'

defineOptions({ name: 'AllowlistView' })

type AllowlistTab = 'deployment' | 'job'

const route = useRoute()
const router = useRouter()
const api = useApi()
const toast = useToast()

const syncLoading = ref(false)
const rememberedTab = ref<AllowlistTab>(route.query.tab === 'job' ? 'job' : 'deployment')

watch(
  () => route.query.tab,
  (tab) => {
    if (route.name === 'allowlist' && (tab === 'job' || tab === 'deployment')) {
      rememberedTab.value = tab
    }
  }
)

onActivated(() => {
  if (route.query.tab === 'job' || route.query.tab === 'deployment') {
    rememberedTab.value = route.query.tab
  } else if (rememberedTab.value !== 'deployment') {
    router.replace({
      path: '/settings/allowlist',
      query: { ...route.query, tab: rememberedTab.value }
    }).catch(() => {})
  }
})

const activeTab = computed<AllowlistTab>(() => {
  if (route.query.tab === 'job' || route.query.tab === 'deployment') {
    return route.query.tab
  }
  return rememberedTab.value
})

function selectTab(tab: AllowlistTab) {
  rememberedTab.value = tab
  if (activeTab.value === tab && route.query.tab === tab) return
  router.replace({
    path: '/settings/allowlist',
    query: { ...route.query, tab }
  })
}

async function handleSyncRbac() {
  if (syncLoading.value) return
  syncLoading.value = true
  try {
    const res = await api.syncRbac()
    if (res.allSucceeded) {
      if (res.results.length === 0) {
        toast.success('Không có namespace nào cần đồng bộ RBAC')
      } else {
        toast.success(`Đã đồng bộ RBAC cho ${res.results.length} namespace`)
      }
    } else {
      const failed = res.results.filter((r) => !r.succeeded)
      const detail = failed
        .map((r) => `${r.namespace}: ${r.error || 'Lỗi không xác định'}`)
        .join('; ')
      toast.warn(
        `Đồng bộ RBAC hoàn tất với ${failed.length}/${res.results.length} namespace lỗi: ${detail}`,
        8000
      )
    }
  } catch (err: any) {
    toast.error(err?.problem?.detail || err?.message || 'Đồng bộ RBAC thất bại')
  } finally {
    syncLoading.value = false
  }
}
</script>

<template>
  <div class="flex-1 min-h-0 flex flex-col gap-5">
      <!-- Page Header -->
      <div class="shrink-0 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 class="text-2xl font-bold tracking-tight text-fg flex items-center gap-2.5">
            <ShieldCheck class="w-6 h-6 text-primary" aria-hidden="true" />
            <span>Allowlist</span>
          </h1>
          <p class="text-sm text-muted-fg mt-1">
            Danh sách Deployment được phép cập nhật và tài nguyên Kubernetes Job được phép dùng.
          </p>
        </div>

        <AppButton
          variant="secondary"
          size="md"
          :loading="syncLoading"
          :disabled="syncLoading"
          aria-label="Đồng bộ RBAC"
          @click="handleSyncRbac"
        >
          <RefreshCw v-if="!syncLoading" class="w-4 h-4" aria-hidden="true" />
          <span>Đồng bộ RBAC</span>
        </AppButton>
      </div>

      <!-- Tabs -->
      <div class="shrink-0 border-b border-border">
        <div role="tablist" aria-label="Phân loại Allowlist" class="flex items-center gap-1 -mb-px">
          <button
            id="tab-deployment"
            type="button"
            role="tab"
            :aria-selected="activeTab === 'deployment'"
            aria-controls="panel-deployment"
            :class="[
              'inline-flex items-center gap-2 px-4 py-2.5 text-sm font-medium border-b-2 transition-colors cursor-pointer focus-visible:outline-2 focus-visible:outline-ring',
              activeTab === 'deployment'
                ? 'border-primary text-primary font-semibold'
                : 'border-transparent text-muted-fg hover:text-fg hover:border-border'
            ]"
            @click="selectTab('deployment')"
          >
            <Rocket class="w-4 h-4" aria-hidden="true" />
            <span>Deployment</span>
          </button>

          <button
            id="tab-job"
            type="button"
            role="tab"
            :aria-selected="activeTab === 'job'"
            aria-controls="panel-job"
            :class="[
              'inline-flex items-center gap-2 px-4 py-2.5 text-sm font-medium border-b-2 transition-colors cursor-pointer focus-visible:outline-2 focus-visible:outline-ring',
              activeTab === 'job'
                ? 'border-primary text-primary font-semibold'
                : 'border-transparent text-muted-fg hover:text-fg hover:border-border'
            ]"
            @click="selectTab('job')"
          >
            <ListChecks class="w-4 h-4" aria-hidden="true" />
            <span>Job</span>
          </button>
        </div>
      </div>

      <!-- Tab Panels -->
      <div
        v-if="activeTab === 'deployment'"
        id="panel-deployment"
        role="tabpanel"
        aria-labelledby="tab-deployment"
        class="flex-1 min-h-0 flex flex-col"
      >
        <DeployTargetsView embedded />
      </div>

      <div
        v-else
        id="panel-job"
        role="tabpanel"
        aria-labelledby="tab-job"
        class="flex-1 min-h-0 flex flex-col"
      >
      <JobAllowListView embedded />
    </div>
  </div>
</template>
