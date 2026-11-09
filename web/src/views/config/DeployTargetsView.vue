<script setup lang="ts">
import { ref, onMounted } from 'vue'
import {
  ShieldCheck,
  Plus,
  Trash2,
  AlertCircle
} from 'lucide-vue-next'
import AppLayout from '../../layouts/AppLayout.vue'
import AppTable from '../../components/AppTable.vue'
import AppButton from '../../components/AppButton.vue'
import AppInput from '../../components/AppInput.vue'
import AppField from '../../components/AppField.vue'
import ConfirmDialog from '../../components/ConfirmDialog.vue'
import EmptyState from '../../components/EmptyState.vue'
import { useConfigDeployTargetsStore } from '../../stores/configDeployTargets'
import { useToast } from '../../composables/useToast'
import type { DeployTarget } from '../../api/types'
import { ApiError } from '../../api/client'

const store = useConfigDeployTargetsStore()
const toast = useToast()

const isConfirmOpen = ref(false)
const deletingTarget = ref<DeployTarget | null>(null)
const actionLoading = ref(false)
const deleteLoading = ref(false)

const form = ref<{
  namespace: string
  deployment: string
}>({
  namespace: '',
  deployment: ''
})

const formErrors = ref<Record<string, string>>({})

function formatDate(dateStr?: string | null): string {
  if (!dateStr) return '—'
  try {
    const d = new Date(dateStr)
    return d.toLocaleString('vi-VN', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit'
    })
  } catch {
    return dateStr
  }
}

function validateForm(): boolean {
  const errors: Record<string, string> = {}
  const ns = form.value.namespace.trim()
  const dep = form.value.deployment.trim()

  if (!ns) {
    errors.namespace = 'Vui lòng nhập Namespace'
  } else if (ns.length > 63 || !/^[a-z0-9]([-a-z0-9]*[a-z0-9])?$/.test(ns)) {
    errors.namespace = 'Namespace phải tuân theo chuẩn DNS-1123 label (chữ thường, số, dấu gạch ngang; 1-63 ký tự)'
  }

  if (!dep) {
    errors.deployment = 'Vui lòng nhập Deployment'
  } else if (dep.length > 253 || !/^[a-z0-9]([-a-z0-9.]*[a-z0-9])?$/.test(dep)) {
    errors.deployment = 'Deployment phải tuân theo chuẩn DNS-1123 subdomain (1-253 ký tự)'
  }

  formErrors.value = errors
  return Object.keys(errors).length === 0
}

async function handleAdd() {
  if (!validateForm()) return

  actionLoading.value = true
  try {
    await store.createDeployTarget({
      namespace: form.value.namespace.trim(),
      deployment: form.value.deployment.trim()
    })
    toast.success('Đã thêm deployment vào danh sách được phép')
    form.value = { namespace: '', deployment: '' }
    formErrors.value = {}
  } catch (err: any) {
    if (err instanceof ApiError && err.status === 409) {
      toast.error('Deployment này đã tồn tại trong danh sách.')
    } else {
      toast.error(err.message || 'Thêm deployment thất bại')
    }
  } finally {
    actionLoading.value = false
  }
}

function openDeleteConfirm(target: DeployTarget) {
  deletingTarget.value = target
  isConfirmOpen.value = true
}

async function handleConfirmDelete() {
  if (!deletingTarget.value) return

  deleteLoading.value = true
  try {
    await store.deleteDeployTarget(deletingTarget.value.id)
    toast.success(`Đã xoá deployment ${deletingTarget.value.namespace}/${deletingTarget.value.deployment}`)
    isConfirmOpen.value = false
    deletingTarget.value = null
  } catch (err: any) {
    if (err instanceof ApiError && err.status === 409) {
      toast.error(err.problem?.detail || err.message || 'Không thể xoá vì còn pipeline đang sử dụng')
    } else {
      toast.error(err.message || 'Xoá deployment thất bại')
    }
  } finally {
    deleteLoading.value = false
  }
}

onMounted(() => {
  store.fetchDeployTargets().catch(() => {})
})
</script>

<template>
  <AppLayout>
    <div class="space-y-6">
      <!-- Header -->
      <div>
        <h2 class="text-xl font-semibold text-fg">Deployment được phép</h2>
        <p class="text-sm text-muted-fg mt-1">
          Quản lý danh sách namespace và deployment mà Shipit được phép triển khai cập nhật container image.
        </p>
      </div>

      <!-- Help text fixed banner -->
      <div
        class="p-4 rounded-[8px] bg-muted/60 border border-border text-sm text-muted-fg leading-relaxed flex items-start gap-3"
      >
        <ShieldCheck class="w-5 h-5 text-primary flex-shrink-0 mt-0.5" aria-hidden="true" />
        <span>
          Danh sách này giới hạn những deployment mà Shipit sẽ cập nhật. Quyền thực tế của Shipit trong cụm vẫn do RBAC của Kubernetes quyết định: thêm vào đây không cấp thêm quyền, nếu RBAC không cho phép thì lần deploy sẽ lỗi.
        </span>
      </div>

      <!-- Quick Add Form -->
      <div class="bg-card border border-border rounded-[8px] p-4 shadow-sm">
        <h3 class="text-sm font-semibold text-fg mb-3">Thêm deployment nhanh</h3>
        <form @submit.prevent="handleAdd" class="grid grid-cols-1 sm:grid-cols-12 gap-3 items-start">
          <div class="sm:col-span-5">
            <AppField
              label="Namespace"
              required
              :error="formErrors.namespace"
            >
              <template #default="{ id, error, ariaDescribedby }">
                <AppInput
                  :id="id"
                  v-model="form.namespace"
                  placeholder="devops"
                  :error="error"
                  :aria-describedby="ariaDescribedby"
                />
              </template>
            </AppField>
          </div>

          <div class="sm:col-span-5">
            <AppField
              label="Deployment"
              required
              :error="formErrors.deployment"
            >
              <template #default="{ id, error, ariaDescribedby }">
                <AppInput
                  :id="id"
                  v-model="form.deployment"
                  placeholder="shipit-demo"
                  :error="error"
                  :aria-describedby="ariaDescribedby"
                />
              </template>
            </AppField>
          </div>

          <div class="sm:col-span-2 sm:pt-6 flex">
            <AppButton
              type="submit"
              :loading="actionLoading"
              class="w-full h-10"
            >
              <Plus class="w-4 h-4" aria-hidden="true" />
              <span>Thêm</span>
            </AppButton>
          </div>
        </form>
      </div>

      <!-- Error banner -->
      <div
        v-if="store.error"
        role="alert"
        class="flex items-center gap-3 p-4 rounded-[8px] bg-danger/10 border border-danger/20 text-danger text-sm"
      >
        <AlertCircle class="w-5 h-5 flex-shrink-0" />
        <span>{{ store.error }}</span>
      </div>

      <!-- Loading state -->
      <div
        v-if="store.loading && store.deployTargets.length === 0"
        class="flex items-center justify-center p-12 text-muted-fg text-sm"
      >
        <div class="flex items-center gap-2">
          <div class="w-4 h-4 border-2 border-primary border-t-transparent rounded-full animate-spin" />
          <span>Đang tải danh sách deployment được phép...</span>
        </div>
      </div>

      <!-- Empty state -->
      <EmptyState
        v-else-if="!store.loading && store.deployTargets.length === 0"
        title="Chưa có deployment được phép"
        description="Thêm namespace và deployment ở biểu mẫu phía trên để bắt đầu cấu hình pipeline."
        :icon="ShieldCheck"
      />

      <!-- Table of Deploy Targets -->
      <div v-else class="bg-card border border-border rounded-[8px] overflow-hidden shadow-sm">
        <AppTable>
          <template #header>
            <tr>
              <th scope="col" class="py-3 px-4 text-left text-xs font-semibold text-muted-fg uppercase tracking-wider">
                Namespace
              </th>
              <th scope="col" class="py-3 px-4 text-left text-xs font-semibold text-muted-fg uppercase tracking-wider">
                Deployment
              </th>
              <th scope="col" class="py-3 px-4 text-left text-xs font-semibold text-muted-fg uppercase tracking-wider">
                Ngày thêm
              </th>
              <th scope="col" class="py-3 px-4 text-right text-xs font-semibold text-muted-fg uppercase tracking-wider">
                Thao tác
              </th>
            </tr>
          </template>

          <template #default>
            <tr
              v-for="target in store.deployTargets"
              :key="target.id"
              class="hover:bg-muted/40 transition-colors border-b border-border/50 last:border-0"
            >
              <td class="py-3 px-4 text-sm font-medium text-fg">
                {{ target.namespace }}
              </td>
              <td class="py-3 px-4 text-sm text-fg font-mono">
                {{ target.deployment }}
              </td>
              <td class="py-3 px-4 text-sm text-muted-fg">
                {{ formatDate(target.createdAt) }}
              </td>
              <td class="py-3 px-4 text-sm text-right space-x-2">
                <AppButton
                  variant="ghost"
                  size="sm"
                  :aria-label="`Xoá deployment ${target.namespace}/${target.deployment}`"
                  @click="openDeleteConfirm(target)"
                >
                  <Trash2 class="w-4 h-4 text-danger" aria-hidden="true" />
                </AppButton>
              </td>
            </tr>
          </template>
        </AppTable>
      </div>

      <!-- Confirm Dialog for Deletion -->
      <ConfirmDialog
        v-model="isConfirmOpen"
        title="Xoá deployment được phép"
        :message="`Bạn có chắc chắn muốn xoá deployment '${deletingTarget?.namespace}/${deletingTarget?.deployment}' khỏi danh sách được phép không?`"
        confirm-text="Xoá deployment"
        :loading="deleteLoading"
        :danger="true"
        @confirm="handleConfirmDelete"
        @cancel="isConfirmOpen = false"
      />
    </div>
  </AppLayout>
</template>
