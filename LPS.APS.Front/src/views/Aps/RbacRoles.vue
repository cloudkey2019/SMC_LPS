<!--
  APS V1 4号位 — 角色管理页（Pkg-8 RBAC 管理 UI）

  @see lps/LPS.APS.Web/Controllers/RbacController.cs
  @see frontNew/src/api/aps-v1/rbac.ts

  含：
   - 角色 CRUD（新建/编辑/停用/删除）
   - 分配权限（按 permission.module 分组）
   - 分配范围（按 scopeType 分组）

  红线：
   - 系统角色保护：isSystemRole=true → 删除/停用按钮 disabled（Tooltip 明示）
   - 覆盖式 PUT（permissions / scopes）：默认全不勾选 + 红色 Alert + 二次确认
-->
<script setup lang="tsx">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { formatUtcDateTimeSec } from '@/utils/datetime'
import {
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
  ElSwitch,
  ElTable,
  ElTableColumn,
  ElTag,
  ElTooltip
} from 'element-plus'
import {
  rbacApi,
  type CreateRoleRequest,
  type UpdateRoleRequest,
  type RoleSummaryDto,
  type PermissionSummaryDto,
  type DataScopePolicyDto,
  SCOPE_TYPE_LABELS
} from '@/api/aps-v1'
import AssignDialog, { type AssignGroup } from './components/AssignDialog.vue'

/* ==================== 状态 ==================== */

const loading = ref(false)
const roles = ref<RoleSummaryDto[]>([])
const permissions = ref<PermissionSummaryDto[]>([])
const scopes = ref<DataScopePolicyDto[]>([])

/** 分页（v1.0 契约） */
const pagination = reactive<{ page: number; pageSize: number; total: number }>({
  page: 1,
  pageSize: 20,
  total: 0
})

/** 角色过滤（服务端：keyword 模糊 + isSystem 精确；v1.0 §三.2） */
const filters = reactive<{ keyword: string; isSystem: '' | 'true' | 'false' }>({
  keyword: '',
  isSystem: ''
})

/* ==================== 加载 ==================== */

async function loadRoles(): Promise<void> {
  loading.value = true
  try {
    const r = await rbacApi.listRoles({
      page: pagination.page,
      pageSize: pagination.pageSize,
      keyword: filters.keyword || undefined,
      isSystem: filters.isSystem === '' ? undefined : filters.isSystem === 'true'
    })
    roles.value = r.items
    pagination.total = r.total
  } finally {
    loading.value = false
  }
}

async function loadAux(): Promise<void> {
  // 权限码 + 范围列表是 AssignDialog 预勾选用；pageSize=200 拿到完整集（v1.0 上限）
  const [p, s] = await Promise.all([
    rbacApi.listPermissions({ pageSize: 200 }),
    rbacApi.listScopes({ pageSize: 200 })
  ])
  permissions.value = p.items
  scopes.value = s.items
}

async function loadAll(): Promise<void> {
  await Promise.all([loadRoles(), loadAux()])
}

onMounted(loadAll)

/* ==================== 分页 handlers ==================== */

function onPageChange(page: number): void {
  pagination.page = page
  loadRoles()
}

function onSizeChange(size: number): void {
  pagination.pageSize = size
  pagination.page = 1
  loadRoles()
}

watch(
  () => [filters.keyword, filters.isSystem],
  () => {
    pagination.page = 1
    loadRoles()
  }
)

/* ==================== 新建 / 编辑 Dialog ==================== */

const editVisible = ref(false)
const editMode = ref<'create' | 'edit'>('create')
const editingId = ref<number | null>(null)
const editSubmitting = ref(false)

interface RoleForm {
  roleCode: string
  roleName: string
  description: string
  isActive: boolean
}

const editForm = reactive<RoleForm>({
  roleCode: '',
  roleName: '',
  description: '',
  isActive: true
})

const editFormRef = ref<InstanceType<typeof ElForm> | null>(null)

function resetEditForm(): void {
  editForm.roleCode = ''
  editForm.roleName = ''
  editForm.description = ''
  editForm.isActive = true
  editingId.value = null
}

function openCreate(): void {
  resetEditForm()
  editMode.value = 'create'
  editVisible.value = true
}

function openEdit(row: RoleSummaryDto): void {
  resetEditForm()
  editMode.value = 'edit'
  editingId.value = row.id
  editForm.roleCode = row.roleCode
  editForm.roleName = row.roleName
  editForm.description = row.description ?? ''
  editForm.isActive = row.isActive
  editVisible.value = true
}

const editRules = {
  roleCode: [
    { required: true, message: '请输入角色编码', trigger: 'blur' },
    {
      pattern: /^aps\.[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)*$/,
      message:
        '格式须以 aps. 开头，小写字母/数字/下划线分段（如 aps.planner / aps.auth.user.edit）',
      trigger: 'blur'
    }
  ],
  roleName: [{ required: true, message: '请输入角色名称', trigger: 'blur' }]
}

async function submitEdit(): Promise<void> {
  if (!editFormRef.value) return
  const valid = await editFormRef.value.validate().catch(() => false)
  if (!valid) return

  editSubmitting.value = true
  try {
    if (editMode.value === 'create') {
      const payload: CreateRoleRequest = {
        roleCode: editForm.roleCode,
        roleName: editForm.roleName,
        description: editForm.description || null
      }
      await rbacApi.createRole(payload)
      ElMessage.success(`角色「${payload.roleCode}」创建成功`)
    } else if (editingId.value !== null) {
      const payload: UpdateRoleRequest = {
        roleName: editForm.roleName,
        isActive: editForm.isActive,
        description: editForm.description || null
      }
      await rbacApi.updateRole(editingId.value, payload)
      ElMessage.success('角色已更新')
    }
    editVisible.value = false
    await loadAll()
  } catch (err) {
    console.error('submitEdit failed:', err)
  } finally {
    editSubmitting.value = false
  }
}

/* ==================== 停用 / 删除 ==================== */

async function handleDeactivate(row: RoleSummaryDto): Promise<void> {
  if (row.isSystemRole) {
    ElMessage.warning('系统角色不可停用')
    return
  }
  try {
    await ElMessageBox.confirm(
      `确认停用角色「${row.roleCode}」？停用后持有该角色的用户将失去对应权限。`,
      '停用确认',
      { type: 'warning', confirmButtonText: '确认停用', cancelButtonText: '取消' }
    )
  } catch {
    return
  }
  try {
    await rbacApi.updateRole(row.id, {
      roleName: row.roleName,
      isActive: false,
      description: row.description ?? null
    })
    ElMessage.success(`角色「${row.roleCode}」已停用`)
    await loadAll()
  } catch (err) {
    console.error('handleDeactivate failed:', err)
  }
}

async function handleDelete(row: RoleSummaryDto): Promise<void> {
  if (row.isSystemRole) {
    ElMessage.warning('系统角色不可删除')
    return
  }
  try {
    await ElMessageBox.confirm(
      `确认删除角色「${row.roleCode}」？该操作为软删除（IsActive=0）。`,
      '删除确认',
      { type: 'error', confirmButtonText: '确认删除', cancelButtonText: '取消' }
    )
  } catch {
    return
  }
  try {
    await rbacApi.deleteRole(row.id)
    ElMessage.success(`角色「${row.roleCode}」已删除`)
    await loadAll()
  } catch (err) {
    console.error('handleDelete failed:', err)
  }
}

/* ==================== 分配权限 Dialog（按 module 分组） ==================== */

const assignPermVisible = ref(false)
const assignPermTarget = ref<RoleSummaryDto | null>(null)
const assignPermSubmitting = ref(false)
const assignPermPreCheckedIds = ref<number[]>([])
const assignPermLoadingPrecheck = ref(false)
const assignPermShowReadBackWarning = ref(false)

const permissionGroups = computed<AssignGroup[]>(() => {
  // 按 permission.module 分组（如 plan / ctp / rule / auth / audit 等）
  const map = new Map<string, PermissionSummaryDto[]>()
  for (const p of permissions.value) {
    if (!p.isActive) continue
    const arr = map.get(p.module) ?? []
    arr.push(p)
    map.set(p.module, arr)
  }
  const groups: AssignGroup[] = []
  for (const [module, perms] of map) {
    groups.push({
      key: module,
      label: `${module}（${perms.length}）`,
      options: perms.map((p) => ({ id: p.id, label: p.permissionCode, sub: p.permissionName }))
    })
  }
  return groups.sort((a, b) => a.key.localeCompare(b.key))
})

async function openAssignPerm(row: RoleSummaryDto): Promise<void> {
  assignPermTarget.value = row
  assignPermPreCheckedIds.value = []
  assignPermShowReadBackWarning.value = false
  assignPermLoadingPrecheck.value = true
  assignPermVisible.value = true
  try {
    const currentPerms = await rbacApi.getRolePermissions(row.id)
    assignPermPreCheckedIds.value = currentPerms.map((p) => p.id)
  } catch (err) {
    console.error('openAssignPerm: getRolePermissions failed', err)
    assignPermPreCheckedIds.value = []
    assignPermShowReadBackWarning.value = true
  } finally {
    assignPermLoadingPrecheck.value = false
  }
}

async function handleAssignPermSubmit(ids: number[]): Promise<void> {
  if (!assignPermTarget.value) return
  assignPermSubmitting.value = true
  try {
    await rbacApi.assignRolePermissions(assignPermTarget.value.id, ids)
    ElMessage.success(
      ids.length === 0
        ? `已清空角色「${assignPermTarget.value.roleCode}」的全部权限`
        : `角色「${assignPermTarget.value.roleCode}」权限已更新为 ${ids.length} 项`
    )
    assignPermVisible.value = false
  } catch (err) {
    console.error('handleAssignPermSubmit failed:', err)
  } finally {
    assignPermSubmitting.value = false
  }
}

/* ==================== 分配范围 Dialog（按 scopeType 分组） ==================== */

const assignScopeVisible = ref(false)
const assignScopeTarget = ref<RoleSummaryDto | null>(null)
const assignScopeSubmitting = ref(false)
const assignScopePreCheckedIds = ref<number[]>([])
const assignScopeLoadingPrecheck = ref(false)
const assignScopeShowReadBackWarning = ref(false)

const scopeGroups = computed<AssignGroup[]>(() => {
  const map = new Map<string, DataScopePolicyDto[]>()
  for (const s of scopes.value) {
    const arr = map.get(s.scopeType) ?? []
    arr.push(s)
    map.set(s.scopeType, arr)
  }
  const groups: AssignGroup[] = []
  for (const [st, list] of map) {
    groups.push({
      key: st,
      label: `${SCOPE_TYPE_LABELS[st as keyof typeof SCOPE_TYPE_LABELS] ?? st}（${list.length}）`,
      options: list.map((s) => ({ id: s.id, label: s.scopeValue, sub: s.description ?? undefined }))
    })
  }
  return groups
})

async function openAssignScope(row: RoleSummaryDto): Promise<void> {
  assignScopeTarget.value = row
  assignScopePreCheckedIds.value = []
  assignScopeShowReadBackWarning.value = false
  assignScopeLoadingPrecheck.value = true
  assignScopeVisible.value = true
  try {
    const currentScopes = await rbacApi.getRoleScopes(row.id)
    assignScopePreCheckedIds.value = currentScopes.map((s) => s.id)
  } catch (err) {
    console.error('openAssignScope: getRoleScopes failed', err)
    assignScopePreCheckedIds.value = []
    assignScopeShowReadBackWarning.value = true
  } finally {
    assignScopeLoadingPrecheck.value = false
  }
}

async function handleAssignScopeSubmit(ids: number[]): Promise<void> {
  if (!assignScopeTarget.value) return
  assignScopeSubmitting.value = true
  try {
    await rbacApi.assignRoleScopes(assignScopeTarget.value.id, ids)
    ElMessage.success(
      ids.length === 0
        ? `已清空角色「${assignScopeTarget.value.roleCode}」的全部业务范围`
        : `角色「${assignScopeTarget.value.roleCode}」业务范围已更新为 ${ids.length} 项`
    )
    assignScopeVisible.value = false
  } catch (err) {
    console.error('handleAssignScopeSubmit failed:', err)
  } finally {
    assignScopeSubmitting.value = false
  }
}

/* ==================== 工具 ==================== */
</script>

<template>
  <div class="rbac-roles">
    <div class="rbac-toolbar">
      <h2 class="rbac-title">角色管理</h2>
      <ElButton type="primary" :disabled="loading" @click="openCreate"> 新建角色 </ElButton>
    </div>

    <div class="rbac-filters">
      <ElInput
        v-model="filters.keyword"
        placeholder="角色编码 / 名称 模糊匹配"
        clearable
        style="width: 260px"
      />
      <ElSelect v-model="filters.isSystem" placeholder="属性" clearable style="width: 140px">
        <ElOption label="全部属性" value="" />
        <ElOption label="仅看系统角色" value="true" />
        <ElOption label="仅看自定义角色" value="false" />
      </ElSelect>
      <ElButton @click="loadRoles" :loading="loading">刷新</ElButton>
    </div>

    <ElTable :data="roles" v-loading="loading" border stripe>
      <ElTableColumn type="index" label="#" width="50" />
      <ElTableColumn prop="roleCode" label="角色编码" width="220" />
      <ElTableColumn prop="roleName" label="角色名称" width="180" />
      <ElTableColumn prop="description" label="描述" min-width="200">
        <template #default="{ row }: { row: RoleSummaryDto }">
          {{ row.description || '—' }}
        </template>
      </ElTableColumn>
      <ElTableColumn label="属性" width="110">
        <template #default="{ row }: { row: RoleSummaryDto }">
          <ElTag v-if="row.isSystemRole" type="warning">系统角色</ElTag>
          <ElTag v-else type="info">自定义</ElTag>
        </template>
      </ElTableColumn>
      <ElTableColumn label="状态" width="90">
        <template #default="{ row }: { row: RoleSummaryDto }">
          <ElTag :type="row.isActive ? 'success' : 'info'">
            {{ row.isActive ? '启用' : '已停用' }}
          </ElTag>
        </template>
      </ElTableColumn>
      <ElTableColumn label="创建时间" width="170">
        <template #default="{ row }: { row: RoleSummaryDto }">
          {{ formatUtcDateTimeSec(row.createdAt) }}
        </template>
      </ElTableColumn>
      <ElTableColumn label="操作" width="400" fixed="right">
        <template #default="{ row }: { row: RoleSummaryDto }">
          <ElButton size="small" @click="openEdit(row)">编辑</ElButton>
          <ElButton size="small" type="primary" @click="openAssignPerm(row)"> 分配权限 </ElButton>
          <ElButton size="small" type="success" @click="openAssignScope(row)"> 分配范围 </ElButton>
          <ElTooltip v-if="row.isSystemRole" content="系统角色不可停用" placement="top">
            <span class="rbac-action-wrap">
              <ElButton size="small" type="warning" disabled>停用</ElButton>
            </span>
          </ElTooltip>
          <ElButton v-else size="small" type="warning" @click="handleDeactivate(row)">
            停用
          </ElButton>
          <ElTooltip v-if="row.isSystemRole" content="系统角色不可删除" placement="top">
            <span class="rbac-action-wrap">
              <ElButton size="small" type="danger" disabled>删除</ElButton>
            </span>
          </ElTooltip>
          <ElButton v-else size="small" type="danger" @click="handleDelete(row)"> 删除 </ElButton>
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

    <!-- 新建 / 编辑 Dialog -->
    <ElDialog
      v-model="editVisible"
      :title="editMode === 'create' ? '新建角色' : '编辑角色'"
      width="520"
      :close-on-click-modal="false"
      :close-on-press-escape="!editSubmitting"
    >
      <ElForm
        ref="editFormRef"
        :model="editForm"
        :rules="editRules"
        label-width="100"
        label-position="right"
      >
        <ElFormItem label="角色编码" prop="roleCode">
          <ElInput
            v-model="editForm.roleCode"
            :disabled="editMode === 'edit'"
            placeholder="如 aps.planner"
          />
        </ElFormItem>
        <ElFormItem label="角色名称" prop="roleName">
          <ElInput v-model="editForm.roleName" placeholder="如 计划员" />
        </ElFormItem>
        <ElFormItem label="描述">
          <ElInput v-model="editForm.description" type="textarea" :rows="3" placeholder="可选" />
        </ElFormItem>
        <ElFormItem v-if="editMode === 'edit'" label="启用">
          <ElSwitch v-model="editForm.isActive" />
        </ElFormItem>
      </ElForm>
      <template #footer>
        <ElButton :disabled="editSubmitting" @click="editVisible = false">取消</ElButton>
        <ElButton type="primary" :loading="editSubmitting" @click="submitEdit">
          {{ editMode === 'create' ? '创建' : '保存' }}
        </ElButton>
      </template>
    </ElDialog>

    <!-- 分配权限 Dialog -->
    <AssignDialog
      v-if="assignPermTarget"
      v-model="assignPermVisible"
      title="分配权限"
      dimension="权限码"
      :target-label="`角色 ${assignPermTarget.roleCode}`"
      :groups="permissionGroups"
      :submitting="assignPermSubmitting"
      :pre-checked-ids="assignPermPreCheckedIds"
      :loading-precheck="assignPermLoadingPrecheck"
      :show-read-back-warning="assignPermShowReadBackWarning"
      @submit="handleAssignPermSubmit"
    />

    <!-- 分配范围 Dialog -->
    <AssignDialog
      v-if="assignScopeTarget"
      v-model="assignScopeVisible"
      title="分配业务范围"
      dimension="业务范围"
      :target-label="`角色 ${assignScopeTarget.roleCode}`"
      :groups="scopeGroups"
      :submitting="assignScopeSubmitting"
      :pre-checked-ids="assignScopePreCheckedIds"
      :loading-precheck="assignScopeLoadingPrecheck"
      :show-read-back-warning="assignScopeShowReadBackWarning"
      @submit="handleAssignScopeSubmit"
    />
  </div>
</template>

<style scoped>
.rbac-roles {
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
.rbac-filters {
  display: flex;
  gap: 12px;
  margin-bottom: 16px;
  flex-wrap: wrap;
  align-items: center;
}
.rbac-filter-switch {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 13px;
  color: var(--el-text-color-regular);
}
.rbac-action-wrap {
  display: inline-block;
}
.rbac-pagination {
  margin-top: 16px;
  justify-content: flex-end;
}
</style>
