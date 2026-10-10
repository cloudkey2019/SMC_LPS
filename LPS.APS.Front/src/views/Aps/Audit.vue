<!--
  APS V1 4号位 — 审计日志查询页（页面 12 §22.6 Audit / §二十四 人工操作审计 / 验收场景 U42）

  @see lps/LPS.APS.Web/Controllers/RbacController.cs L170-181（GET /api/rbac/audit-logs，Policy=aps.audit.view）
  @see frontNew/src/api/aps-v1/types/audit.ts（字段级契约源）
  @see frontNew/docs/rbac.md（路由矩阵 22 路由 × 4 角色）

  契约要点（实现受限于后端契约，非前端偷懒）：
   - 响应 data 为**纯数组无 total**（QueryPagedAsync 无 out 总数）→ 分页用「上一页/下一页 + hasMore」形态，
     hasMore = 本页返回条数 === size（后端 OccurredAt 倒序、size clamp 1..200）
   - 过滤参数仅 userId / action / from / to 四个，action 为**精确等值匹配**；
     **不支持 result 过滤** → 页面只做 Result 列展示，不做筛选（避免客户端假过滤误导）
   - ⚠️ 后端 AuthService 不写 Login/Logout 审计（§22.6 要求覆盖）→ 3号位 沟通项，落地后
     types/audit.ts AUDIT_ACTION_CODES 追加即可，本页无需改动

  红线（U42 / §23.3 / §二十三）：
   - U42 脱敏：oldValue/newValue/requestData/responseData 原文展示；后端现有写入点已确认结构上
     不涉密（无 password/Token 序列化路径）；验收时人工抽查详情 JSON 无密码/Token 明文。
     后端新增写入点时由 3号位 保证不整对象序列化含密 DTO（§22.6 末行）
   - §23.3 后端是最终安全边界：本页只读、无任何写操作；无权限用户由路由守卫拦截（aps.audit.view），
     绕过页面直接构造 HTTP 仍会被后端 403（U38）
   - 页面不脱敏、不裁剪、不改写审计原文——审计记录只读呈现，任何"美化"都会破坏可追溯性
-->
<script setup lang="tsx">
import { computed, onMounted, reactive, ref } from 'vue'
import { formatUtcDateTimeSec } from '@/utils/datetime'
import {
  ElAlert,
  ElButton,
  ElDatePicker,
  ElDescriptions,
  ElDescriptionsItem,
  ElDrawer,
  ElMessage,
  ElOption,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus'
import {
  auditApi,
  rbacApi,
  AUDIT_ACTION_CODES,
  AUDIT_ACTION_LABELS,
  AUDIT_ENTITY_TYPE_LABELS,
  AUDIT_RESULT_LABELS,
  type AuditLogDto,
  type AuditLogQuery,
  type UserSummaryDto
} from '@/api/aps-v1'

/* ==================== 状态 ==================== */

const loading = ref(false)
const rows = ref<AuditLogDto[]>([])

/** 分页（无 total 契约）：page 从 1 起；hasMore = 本页条数 === size */
const page = ref(1)
const size = ref(20)
const hasMore = computed<boolean>(() => rows.value.length === size.value)

const filters = reactive<{
  /** Actor（后端只支持 userId 精确过滤；下拉展示 userCode，值传 userId）
   *  - 用 undefined 不用 null：ElSelect modelValue 类型不含 null（与 Order.vue dueDateRange 同法） */
  userId: number | undefined
  /** 动作码（后端精确等值匹配） */
  action: string
  /** 时间范围（含两端）→ from/to ISO；清空时 ElDatePicker 抛 null，经 onDateRangeChange 归一为 undefined */
  dateRange: [Date, Date] | undefined
}>({
  userId: undefined,
  action: '',
  dateRange: undefined
})

/** Actor 下拉数据源（复用 rbacApi.listUsers，真实/mock 双模均有） */
const users = ref<UserSummaryDto[]>([])
const usersLoadFailed = ref(false)

/* ==================== 加载 ==================== */

async function loadUsers(): Promise<void> {
  try {
    const r = await rbacApi.listUsers({ pageSize: 200 })
    users.value = r.items
  } catch {
    // 降级：Actor 筛选禁用 + warning，不阻塞主查询（审计列表本身不依赖用户列表）
    usersLoadFailed.value = true
    ElMessage.warning('用户列表加载失败，Actor 筛选暂不可用（不影响审计查询）')
  }
}

async function loadLogs(): Promise<void> {
  loading.value = true
  try {
    const query: AuditLogQuery = {
      page: page.value,
      size: size.value
    }
    if (filters.userId != null) query.userId = filters.userId
    if (filters.action) query.action = filters.action
    if (filters.dateRange?.[0]) query.from = filters.dateRange[0].toISOString()
    if (filters.dateRange?.[1]) query.to = filters.dateRange[1].toISOString()
    rows.value = await auditApi.listAuditLogs(query)
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  loadUsers()
  loadLogs()
})

/* ==================== 交互 ==================== */

function onSearch(): void {
  page.value = 1
  loadLogs()
}

/** ElSelect 清空时可能抛 ''/null（版本行为差异）→ 统一归一为 undefined（Order.vue onDomainChange 同法） */
function onUserIdChange(v: unknown): void {
  filters.userId = v == null || v === '' ? undefined : Number(v)
}

/** ElDatePicker 清空时抛 null → 归一为 undefined（Order.vue onDueRangeChange 同法） */
function onDateRangeChange(v: [Date, Date] | null | undefined): void {
  filters.dateRange = v ?? undefined
}

function onReset(): void {
  filters.userId = undefined
  filters.action = ''
  filters.dateRange = undefined
  page.value = 1
  loadLogs()
}

function onPrevPage(): void {
  if (page.value <= 1 || loading.value) return
  page.value -= 1
  loadLogs()
}

function onNextPage(): void {
  if (!hasMore.value || loading.value) return
  page.value += 1
  loadLogs()
}

function onSizeChange(): void {
  page.value = 1
  loadLogs()
}

/* ==================== 详情抽屉 ==================== */

const drawerVisible = ref(false)
const currentRow = ref<AuditLogDto | null>(null)

function openDetail(row: AuditLogDto): void {
  currentRow.value = row
  drawerVisible.value = true
}

/* ==================== 工具 ==================== */

/** Actor 展示：userCode 首选，空时回退 #userId，都空显示 系统 */
function actorLabel(row: AuditLogDto): string {
  if (row.userCode) return row.userCode
  if (row.userId != null) return `#${row.userId}`
  return '系统'
}

function actionLabel(code: string): string {
  return AUDIT_ACTION_LABELS[code] ?? code
}

function resultLabel(result: string): string {
  return AUDIT_RESULT_LABELS[result] ?? result
}

function entityTypeLabel(entityType: string | null | undefined): string {
  if (!entityType) return '—'
  return AUDIT_ENTITY_TYPE_LABELS[entityType] ?? entityType
}

const ACTION_TAG_TYPE = (code: string): 'success' | 'warning' | 'danger' | 'info' | 'primary' => {
  if (['Publish', 'Delete', 'Disable', 'ActivateCandidate', 'RecoverFailedRun'].includes(code)) {
    return 'danger'
  }
  if (
    ['Create', 'Update', 'Enable', 'AssignRoles', 'AssignPermissions', 'AssignScopes'].includes(
      code
    )
  ) {
    return 'warning'
  }
  return 'success'
}

const RESULT_TAG_TYPE = (result: string): 'success' | 'danger' =>
  result === 'Success' ? 'success' : 'danger'

/** oldValue/newValue 展示：JSON 文本尝试美化，失败原样；空值显示占位 */
function prettyValue(v: string | null | undefined): string {
  if (v == null || v === '') return ''
  try {
    return JSON.stringify(JSON.parse(v), null, 2)
  } catch {
    return v
  }
}
</script>

<template>
  <div class="audit-page">
    <div class="audit-toolbar">
      <h2 class="audit-title">审计日志</h2>
      <ElButton :loading="loading" @click="loadLogs">刷新</ElButton>
    </div>

    <ElAlert type="info" :closable="false" show-icon class="audit-hint">
      <template #title>
        回答「谁在什么时候，对什么对象，做了什么，结果如何？」。 记录只读呈现、原文展示（不展示密码
        / Token 等敏感内容）。 登录 / 登出动作当前未纳入审计，故动作筛选中无此二项。
      </template>
    </ElAlert>

    <!-- ===== 筛选栏（4 参数与后端契约一一对应；无 Result 筛选=契约不支持） ===== -->
    <div class="audit-filters">
      <ElSelect
        v-model="filters.userId"
        :placeholder="usersLoadFailed ? '用户列表不可用' : '操作人'"
        :disabled="usersLoadFailed"
        clearable
        filterable
        style="width: 200px"
        @change="onUserIdChange"
      >
        <ElOption
          v-for="u in users"
          :key="u.id"
          :label="`${u.userCode}（${u.userName}）`"
          :value="u.id"
        />
      </ElSelect>
      <ElSelect
        v-model="filters.action"
        placeholder="动作（精确匹配）"
        clearable
        style="width: 200px"
      >
        <ElOption
          v-for="code in AUDIT_ACTION_CODES"
          :key="code"
          :label="`${AUDIT_ACTION_LABELS[code]}（${code}）`"
          :value="code"
        />
      </ElSelect>
      <ElDatePicker
        v-model="filters.dateRange"
        type="datetimerange"
        start-placeholder="开始时间（含）"
        end-placeholder="截止时间（含）"
        style="width: 360px"
        @change="onDateRangeChange"
      />
      <ElButton type="primary" :loading="loading" @click="onSearch">查询</ElButton>
      <ElButton :disabled="loading" @click="onReset">重置</ElButton>
    </div>

    <!-- ===== 表格（§二十四 7 项：Actor/Time/ObjectType/ObjectId/Action/Result/Remark 全覆盖） ===== -->
    <ElTable
      v-loading="loading"
      :data="rows"
      border
      stripe
      class="audit-table"
      @row-click="openDetail"
    >
      <ElTableColumn label="时间" width="170">
        <template #default="{ row }: { row: AuditLogDto }">
          {{ formatUtcDateTimeSec(row.occurredAt) }}
        </template>
      </ElTableColumn>
      <ElTableColumn label="操作人" width="120">
        <template #default="{ row }: { row: AuditLogDto }">
          {{ actorLabel(row) }}
        </template>
      </ElTableColumn>
      <ElTableColumn label="模块" width="110">
        <template #default="{ row }: { row: AuditLogDto }">
          {{ row.module || '—' }}
        </template>
      </ElTableColumn>
      <ElTableColumn label="动作" width="170">
        <template #default="{ row }: { row: AuditLogDto }">
          <ElTag :type="ACTION_TAG_TYPE(row.actionCode)" size="small">
            {{ actionLabel(row.actionCode) }}
          </ElTag>
          <code class="audit-code">{{ row.actionCode }}</code>
        </template>
      </ElTableColumn>
      <ElTableColumn label="对象类型" width="170">
        <template #default="{ row }: { row: AuditLogDto }">
          {{ entityTypeLabel(row.entityType) }}
        </template>
      </ElTableColumn>
      <ElTableColumn label="对象编号" width="110">
        <template #default="{ row }: { row: AuditLogDto }">
          {{ row.entityId || '—' }}
        </template>
      </ElTableColumn>
      <ElTableColumn label="结果" width="90">
        <template #default="{ row }: { row: AuditLogDto }">
          <ElTag :type="RESULT_TAG_TYPE(row.result)" size="small">
            {{ resultLabel(row.result) }}
          </ElTag>
        </template>
      </ElTableColumn>
      <ElTableColumn label="备注" min-width="200">
        <template #default="{ row }: { row: AuditLogDto }">
          {{ row.remark || '—' }}
        </template>
      </ElTableColumn>
      <ElTableColumn label="操作" width="80" fixed="right">
        <template #default="{ row }: { row: AuditLogDto }">
          <ElButton link type="primary" size="small" @click.stop="openDetail(row)"> 详情 </ElButton>
        </template>
      </ElTableColumn>
    </ElTable>

    <!-- ===== 分页（后端无 total：上一页/下一页 + hasMore；注释见契约要点） ===== -->
    <div class="audit-pagination">
      <ElButton :disabled="page <= 1 || loading" @click="onPrevPage">上一页</ElButton>
      <span class="audit-page-info">第 {{ page }} 页 · 本页 {{ rows.length }} 条</span>
      <ElButton :disabled="!hasMore || loading" @click="onNextPage">下一页</ElButton>
      <ElSelect v-model="size" style="width: 110px" @change="onSizeChange">
        <ElOption :value="10" label="10 条/页" />
        <ElOption :value="20" label="20 条/页" />
        <ElOption :value="50" label="50 条/页" />
      </ElSelect>
      <span class="audit-page-hint">（后端按发生时间倒序返回，不提供总数）</span>
    </div>

    <!-- ===== 详情抽屉（全 21 字段分组展示，审计原文不裁剪） ===== -->
    <ElDrawer v-model="drawerVisible" title="审计详情" size="640px">
      <template v-if="currentRow">
        <ElDescriptions :column="2" border size="small" class="audit-detail-block">
          <ElDescriptionsItem label="审计编号">{{ currentRow.id }}</ElDescriptionsItem>
          <ElDescriptionsItem label="时间">
            {{ formatUtcDateTimeSec(currentRow.occurredAt) }}
          </ElDescriptionsItem>
          <ElDescriptionsItem label="操作人">
            {{ actorLabel(currentRow) }}
            <span v-if="currentRow.userCode && currentRow.userId != null" class="audit-muted">
              （#{{ currentRow.userId }}）
            </span>
          </ElDescriptionsItem>
          <ElDescriptionsItem label="模块">{{ currentRow.module || '—' }}</ElDescriptionsItem>
          <ElDescriptionsItem label="动作">
            <ElTag :type="ACTION_TAG_TYPE(currentRow.actionCode)" size="small">
              {{ actionLabel(currentRow.actionCode) }}
            </ElTag>
            <code class="audit-code">{{ currentRow.actionCode }}</code>
          </ElDescriptionsItem>
          <ElDescriptionsItem label="结果">
            <ElTag :type="RESULT_TAG_TYPE(currentRow.result)" size="small">
              {{ resultLabel(currentRow.result) }}
            </ElTag>
            <code class="audit-code">{{ currentRow.result }}</code>
          </ElDescriptionsItem>
          <ElDescriptionsItem label="对象类型">
            {{ entityTypeLabel(currentRow.entityType) }}
          </ElDescriptionsItem>
          <ElDescriptionsItem label="对象编号">
            {{ currentRow.entityId || '—' }}
          </ElDescriptionsItem>
          <ElDescriptionsItem label="版本码">
            {{ currentRow.versionCode || '—' }}
          </ElDescriptionsItem>
          <ElDescriptionsItem label="计划版本 ID">
            {{ currentRow.planVersionId ?? '—' }}
          </ElDescriptionsItem>
          <ElDescriptionsItem label="批次号">{{ currentRow.batchNo || '—' }}</ElDescriptionsItem>
          <ElDescriptionsItem label="审批编号">
            {{ currentRow.approvalId ?? '—' }}
          </ElDescriptionsItem>
          <ElDescriptionsItem label="备注" :span="2">
            {{ currentRow.remark || '—' }}
          </ElDescriptionsItem>
        </ElDescriptions>

        <h4 class="audit-detail-title">变更对比（审计原文，未脱敏未裁剪）</h4>
        <div class="audit-diff">
          <div class="audit-diff-col">
            <div class="audit-diff-label">变更前</div>
            <pre v-if="prettyValue(currentRow.oldValue)" class="audit-pre">{{
              prettyValue(currentRow.oldValue)
            }}</pre>
            <div v-else class="audit-muted">未记录</div>
          </div>
          <div class="audit-diff-col">
            <div class="audit-diff-label">变更后</div>
            <pre v-if="prettyValue(currentRow.newValue)" class="audit-pre">{{
              prettyValue(currentRow.newValue)
            }}</pre>
            <div v-else class="audit-muted">未记录</div>
          </div>
        </div>

        <h4 class="audit-detail-title">请求上下文</h4>
        <ElDescriptions :column="1" border size="small" class="audit-detail-block">
          <ElDescriptionsItem label="客户端 IP 地址">
            {{ currentRow.clientIp || '—' }}
          </ElDescriptionsItem>
          <ElDescriptionsItem label="客户端标识">
            {{ currentRow.userAgent || '—' }}
          </ElDescriptionsItem>
          <ElDescriptionsItem v-if="currentRow.errorMessage" label="错误消息">
            <span class="audit-error">{{ currentRow.errorMessage }}</span>
          </ElDescriptionsItem>
        </ElDescriptions>

        <!-- requestData/responseData 仅非空时展示（写入方选择性记录） -->
        <template v-if="currentRow.requestData || currentRow.responseData">
          <h4 class="audit-detail-title">请求 / 响应原文</h4>
          <pre v-if="currentRow.requestData" class="audit-pre">{{
            prettyValue(currentRow.requestData)
          }}</pre>
          <pre v-if="currentRow.responseData" class="audit-pre">{{
            prettyValue(currentRow.responseData)
          }}</pre>
        </template>
      </template>
    </ElDrawer>
  </div>
</template>

<style scoped>
.audit-page {
  padding: 16px;
}
.audit-toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
}
.audit-title {
  margin: 0;
  font-size: 18px;
  font-weight: 600;
}
.audit-hint {
  margin-bottom: 16px;
}
.audit-filters {
  display: flex;
  gap: 12px;
  margin-bottom: 16px;
  flex-wrap: wrap;
  align-items: center;
}
.audit-table {
  cursor: pointer;
}
.audit-pagination {
  display: flex;
  gap: 12px;
  align-items: center;
  margin-top: 16px;
}
.audit-page-info {
  font-size: 13px;
  color: var(--el-text-color-regular);
}
.audit-page-hint {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
.audit-code {
  margin-left: 6px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
  font-family: var(--el-font-family-mono, monospace);
}
.audit-detail-block {
  margin-bottom: 8px;
}
.audit-detail-title {
  margin: 16px 0 8px;
  font-size: 14px;
  font-weight: 600;
  color: var(--el-text-color-primary);
}
.audit-diff {
  display: flex;
  gap: 12px;
}
.audit-diff-col {
  flex: 1;
  min-width: 0;
}
.audit-diff-label {
  font-size: 12px;
  color: var(--el-text-color-secondary);
  margin-bottom: 4px;
}
.audit-pre {
  margin: 0;
  padding: 8px;
  background-color: var(--el-fill-color-light);
  border-radius: 4px;
  font-size: 12px;
  font-family: var(--el-font-family-mono, monospace);
  white-space: pre-wrap;
  word-break: break-all;
  max-height: 240px;
  overflow: auto;
}
.audit-muted {
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.audit-error {
  color: var(--el-color-danger);
}
</style>
