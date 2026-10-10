<!--
  APS V1 4号位 — 权限码页（Pkg-8 RBAC 管理 UI 第二版）

  @see lps/LPS.APS.Web/Controllers/RbacController.cs（GET/POST permissions）

  说明：
   - 后端只有 GET / POST，**无 PUT / DELETE** → 权限码一经创建不可改、不可删（只能由 3号位 直接改库）
   - 因此新建入口默认折叠，且二次确认文案强调「不可撤销」
   - 主要用途：管理员在「角色 → 分配权限」之前，先在此确认系统里到底有哪些权限码及其语义
-->
<script setup lang="tsx">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { formatUtcDateTimeSec } from '@/utils/datetime'
import {
  ElAlert,
  ElButton,
  ElDialog,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElOption,
  ElPagination,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus'
import { rbacApi, type CreatePermissionRequest, type PermissionSummaryDto } from '@/api/aps-v1'

/* ==================== 状态 ==================== */

const loading = ref(false)
const permissions = ref<PermissionSummaryDto[]>([])

/** 分页（v1.0 契约） */
const pagination = reactive<{ page: number; pageSize: number; total: number }>({
  page: 1,
  pageSize: 20,
  total: 0
})

/** 服务端过滤：module / actionType / keyword（v1.0 §三.3） */
const filters = reactive<{ module: string; actionType: string; keyword: string }>({
  module: '',
  actionType: '',
  keyword: ''
})

/** 可选模块 / 动作（首页加载 + 翻页时去重，需后端提供完整 module 列表；暂用首页数据近似） */
const moduleOptions = computed<string[]>(() =>
  Array.from(new Set(permissions.value.map((p) => p.module))).sort()
)
const actionOptions = computed<string[]>(() =>
  Array.from(new Set(permissions.value.map((p) => p.actionType))).sort()
)

/** 按模块统计（顶部概览；分页后仅显示当前页的 module 分布） */
const moduleCounts = computed(() =>
  moduleOptions.value.map((m) => ({
    module: m,
    count: permissions.value.filter((p) => p.module === m).length
  }))
)

/* ==================== 加载 ==================== */

async function loadPermissions(): Promise<void> {
  loading.value = true
  try {
    const r = await rbacApi.listPermissions({
      page: pagination.page,
      pageSize: pagination.pageSize,
      module: filters.module || undefined,
      actionType: filters.actionType || undefined,
      keyword: filters.keyword || undefined
    })
    permissions.value = r.items
    pagination.total = r.total
  } finally {
    loading.value = false
  }
}

onMounted(loadPermissions)

/* ==================== 分页 handlers ==================== */

function onPageChange(page: number): void {
  pagination.page = page
  loadPermissions()
}

function onSizeChange(size: number): void {
  pagination.pageSize = size
  pagination.page = 1
  loadPermissions()
}

watch(
  () => [filters.module, filters.actionType, filters.keyword],
  () => {
    pagination.page = 1
    loadPermissions()
  }
)

/* ==================== 新建 Dialog ==================== */

const createVisible = ref(false)
const createSubmitting = ref(false)

const createForm = reactive<{
  permissionCode: string
  permissionName: string
  module: string
  actionType: string
  description: string
}>({
  permissionCode: '',
  permissionName: '',
  module: '',
  actionType: '',
  description: ''
})

const createFormRef = ref<InstanceType<typeof ElForm> | null>(null)

function validateCodeUnique(_rule: unknown, value: string, cb: (err?: Error) => void): void {
  if (permissions.value.some((p) => p.permissionCode === value.trim())) {
    cb(new Error(`权限码「${value}」已存在`))
  } else {
    cb()
  }
}

/** 权限码格式：aps.<module>.<action>，允许多级（如 aps.auth.user.edit） */
function validateCodeFormat(_rule: unknown, value: string, cb: (err?: Error) => void): void {
  if (!/^aps(\.[a-z][a-z0-9_]*){2,}$/.test(value.trim())) {
    cb(new Error('格式须为 aps.<模块>.<动作>，小写字母/数字/下划线，如 aps.plan.view'))
  } else {
    cb()
  }
}

const createRules = {
  permissionCode: [
    { required: true, message: '请输入权限码', trigger: 'blur' },
    { validator: validateCodeFormat, trigger: 'blur' },
    { validator: validateCodeUnique, trigger: 'blur' }
  ],
  permissionName: [{ required: true, message: '请输入权限名称', trigger: 'blur' }],
  module: [{ required: true, message: '请输入所属模块', trigger: 'blur' }],
  actionType: [{ required: true, message: '请输入动作类型', trigger: 'blur' }]
}

function openCreate(): void {
  createForm.permissionCode = ''
  createForm.permissionName = ''
  createForm.module = ''
  createForm.actionType = ''
  createForm.description = ''
  createVisible.value = true
}

/** 输入权限码时自动推导 module / actionType（aps.<module>.<...>.<action>） */
function syncFromCode(): void {
  const parts = createForm.permissionCode.trim().split('.')
  if (parts.length >= 3 && parts[0] === 'aps') {
    if (!createForm.module) createForm.module = parts[1]
    if (!createForm.actionType) createForm.actionType = parts[parts.length - 1]
  }
}

async function submitCreate(): Promise<void> {
  if (!createFormRef.value) return
  const valid = await createFormRef.value.validate().catch(() => false)
  if (!valid) return

  try {
    await ElMessageBox.confirm(
      `确认创建权限码「${createForm.permissionCode.trim()}」？\n\n⚠️ 系统未提供权限码的修改与删除接口，创建后无法在界面上更正，只能由后端直接改库。`,
      '创建确认',
      { type: 'warning', confirmButtonText: '确认创建', cancelButtonText: '取消' }
    )
  } catch {
    return
  }

  createSubmitting.value = true
  try {
    const payload: CreatePermissionRequest = {
      permissionCode: createForm.permissionCode.trim(),
      permissionName: createForm.permissionName.trim(),
      module: createForm.module.trim(),
      actionType: createForm.actionType.trim(),
      description: createForm.description || null
    }
    await rbacApi.createPermission(payload)
    ElMessage.success(`权限码「${payload.permissionCode}」创建成功`)
    createVisible.value = false
    await loadPermissions()
  } catch (err) {
    console.error('submitCreate failed:', err)
  } finally {
    createSubmitting.value = false
  }
}

/* ==================== 工具 ==================== */

const ACTION_TAG_TYPE = (action: string): 'success' | 'warning' | 'danger' | 'info' => {
  if (action === 'view') return 'info'
  if (action === 'edit' || action === 'assign') return 'warning'
  if (action === 'publish' || action === 'dispatch' || action === 'activate') return 'danger'
  return 'success'
}
</script>

<template>
  <div class="rbac-permissions">
    <div class="rbac-toolbar">
      <h2 class="rbac-title">权限码</h2>
      <ElButton type="primary" :disabled="loading" @click="openCreate"> 新建权限码 </ElButton>
    </div>

    <div class="rbac-filters">
      <ElSelect v-model="filters.module" placeholder="所属模块" clearable style="width: 160px">
        <ElOption label="全部模块" value="" />
        <ElOption v-for="m in moduleOptions" :key="m" :label="m" :value="m" />
      </ElSelect>
      <ElSelect v-model="filters.actionType" placeholder="动作类型" clearable style="width: 160px">
        <ElOption label="全部动作" value="" />
        <ElOption v-for="a in actionOptions" :key="a" :label="a" :value="a" />
      </ElSelect>
      <ElInput
        v-model="filters.keyword"
        placeholder="权限码 / 名称 / 说明 模糊匹配"
        clearable
        style="width: 260px"
      />
      <ElButton :loading="loading" @click="loadPermissions">刷新</ElButton>
      <span class="rbac-counts">
        共 <b>{{ permissions.length }}</b> 个权限码
        <ElTag v-for="mc in moduleCounts" :key="mc.module" size="small" type="info">
          {{ mc.module }} {{ mc.count }}
        </ElTag>
      </span>
    </div>

    <ElTable v-loading="loading" :data="permissions" border stripe>
      <ElTableColumn type="index" label="#" width="50" />
      <ElTableColumn prop="permissionCode" label="权限码" width="260" />
      <ElTableColumn prop="permissionName" label="权限名称" width="180" />
      <ElTableColumn prop="module" label="所属模块" width="120" />
      <ElTableColumn label="动作类型" width="120">
        <template #default="{ row }: { row: PermissionSummaryDto }">
          <ElTag :type="ACTION_TAG_TYPE(row.actionType)" size="small">
            {{ row.actionType }}
          </ElTag>
        </template>
      </ElTableColumn>
      <ElTableColumn label="说明" min-width="200">
        <template #default="{ row }: { row: PermissionSummaryDto }">
          {{ row.description || '—' }}
        </template>
      </ElTableColumn>
      <ElTableColumn label="状态" width="90">
        <template #default="{ row }: { row: PermissionSummaryDto }">
          <ElTag :type="row.isActive ? 'success' : 'info'">
            {{ row.isActive ? '启用' : '已停用' }}
          </ElTag>
        </template>
      </ElTableColumn>
      <ElTableColumn label="创建时间" width="170">
        <template #default="{ row }: { row: PermissionSummaryDto }">
          {{ formatUtcDateTimeSec(row.createdAt) }}
        </template>
      </ElTableColumn>
    </ElTable>

    <ElPagination
      :current-page="pagination.page"
      :page-size="pagination.pageSize"
      :total="pagination.total"
      :page-sizes="[20, 50, 100]"
      layout="total, sizes, prev, pager, next, jumper"
      background
      class="rbac-pagination"
      @current-change="onPageChange"
      @size-change="onSizeChange"
    />

    <!-- 新建 Dialog -->
    <ElDialog
      v-model="createVisible"
      title="新建权限码"
      width="560"
      :close-on-click-modal="false"
      :close-on-press-escape="!createSubmitting"
    >
      <ElAlert type="error" :closable="false" show-icon class="rbac-create-alert">
        <template #title> 权限码不可修改、不可删除，创建前请确认命名无误。 </template>
      </ElAlert>
      <ElForm
        ref="createFormRef"
        :model="createForm"
        :rules="createRules"
        label-width="100"
        label-position="right"
      >
        <ElFormItem label="权限码" prop="permissionCode">
          <ElInput
            v-model="createForm.permissionCode"
            placeholder="如 aps.plan.view"
            @blur="syncFromCode"
          />
        </ElFormItem>
        <ElFormItem label="权限名称" prop="permissionName">
          <ElInput v-model="createForm.permissionName" placeholder="如 查看排产" />
        </ElFormItem>
        <ElFormItem label="所属模块" prop="module">
          <ElInput v-model="createForm.module" placeholder="如 plan（由权限码自动推导，可改）" />
        </ElFormItem>
        <ElFormItem label="动作类型" prop="actionType">
          <ElInput
            v-model="createForm.actionType"
            placeholder="如 view（由权限码自动推导，可改）"
          />
        </ElFormItem>
        <ElFormItem label="说明">
          <ElInput v-model="createForm.description" type="textarea" :rows="3" placeholder="可选" />
        </ElFormItem>
      </ElForm>
      <template #footer>
        <ElButton :disabled="createSubmitting" @click="createVisible = false">取消</ElButton>
        <ElButton type="primary" :loading="createSubmitting" @click="submitCreate">创建</ElButton>
      </template>
    </ElDialog>
  </div>
</template>

<style scoped>
.rbac-permissions {
  padding: 16px;
}
.rbac-toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
}
.rbac-title {
  margin: 0;
  font-size: 18px;
  font-weight: 600;
}
.rbac-hint {
  margin-bottom: 16px;
}
.rbac-filters {
  display: flex;
  gap: 12px;
  margin-bottom: 16px;
  flex-wrap: wrap;
  align-items: center;
}
.rbac-counts {
  display: inline-flex;
  gap: 6px;
  flex-wrap: wrap;
  align-items: center;
  margin-left: auto;
  font-size: 13px;
  color: var(--el-text-color-secondary);
}
.rbac-create-alert {
  margin-bottom: 16px;
}
.rbac-pagination {
  margin-top: 16px;
  justify-content: flex-end;
}
</style>
