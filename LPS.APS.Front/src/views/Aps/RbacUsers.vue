<!--
  APS V1 4号位 — 用户管理页（Pkg-8 RBAC 管理 UI）

  @see lps/LPS.APS.Web/Controllers/RbacController.cs（GET/POST/PUT/DELETE/PUT roles）
  @see frontNew/src/api/aps-v1/rbac.ts（17 端点 + 写端点 mock 拒绝）

  红线：
   - 自删保护：row.id === 当前登录 userId → 删除/停用按钮 disabled（与后端 403 双保险）
   - UpdateUserRequest 全量覆盖：编辑提交必须带全字段（含 email/phoneNumber 旧值或 null）
   - 密码：8-128 位 + ≠ userCode（与后端校验一致）
   - 分配角色（覆盖式 PUT）：默认全不勾选 → AssignDialog 顶部红 Alert + 二次确认
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
  ElTable,
  ElTableColumn,
  ElTag,
  ElTooltip
} from 'element-plus'
import {
  rbacApi,
  type CreateUserRequest,
  type UpdateUserRequest,
  type UserSummaryDto,
  type RoleSummaryDto,
  type DataScopePolicyDto,
  type UserStatus,
  USER_STATUS_LABELS,
  SCOPE_TYPE_LABELS
} from '@/api/aps-v1'
import { useApsAuthStore } from '@/store/modules/aps/auth'
import AssignDialog, { type AssignGroup } from './components/AssignDialog.vue'

const apsAuth = useApsAuthStore()

/* ==================== 状态 ==================== */

const loading = ref(false)
const users = ref<UserSummaryDto[]>([])
const roles = ref<RoleSummaryDto[]>([])
const scopes = ref<DataScopePolicyDto[]>([])

/** 分页（v1.0 契约：page 从 1 起，pageSize 默认 20，上限 200） */
const pagination = reactive<{ page: number; pageSize: number; total: number }>({
  page: 1,
  pageSize: 20,
  total: 0
})

/** 搜索 + 状态过滤（服务端分页 + 过滤；3号位 v1.0 §三.1） */
const filters = reactive<{ keyword: string; status: '' | UserStatus }>({
  keyword: '',
  status: ''
})

/* ==================== 加载 ==================== */

async function loadUsers(): Promise<void> {
  loading.value = true
  try {
    const u = await rbacApi.listUsers({
      page: pagination.page,
      pageSize: pagination.pageSize,
      keyword: filters.keyword || undefined,
      status: filters.status || undefined
    })
    users.value = u.items
    pagination.total = u.total
  } finally {
    loading.value = false
  }
}

async function loadAux(): Promise<void> {
  // 角色 + 范围列表是 AssignDialog 预勾选用，不需要分页；后端目前无分页上限
  const [r, s] = await Promise.all([rbacApi.listRoles({}), rbacApi.listScopes({})])
  roles.value = r.items
  scopes.value = s.items
}

async function loadAll(): Promise<void> {
  await Promise.all([loadUsers(), loadAux()])
}

onMounted(loadAll)

/* ==================== 分页 handlers ==================== */

function onPageChange(page: number): void {
  pagination.page = page
  loadUsers()
}

function onSizeChange(size: number): void {
  pagination.pageSize = size
  pagination.page = 1
  loadUsers()
}

// 关键词 / 状态变更时回到第一页（防「搜了关键词却停在第 5 页没结果」）
watch(
  () => [filters.keyword, filters.status],
  () => {
    pagination.page = 1
    loadUsers()
  }
)

/* ==================== 自删保护 ==================== */

const currentUserId = computed<number | null>(() => apsAuth.userInfo?.userId ?? null)
const isSelf = (row: UserSummaryDto): boolean => row.id === currentUserId.value

/* ==================== 新建 / 编辑 Dialog ==================== */

const editVisible = ref(false)
const editMode = ref<'create' | 'edit'>('create')
const editingId = ref<number | null>(null)
const editSubmitting = ref(false)

interface UserForm {
  userCode: string
  userName: string
  password: string
  confirmPassword: string
  email: string
  phoneNumber: string
}

const editForm = reactive<UserForm>({
  userCode: '',
  userName: '',
  password: '',
  confirmPassword: '',
  email: '',
  phoneNumber: ''
})

const editFormRef = ref<InstanceType<typeof ElForm> | null>(null)

function resetEditForm(): void {
  editForm.userCode = ''
  editForm.userName = ''
  editForm.password = ''
  editForm.confirmPassword = ''
  editForm.email = ''
  editForm.phoneNumber = ''
  editingId.value = null
}

function openCreate(): void {
  resetEditForm()
  editMode.value = 'create'
  editVisible.value = true
}

function openEdit(row: UserSummaryDto): void {
  resetEditForm()
  editMode.value = 'edit'
  editingId.value = row.id
  // UpdateUserRequest 全量覆盖：编辑必须把 email/phoneNumber 一并回填
  editForm.userCode = row.userCode
  editForm.userName = row.userName
  editForm.email = row.email ?? ''
  editForm.phoneNumber = row.phoneNumber ?? ''
  editVisible.value = true
}

/** 表单校验规则 */
function validatePasswordEqual(_rule: unknown, value: string, cb: (err?: Error) => void): void {
  if (editMode.value === 'create' && value !== editForm.password) {
    cb(new Error('两次输入的密码不一致'))
  } else {
    cb()
  }
}

function validatePasswordNotUserCode(
  _rule: unknown,
  value: string,
  cb: (err?: Error) => void
): void {
  if (editMode.value === 'create' && value && value === editForm.userCode) {
    cb(new Error('密码不能与登录账号相同'))
  } else {
    cb()
  }
}

const editRules = computed(() => ({
  userCode: [
    { required: true, message: '请输入登录账号', trigger: 'blur' },
    { min: 8, max: 128, message: '长度 8-128 位（与服务端一致）', trigger: 'blur' },
    {
      pattern: /^[a-zA-Z][a-zA-Z0-9._-]*$/,
      message: '字母开头，仅含字母/数字/点/下划线/横线（推荐 aps.<业务域>.<账号>）',
      trigger: 'blur'
    }
  ],
  userName: [{ required: true, message: '请输入用户姓名', trigger: 'blur' }],
  password:
    editMode.value === 'create'
      ? [
          { required: true, message: '请输入密码', trigger: 'blur' },
          { min: 8, max: 128, message: '长度 8-128 位', trigger: 'blur' },
          { validator: validatePasswordNotUserCode, trigger: 'blur' }
        ]
      : [{ min: 8, max: 128, message: '留空则不修改密码', trigger: 'blur' }],
  confirmPassword:
    editMode.value === 'create'
      ? [
          { required: true, message: '请再次输入密码', trigger: 'blur' },
          { validator: validatePasswordEqual, trigger: 'blur' }
        ]
      : []
}))

async function submitEdit(): Promise<void> {
  if (!editFormRef.value) return
  const valid = await editFormRef.value.validate().catch(() => false)
  if (!valid) return

  editSubmitting.value = true
  try {
    if (editMode.value === 'create') {
      const payload: CreateUserRequest = {
        userCode: editForm.userCode,
        userName: editForm.userName,
        password: editForm.password,
        email: editForm.email || null,
        phoneNumber: editForm.phoneNumber || null
      }
      await rbacApi.createUser(payload)
      ElMessage.success(`用户「${payload.userCode}」创建成功`)
    } else if (editingId.value !== null) {
      // 全量覆盖：email/phoneNumber 即使为空也要带 null（否则后端置空为 undefined 与 null 不一致）
      const payload: UpdateUserRequest = {
        userName: editForm.userName,
        status: 'Active',
        email: editForm.email || null,
        phoneNumber: editForm.phoneNumber || null
      }
      await rbacApi.updateUser(editingId.value, payload)
      ElMessage.success('用户已更新')
    }
    editVisible.value = false
    await loadAll()
  } catch (err) {
    // 错误已由 http.ts 的响应拦截器弹 ElMessage，此处不重复
    console.error('submitEdit failed:', err)
  } finally {
    editSubmitting.value = false
  }
}

/* ==================== 停用 / 删除 ==================== */

async function handleDeactivate(row: UserSummaryDto): Promise<void> {
  if (isSelf(row)) {
    ElMessage.warning('不能停用当前登录账号')
    return
  }
  try {
    await ElMessageBox.confirm(
      `确认停用用户「${row.userCode}」？停用后该用户无法登录（密码校验仍存在，但 IsEnabled=0）。`,
      '停用确认',
      { type: 'warning', confirmButtonText: '确认停用', cancelButtonText: '取消' }
    )
  } catch {
    return
  }
  try {
    await rbacApi.updateUser(row.id, {
      userName: row.userName,
      status: 'Disabled',
      email: row.email ?? null,
      phoneNumber: row.phoneNumber ?? null
    })
    ElMessage.success(`用户「${row.userCode}」已停用`)
    await loadUsers()
  } catch (err) {
    console.error('handleDeactivate failed:', err)
  }
}

async function handleDelete(row: UserSummaryDto): Promise<void> {
  if (isSelf(row)) {
    ElMessage.warning('不能删除当前登录账号')
    return
  }
  try {
    await ElMessageBox.confirm(
      `确认删除用户「${row.userCode}」？该操作为软删除（IsDeleted=1, IsEnabled=0）。\n\n⚠️ 若该账号是最后一名持有 aps.auth.user.edit 的管理员，删除后将无人可管理 RBAC。`,
      '删除确认',
      {
        type: 'error',
        confirmButtonText: '确认删除',
        cancelButtonText: '取消',
        dangerouslyUseHTMLString: false
      }
    )
  } catch {
    return
  }
  try {
    await rbacApi.deleteUser(row.id)
    ElMessage.success(`用户「${row.userCode}」已删除`)
    await loadAll()
  } catch (err) {
    console.error('handleDelete failed:', err)
  }
}

/* ==================== 分配角色 Dialog ==================== */

const assignVisible = ref(false)
const assignTarget = ref<UserSummaryDto | null>(null)
const assignSubmitting = ref(false)
const assignPreCheckedIds = ref<number[]>([])
const assignLoadingPrecheck = ref(false)
const assignShowReadBackWarning = ref(false)

const roleGroups = computed<AssignGroup[]>(() => {
  // 角色列表按 isSystemRole 分两组；可扩展按 module
  const systemRoles = roles.value.filter((r) => r.isSystemRole)
  const customRoles = roles.value.filter((r) => !r.isSystemRole)
  const groups: AssignGroup[] = []
  if (systemRoles.length) {
    groups.push({
      key: 'system',
      label: '系统角色',
      options: systemRoles.map((r) => ({ id: r.id, label: r.roleCode, sub: r.roleName }))
    })
  }
  if (customRoles.length) {
    groups.push({
      key: 'custom',
      label: '自定义角色',
      options: customRoles.map((r) => ({ id: r.id, label: r.roleCode, sub: r.roleName }))
    })
  }
  return groups
})

async function openAssign(row: UserSummaryDto): Promise<void> {
  assignTarget.value = row
  assignPreCheckedIds.value = []
  assignShowReadBackWarning.value = false
  assignLoadingPrecheck.value = true
  assignVisible.value = true
  try {
    const currentRoles = await rbacApi.getUserRoles(row.id)
    assignPreCheckedIds.value = currentRoles.map((r) => r.id)
  } catch (err) {
    console.error('openAssign: getUserRoles failed', err)
    assignPreCheckedIds.value = []
    assignShowReadBackWarning.value = true
  } finally {
    assignLoadingPrecheck.value = false
  }
}

async function handleAssignSubmit(ids: number[]): Promise<void> {
  if (!assignTarget.value) return
  assignSubmitting.value = true
  try {
    await rbacApi.assignUserRoles(assignTarget.value.id, ids)
    ElMessage.success(
      ids.length === 0
        ? `已清空用户「${assignTarget.value.userCode}」的全部角色`
        : `用户「${assignTarget.value.userCode}」角色已更新为 ${ids.length} 项`
    )
    assignVisible.value = false
  } catch (err) {
    console.error('handleAssignSubmit failed:', err)
  } finally {
    assignSubmitting.value = false
  }
}

/* ==================== 分配业务范围 Dialog（用户级，独立于角色继承） ==================== */

const assignScopeVisible = ref(false)
const assignScopeTarget = ref<UserSummaryDto | null>(null)
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

async function openAssignScope(row: UserSummaryDto): Promise<void> {
  assignScopeTarget.value = row
  assignScopePreCheckedIds.value = []
  assignScopeShowReadBackWarning.value = false
  assignScopeLoadingPrecheck.value = true
  assignScopeVisible.value = true
  try {
    const currentScopes = await rbacApi.getUserScopes(row.id)
    assignScopePreCheckedIds.value = currentScopes.map((s) => s.id)
  } catch (err) {
    console.error('openAssignScope: getUserScopes failed', err)
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
    await rbacApi.assignUserScopes(assignScopeTarget.value.id, ids)
    ElMessage.success(
      ids.length === 0
        ? `已清空用户「${assignScopeTarget.value.userCode}」的全部直授业务范围`
        : `用户「${assignScopeTarget.value.userCode}」直授业务范围已更新为 ${ids.length} 项`
    )
    assignScopeVisible.value = false
  } catch (err) {
    console.error('handleAssignScopeSubmit failed:', err)
  } finally {
    assignScopeSubmitting.value = false
  }
}

/* ==================== 工具 ==================== */

const statusTagType = (s: UserStatus): 'success' | 'warning' | 'info' => {
  if (s === 'Active') return 'success'
  if (s === 'Disabled') return 'warning'
  return 'info'
}
</script>

<template>
  <div class="rbac-users">
    <div class="rbac-toolbar">
      <h2 class="rbac-title">用户管理</h2>
      <ElButton type="primary" :disabled="loading" @click="openCreate"> 新建用户 </ElButton>
    </div>

    <div class="rbac-filters">
      <ElInput
        v-model="filters.keyword"
        placeholder="账号 / 姓名 / 邮箱 模糊匹配"
        clearable
        style="width: 260px"
      />
      <ElSelect v-model="filters.status" placeholder="状态" clearable style="width: 140px">
        <ElOption label="全部" value="" />
        <ElOption label="启用" value="Active" />
        <ElOption label="已停用" value="Disabled" />
        <ElOption label="已删除" value="Deleted" />
      </ElSelect>
      <ElButton @click="loadUsers" :loading="loading">刷新</ElButton>
    </div>

    <ElTable :data="users" v-loading="loading" border stripe>
      <ElTableColumn type="index" label="#" width="50" />
      <ElTableColumn prop="userCode" label="登录账号" width="160" />
      <ElTableColumn prop="userName" label="姓名" width="140" />
      <ElTableColumn prop="email" label="邮箱" min-width="200">
        <template #default="{ row }: { row: UserSummaryDto }">
          {{ row.email || '—' }}
        </template>
      </ElTableColumn>
      <ElTableColumn label="状态" width="100">
        <template #default="{ row }: { row: UserSummaryDto }">
          <ElTag :type="statusTagType(row.status)">
            {{ USER_STATUS_LABELS[row.status] }}
          </ElTag>
        </template>
      </ElTableColumn>
      <ElTableColumn label="最后登录" width="170">
        <template #default="{ row }: { row: UserSummaryDto }">
          {{ formatUtcDateTimeSec(row.lastLoginTime) }}
        </template>
      </ElTableColumn>
      <ElTableColumn label="创建时间" width="170">
        <template #default="{ row }: { row: UserSummaryDto }">
          {{ formatUtcDateTimeSec(row.createdAt) }}
        </template>
      </ElTableColumn>
      <ElTableColumn label="操作" width="400" fixed="right">
        <template #default="{ row }: { row: UserSummaryDto }">
          <ElButton size="small" @click="openEdit(row)">编辑</ElButton>
          <ElButton size="small" type="primary" @click="openAssign(row)"> 分配角色 </ElButton>
          <ElButton size="small" type="success" @click="openAssignScope(row)"> 分配范围 </ElButton>
          <ElTooltip v-if="isSelf(row)" content="不能停用当前登录账号" placement="top">
            <span class="rbac-action-wrap">
              <ElButton size="small" type="warning" disabled>停用</ElButton>
            </span>
          </ElTooltip>
          <ElButton v-else size="small" type="warning" @click="handleDeactivate(row)">
            停用
          </ElButton>
          <ElTooltip v-if="isSelf(row)" content="不能删除当前登录账号" placement="top">
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
      :title="editMode === 'create' ? '新建用户' : '编辑用户'"
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
        <ElFormItem label="登录账号" prop="userCode">
          <ElInput
            v-model="editForm.userCode"
            :disabled="editMode === 'edit'"
            placeholder="8-128 位（创建后不可改）"
          />
        </ElFormItem>
        <ElFormItem label="姓名" prop="userName">
          <ElInput v-model="editForm.userName" placeholder="用户姓名" />
        </ElFormItem>
        <ElFormItem v-if="editMode === 'create'" label="密码" prop="password">
          <ElInput
            v-model="editForm.password"
            type="password"
            show-password
            placeholder="8-128 位，且不能与登录账号相同"
          />
        </ElFormItem>
        <ElFormItem v-if="editMode === 'create'" label="确认密码" prop="confirmPassword">
          <ElInput
            v-model="editForm.confirmPassword"
            type="password"
            show-password
            placeholder="再次输入"
          />
        </ElFormItem>
        <ElFormItem label="邮箱">
          <ElInput v-model="editForm.email" placeholder="可选" />
        </ElFormItem>
        <ElFormItem label="手机号">
          <ElInput v-model="editForm.phoneNumber" placeholder="可选" />
        </ElFormItem>
      </ElForm>
      <template #footer>
        <ElButton :disabled="editSubmitting" @click="editVisible = false">取消</ElButton>
        <ElButton type="primary" :loading="editSubmitting" @click="submitEdit">
          {{ editMode === 'create' ? '创建' : '保存' }}
        </ElButton>
      </template>
    </ElDialog>

    <!-- 分配角色 Dialog -->
    <AssignDialog
      v-if="assignTarget"
      v-model="assignVisible"
      title="分配角色"
      dimension="角色"
      :target-label="`用户 ${assignTarget.userCode}`"
      :groups="roleGroups"
      :submitting="assignSubmitting"
      :pre-checked-ids="assignPreCheckedIds"
      :loading-precheck="assignLoadingPrecheck"
      :show-read-back-warning="assignShowReadBackWarning"
      @submit="handleAssignSubmit"
    />

    <!-- 分配业务范围 Dialog（用户级直授） -->
    <AssignDialog
      v-if="assignScopeTarget"
      v-model="assignScopeVisible"
      title="分配业务范围"
      dimension="直授业务范围"
      :target-label="`用户 ${assignScopeTarget.userCode}`"
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
.rbac-users {
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
}
.rbac-action-wrap {
  display: inline-block;
}
.rbac-pagination {
  margin-top: 16px;
  justify-content: flex-end;
}
</style>
