<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ShieldCheck, ListChecks, Rocket } from 'lucide-vue-next'
import AppLayout from '../../layouts/AppLayout.vue'
import DeployTargetsView from './DeployTargetsView.vue'
import JobAllowListView from '../jobs/JobAllowListView.vue'

type AllowlistTab = 'deployment' | 'job'

const route = useRoute()
const router = useRouter()

const activeTab = computed<AllowlistTab>(() => {
  return route.query.tab === 'job' ? 'job' : 'deployment'
})

function selectTab(tab: AllowlistTab) {
  if (activeTab.value === tab && route.query.tab === tab) return
  router.replace({
    path: '/settings/allowlist',
    query: { ...route.query, tab }
  })
}
</script>

<template>
  <AppLayout>
    <div class="space-y-6">
      <!-- Page Header -->
      <div>
        <h1 class="text-2xl font-bold tracking-tight text-fg flex items-center gap-2.5">
          <ShieldCheck class="w-6 h-6 text-primary" aria-hidden="true" />
          <span>Allowlist</span>
        </h1>
        <p class="text-sm text-muted-fg mt-1">
          Danh sách Deployment được phép cập nhật và tài nguyên Kubernetes Job được phép dùng.
        </p>
      </div>

      <!-- Tabs -->
      <div class="border-b border-border">
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
      >
        <DeployTargetsView embedded />
      </div>

      <div
        v-else
        id="panel-job"
        role="tabpanel"
        aria-labelledby="tab-job"
      >
        <JobAllowListView embedded />
      </div>
    </div>
  </AppLayout>
</template>
