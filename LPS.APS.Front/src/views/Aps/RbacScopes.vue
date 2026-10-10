<!--
  APS V1 4号位 — 业务范围维护页（Pkg-8 RBAC 管理 UI 第二版）

  @see lps/LPS.APS.Web/Controllers/RbacController.cs（GET/POST/PUT/DELETE scopes）

  红线：
   - 后端 UpdateDataScopePolicyRequest 只有 Description → scopeType / scopeValue 创建后不可改
     （要改只能删除重建，且重建后 id 变化，原先挂在角色/用户上的分配会一并失效）
   - 删除前必须提示：该范围可能已被角色或用户引用，删除后对应授权静默失效
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
  ElTag
} from 'element-plus'
import {
  rbacApi,
  type CreateDataScopePolicyRequest,
  type DataScopePolicyDto,
  type ScopeType,
  SCOPE_TYPES,
  SCOPE_TYPE_LABELS
} from '@/api/aps-v1'

/* ==================== 状态 ==================== */

const loading = ref(false)
const scopes = ref<DataScopePolicyDto[]>([])

/** 分页（v1.0 契约） */
const pagination = reactive<{ page: number; pageSize: number; total: number }>({
  page: 1,
  pageSize: 20,
  total: 0
})

/** 服务端过滤：scopeType / keyword（v1.0 §三.4） */
const filters = reactive<{ scopeType: '' | ScopeType; keyword: string }>({
  scopeType: '',
  keyword: ''
})

/** 按范围类型统计（顶部概览；分页后仅显示当前页 type 分布） */
const typeCounts = computed<Array<{ type: ScopeType; count: number }>>(() => {
  const seen = new Set<ScopeType>()
  for (const s of scopes.value) seen.add(s.scopeType)
  return Array.from(seen)
    .map((t) => ({
      type: t,
      count: scopes.value.filter((s) => s.scopeType === t).length
    }))
    .sort((a, b) => SCOPE_TYPES.indexOf(a.type) - SCOPE_TYPES.indexOf(b.type))
})

/* ==================== 加载 ==================== */

async function loadScopes(): Promise<void> {
  loading.value = true
  try {
    const r = await rbacApi.listScopes({
      page: pagination.page,
      pageSize: pagination.pageSize,
      scopeType: filters.scopeType || undefined,
      keyword: filters.keyword || undefined
    })
    scopes.value = r.items
    pagination.total = r.total
  } finally {
    loading.value = false
  }
}

onMounted(loadScopes)

/* ==================== 分页 handlers ==================== */

function onPageChange(page: number): void {
  pagination.page = page
  loadScopes()
}

function onSizeChange(size: number): void {
  pagination.pageSize = size
  pagination.page = 1
  loadScopes()
}

watch(
  () => [filters.scopeType, filters.keyword],
  () => {
    pagination.page = 1
    loadScopes()
  }
)

/* ==================== 新建 Dialog ==================== */

const createVisible = ref(false)
const createSubmitting = ref(false)

const createForm = reactive<{ scopeType: ScopeType; scopeValue: string; description: string }>({
  scopeType: 'Factory',
  scopeValue: '',
  description: ''
})

const createFormRef = ref<InstanceType<typeof ElForm> | null>(null)

/** 同类型下 scopeValue 不可重复（前端先拦，后端 400 兜底） */
function validateScopeValueUnique(_rule: unknown, value: string, cb: (err?: Error) => void): void {
  const dup = scopes.value.some(
    (s) => s.scopeType === createForm.scopeType && s.scopeValue === value.trim()
  )
  if (dup) cb(new Error(`该类型下已存在范围值「${value}」`))
  else cb()
}

const createRules = {
  scopeType: [{ required: true, message: '请选择范围类型', trigger: 'change' }],
  scopeValue: [
    { required: true, message: '请输入范围值', trigger: 'blur' },
    { validator: validateScopeValueUnique, trigger: 'blur' }
  ]
}

function openCreate(): void {
  createForm.scopeType = 'Factory'
  createForm.scopeValue = ''
  createForm.description = ''
  createVisible.value = true
}

async function submitCreate(): Promise<void> {
  if (!createFormRef.value) return
  const valid = await createFormRef.value.validate().catch(() => false)
  if (!valid) return

  createSubmitting.value = true
  try {
    const payload: CreateDataScopePolicyRequest = {
      scopeType: createForm.scopeType,
      scopeValue: createForm.scopeValue.trim(),
      description: createForm.description || null
    }
    await rbacApi.createScope(payload)
    ElMessage.success(`业务范围「${payload.scopeValue}」创建成功`)
    createVisible.value = false
    await loadScopes()
  } catch (err) {
    console.error('submitCreate failed:', err)
  } finally {
    createSubmitting.value = false
  }
}

/* ==================== 编辑说明 Dialog（后端仅支持改 description） ==================== */

const editVisible = ref(false)
const editSubmitting = ref(false)
const editingRow = ref<DataScopePolicyDto | null>(null)
const editDescription = ref('')

function openEdit(row: DataScopePolicyDto): void {
  editingRow.value = row
  editDescription.value = row.description ?? ''
  editVisible.value = true
}

async function submitEdit(): Promise<void> {
  if (!editingRow.value) return
  editSubmitting.value = true
  try {
    await rbacApi.updateScope(editingRow.value.id, editDescription.value || null)
    ElMessage.success('说明已更新')
    editVisible.value = false
    await loadScopes()
  } catch (err) {
    console.error('submitEdit failed:', err)
  } finally {
    editSubmitting.value = false
  }
}

/* ==================== 删除 ==================== */

async function handleDelete(row: DataScopePolicyDto): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `确认删除业务范围「${SCOPE_TYPE_LABELS[row.scopeType]} / ${row.scopeValue}」？\n\n⚠️ 该范围可能已分配给角色或用户；删除后相关授权会静默失效，且系统未提供引用查询接口，无法事先确认影响面。`,
      '删除确认',
      { type: 'error', confirmButtonText: '确认删除', cancelButtonText: '取消' }
    )
  } catch {
    return
  }
  try {
    await rbacApi.deleteScope(row.id)
    ElMessage.success(`业务范围「${row.scopeValue}」已删除`)
    await loadScopes()
  } catch (err) {
    console.error('handleDelete failed:', err)
  }
}

/* ==================== 工具 ==================== */

const SCOPE_TAG_TYPE: Record<ScopeType, 'primary' | 'success' | 'warning' | 'info' | 'danger'> = {
  Factory: 'primary',
  ProductFamily: 'success',
  Department: 'warning',
  Domain: 'danger',
  ResourceOrgGroup: 'info',
  Global: 'info'
}
</script>

<template>
  <div class="rbac-scopes">
    <div class="rbac-toolbar">
      <h2 class="rbac-title">业务范围维护</h2>
      <ElButton type="primary" :disabled="loading" @click="openCreate"> 新建业务范围 </ElButton>
    </div>

    <div class="rbac-filters">
      <ElSelect v-model="filters.scopeType" placeholder="范围类型" clearable style="width: 180px">
        <ElOption label="全部类型" value="" />
        <ElOption v-for="t in SCOPE_TYPES" :key="t" :label="SCOPE_TYPE_LABELS[t]" :value="t" />
      </ElSelect>
      <ElInput
        v-model="filters.keyword"
        placeholder="范围值 / 说明 模糊匹配"
        clearable
        style="width: 240px"
      />
      <ElButton :loading="loading" @click="loadScopes">刷新</ElButton>
      <span class="rbac-counts">
        <ElTag v-for="tc in typeCounts" :key="tc.type" :type="SCOPE_TAG_TYPE[tc.type]" size="small">
          {{ SCOPE_TYPE_LABELS[tc.type] }} {{ tc.count }}
        </ElTag>
      </span>
    </div>

    <ElTable v-loading="loading" :data="scopes" border stripe>
      <ElTableColumn type="index" label="#" width="50" />
      <ElTableColumn label="范围类型" width="140">
        <template #default="{ row }: { row: DataScopePolicyDto }">
          <ElTag :type="SCOPE_TAG_TYPE[row.scopeType]">
            {{ SCOPE_TYPE_LABELS[row.scopeType] ?? row.scopeType }}
          </ElTag>
        </template>
      </ElTableColumn>
      <ElTableColumn prop="scopeValue" label="范围值" width="220" />
      <ElTableColumn label="说明" min-width="240">
        <template #default="{ row }: { row: DataScopePolicyDto }">
          {{ row.description || '—' }}
        </template>
      </ElTableColumn>
      <ElTableColumn label="创建时间" width="170">
        <template #default="{ row }: { row: DataScopePolicyDto }">
          {{ formatUtcDateTimeSec(row.createdAt) }}
        </template>
      </ElTableColumn>
      <ElTableColumn label="操作" width="180" fixed="right">
        <template #default="{ row }: { row: DataScopePolicyDto }">
          <ElButton size="small" @click="openEdit(row)">编辑说明</ElButton>
          <ElButton size="small" type="danger" @click="handleDelete(row)">删除</ElButton>
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
      title="新建业务范围"
      width="520"
      :close-on-click-modal="false"
      :close-on-press-escape="!createSubmitting"
    >
      <ElForm
        ref="createFormRef"
        :model="createForm"
        :rules="createRules"
        label-width="100"
        label-position="right"
      >
        <ElFormItem label="范围类型" prop="scopeType">
          <ElSelect v-model="createForm.scopeType" style="width: 100%">
            <ElOption
              v-for="t in SCOPE_TYPES"
              :key="t"
              :label="`${SCOPE_TYPE_LABELS[t]}（${t}）`"
              :value="t"
            />
          </ElSelect>
        </ElFormItem>
        <ElFormItem label="范围值" prop="scopeValue">
          <ElInput
            v-model="createForm.scopeValue"
            placeholder="如 BJ / INJECTION / FAMILY_INJECTION"
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

    <!-- 编辑说明 Dialog -->
    <ElDialog
      v-model="editVisible"
      title="编辑说明"
      width="520"
      :close-on-click-modal="false"
      :close-on-press-escape="!editSubmitting"
    >
      <div v-if="editingRow" class="rbac-edit-meta">
        {{ SCOPE_TYPE_LABELS[editingRow.scopeType] }} / <b>{{ editingRow.scopeValue }}</b>
        <span class="rbac-edit-meta-hint">（类型与范围值不可修改）</span>
      </div>
      <ElInput v-model="editDescription" type="textarea" :rows="3" placeholder="可选" />
      <template #footer>
        <ElButton :disabled="editSubmitting" @click="editVisible = false">取消</ElButton>
        <ElButton type="primary" :loading="editSubmitting" @click="submitEdit">保存</ElButton>
      </template>
    </ElDialog>
  </div>
</template>

<style scoped>
.rbac-scopes {
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
  margin-left: auto;
}
.rbac-edit-meta {
  margin-bottom: 12px;
  font-size: 13px;
  color: var(--el-text-color-regular);
}
.rbac-edit-meta-hint {
  color: var(--el-text-color-secondary);
}
.rbac-pagination {
  margin-top: 16px;
  justify-content: flex-end;
}
</style>
