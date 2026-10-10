<!--
  APS V1 4号位 — 明确产品转换（EXACT）Setup 规则维护（v1.5 Setup 专项 §3，页面 2.1）

  职责：EXACT 规则 CRUD（7 元组：Department/Stage/Operation/Resource/FromMaterial/ToMaterial + RuleSetVersionId）
  核心流程（§3.3）：新增时选定 Operation + From/To Material → 调共同合法设备推荐端点 → 设备下拉只展示交集
  入口联动（§5.4）：从页面 2.3 "去补 EXACT" 跳转带入 query 预填 6 字段，自动打开新增 Dialog

  红线（§7.1 + §7.2）：
   - 仅 DRAFT 状态的 RuleSetVersion 可编辑（PUBLISHED/ACTIVE → 新增/编辑/删除按钮 disabled + Tooltip）
   - FromMaterial != ToMaterial（Dialog 内拦截）
   - SetupMinutes > 0（Dialog 内拦截）
   - 共同合法设备外不允许保存（Dialog 内拦截）
   - 同 7 元组重复 → 后端 422 原文透传

  权限：aps.setup.view（查看）+ aps.setup.edit（CRUD）

  架构：复用 StrategyProfiles.vue 五段式（加载 / 过滤 / 表格 / Dialog / 二次确认 / 红线注释）
-->

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { storeToRefs } from 'pinia'
import dayjs from 'dayjs'
import {
  ElAlert,
  ElButton,
  ElCard,
  ElCol,
  ElEmpty,
  ElMessage,
  ElMessageBox,
  ElOption,
  ElRow,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag,
  ElTooltip
} from 'element-plus'
import { useSetupExactStore } from '@/store/modules/aps/setupExact'
import { useApsAuthStore } from '@/store/modules/aps/auth'
import {
  SETUP_RULE_STATUS_META,
  SETUP_VERSION_STATUS_META,
  roleLabelOf,
  type SetupDialogPrefill,
  type SetupRuleDto
} from '@/api/aps-v1'
import SetupRedlineAlert from './components/SetupRedlineAlert.vue'
import SetupExactDialog from './components/SetupExactDialog.vue'

defineOptions({ name: 'ApsSetupExact' })

const route = useRoute()
const exactStore = useSetupExactStore()
const apsAuth = useApsAuthStore()

const {
  rules,
  selectedRuleSetVersionId,
  ruleSetVersions,
  listLoading,
  versionsLoading,
  actionRunning,
  error,
  currentRuleSetVersion,
  canEdit,
  canWrite,
  ruleCount
} = storeToRefs(exactStore)

// ===== 过滤（部门/大工艺 客户端过滤，规则量级 < 1000，§13 性能允许） =====
const filterDepartment = ref<string>('')
const filterStage = ref<string>('')

const filteredRules = computed<SetupRuleDto[]>(() => {
  return rules.value.filter((r) => {
    if (filterDepartment.value && r.departmentCode !== filterDepartment.value) return false
    if (filterStage.value && r.stageCode !== filterStage.value) return false
    return true
  })
})

// ===== Dialog 状态 =====
const dialogVisible = ref(false)
const dialogMode = ref<'create' | 'edit'>('create')
const editingRule = ref<SetupRuleDto | null>(null)
const dialogPrefill = ref<SetupDialogPrefill | null>(null)

// ===== 加载 =====
async function loadAll() {
  await exactStore.loadRuleSetVersions()
  // 默认选中第一个 DRAFT 版本（无 DRAFT 时选第一个）
  const draft =
    ruleSetVersions.value.find((v) => v.status === 'DRAFT') ?? ruleSetVersions.value[0] ?? null
  if (draft) {
    exactStore.selectRuleSetVersion(draft.id)
    await exactStore.loadRules(draft.id)
  }
}

onMounted(async () => {
  await loadAll()
  // 页面 2.3 "去补 EXACT" 跳转预填（§5.4：6 字段）
  const q = route.query
  if (q.fromMaterialCode && q.toMaterialCode && canWrite.value && canEdit.value) {
    dialogPrefill.value = {
      departmentCode: String(q.departmentCode ?? ''),
      stageCode: String(q.stageCode ?? ''),
      operationCode: String(q.operationCode ?? ''),
      resourceCode: String(q.resourceCode ?? ''),
      fromMaterialCode: String(q.fromMaterialCode),
      toMaterialCode: String(q.toMaterialCode)
    }
    openCreateDialog()
  }
})

async function handleVersionChange(versionId: number | null) {
  exactStore.selectRuleSetVersion(versionId)
  if (versionId !== null) {
    await exactStore.loadRules(versionId)
  }
}

// ===== CRUD 操作（§7.2：仅 DRAFT 可编辑，按钮 disabled + store 双保险） =====
function openCreateDialog() {
  if (!canWrite.value) {
    ElMessage.warning('当前操作者无编辑权限，请联系系统管理员授予')
    return
  }
  if (!canEdit.value) {
    ElMessage.warning('只能编辑草稿（DRAFT）状态的版本')
    return
  }
  dialogMode.value = 'create'
  editingRule.value = null
  dialogVisible.value = true
}

function openEditDialog(rule: SetupRuleDto) {
  if (!canWrite.value || !canEdit.value) return
  dialogMode.value = 'edit'
  editingRule.value = rule
  dialogPrefill.value = null
  dialogVisible.value = true
}

async function handleDelete(rule: SetupRuleDto) {
  if (!canWrite.value || !canEdit.value) return
  let confirmed = false
  try {
    await ElMessageBox.confirm(
      `即将删除明确产品转换规则（EXACT）：${rule.departmentCode} / ${rule.stageCode} / ${rule.operationCode} / ${rule.resourceCode} / ${rule.fromMaterialCode} → ${rule.toMaterialCode}（${rule.setupMinutes} 分钟）。删除后需重新发布版本才生效。是否继续？`,
      '删除确认',
      {
        type: 'warning',
        confirmButtonText: '确认删除',
        cancelButtonText: '取消',
        confirmButtonClass: 'el-button--danger'
      }
    )
    confirmed = true
  } catch {
    confirmed = false
  }
  if (!confirmed) return

  const ok = await exactStore.deleteRule(rule.id)
  if (ok) {
    ElMessage.success('明确产品转换规则（EXACT）删除成功')
  } else if (exactStore.error) {
    ElMessage.error(exactStore.error)
  }
}

function handleSaved() {
  dialogPrefill.value = null
}

// ===== 格式化 =====
function formatDateTime(iso?: string | null) {
  return iso ? dayjs(iso).format('YYYY-MM-DD HH:mm:ss') : '—'
}

function getStatusTag(status: string) {
  // 版本选择器传治理六态、规则行传派生三态；两表 DRAFT 同义，其余键不重叠，合并回退安全
  const meta =
    SETUP_RULE_STATUS_META[status as keyof typeof SETUP_RULE_STATUS_META] ??
    SETUP_VERSION_STATUS_META[status as keyof typeof SETUP_VERSION_STATUS_META]
  return meta ?? { label: status, tag: 'info' as const }
}

/** 编辑按钮不可用原因（Tooltip 文案） */
const editDisabledReason = computed<string>(() => {
  if (!canWrite.value) return '当前操作者无编辑权限，请联系系统管理员授予'
  if (!canEdit.value) return '只能编辑草稿（DRAFT）状态的版本'
  return ''
})
</script>

<template>
  <div class="setup-exact-page">
    <!-- 页面头部 -->
    <div class="page-header">
      <div class="header-left">
        <h2>明确产品转换规则（EXACT）</h2>
        <p class="subtitle">按工序维护"前产品 → 后产品"的换型分钟数，并推荐共同合法设备</p>
      </div>
      <div class="header-right">
        <ElTooltip
          :content="`当前操作者：${apsAuth.userInfo?.userCode ?? '—'}（${apsAuth.roles.map(roleLabelOf).join('、')}）`"
        >
          <ElTag type="info" size="small">{{ apsAuth.userInfo?.userCode ?? '—' }}</ElTag>
        </ElTooltip>
      </div>
    </div>

    <!-- 红线 Alert（§7.1 + §7.2） -->
    <SetupRedlineAlert page-type="exact" />

    <!-- 错误提示 -->
    <ElAlert
      v-if="error"
      type="error"
      :title="error"
      :closable="false"
      show-icon
      class="error-alert"
    />

    <!-- 版本选择 + 过滤 -->
    <ElCard class="selector-card" shadow="never">
      <ElRow :gutter="16" align="middle">
        <ElCol :span="8">
          <div class="selector-label">规则集版本（仅草稿可编辑）</div>
          <ElSelect
            :model-value="selectedRuleSetVersionId ?? undefined"
            placeholder="选择规则集版本"
            :loading="versionsLoading"
            style="width: 100%"
            @change="handleVersionChange"
          >
            <ElOption
              v-for="v in ruleSetVersions"
              :key="v.id"
              :label="`${v.versionCode}（${getStatusTag(v.status).label}）`"
              :value="v.id"
            >
              <span>{{ v.versionCode }}</span>
              <ElTag :type="getStatusTag(v.status).tag" size="small" style="margin-left: 8px">
                {{ getStatusTag(v.status).label }}
              </ElTag>
            </ElOption>
          </ElSelect>
        </ElCol>
        <ElCol :span="5">
          <div class="selector-label">部门筛选</div>
          <ElSelect v-model="filterDepartment" placeholder="全部" clearable style="width: 100%">
            <ElOption label="DEPT_A（注塑车间）" value="DEPT_A" />
            <ElOption label="DEPT_B（装配车间）" value="DEPT_B" />
          </ElSelect>
        </ElCol>
        <ElCol :span="5">
          <div class="selector-label">大工艺筛选</div>
          <ElSelect v-model="filterStage" placeholder="全部" clearable style="width: 100%">
            <ElOption label="STAGE_1（注塑工艺）" value="STAGE_1" />
            <ElOption label="STAGE_2（装配工艺）" value="STAGE_2" />
          </ElSelect>
        </ElCol>
        <ElCol :span="6">
          <div class="selector-label">&nbsp;</div>
          <ElTooltip :content="editDisabledReason" :disabled="!editDisabledReason" placement="top">
            <span>
              <ElButton type="primary" :disabled="!!editDisabledReason" @click="openCreateDialog">
                <Icon icon="vi-ep:plus" class="mr-4" />
                新增明确产品转换规则（EXACT）
              </ElButton>
            </span>
          </ElTooltip>
        </ElCol>
      </ElRow>
      <div v-if="currentRuleSetVersion && !canEdit" class="version-hint">
        当前版本 {{ currentRuleSetVersion.versionCode }} 状态为
        {{ getStatusTag(currentRuleSetVersion.status).label }}，不可编辑（仅草稿可编辑）
      </div>
    </ElCard>

    <!-- 规则表格 -->
    <ElCard class="table-card" shadow="never">
      <template #header>
        <div class="card-header">
          <span>
            明确产品转换规则列表
            <ElTag size="small" style="margin-left: 8px">{{ ruleCount }} 条</ElTag>
            <ElTag
              v-if="filteredRules.length !== ruleCount"
              size="small"
              type="info"
              style="margin-left: 4px"
            >
              过滤后 {{ filteredRules.length }} 条
            </ElTag>
          </span>
        </div>
      </template>

      <ElTable
        v-loading="listLoading || actionRunning"
        :data="filteredRules"
        border
        stripe
        empty-text="该版本下暂无明确产品转换规则（可在「未维护查询」中筛选后补录）"
      >
        <ElTableColumn type="index" label="#" width="50" />
        <ElTableColumn prop="departmentCode" label="部门" width="100" />
        <ElTableColumn prop="stageCode" label="大工艺" width="100" />
        <ElTableColumn prop="operationCode" label="当前工序" width="100" />
        <ElTableColumn prop="resourceCode" label="设备" width="100" />
        <ElTableColumn prop="fromMaterialCode" label="前产品" width="120" />
        <ElTableColumn prop="toMaterialCode" label="后产品" width="120" />
        <ElTableColumn prop="setupMinutes" label="换型分钟" width="100" align="right">
          <template #default="{ row }">
            <ElTag
              :type="
                row.setupMinutes > 60 ? 'danger' : row.setupMinutes > 30 ? 'warning' : 'success'
              "
              size="small"
            >
              {{ row.setupMinutes }} 分钟
            </ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn prop="status" label="状态" width="90">
          <template #default="{ row }">
            <ElTag :type="getStatusTag(row.status).tag" size="small">
              {{ getStatusTag(row.status).label }}
            </ElTag>
          </template>
        </ElTableColumn>
        <!-- 09-20 交付回执偏差 #1：remark 字段撤除（备注列移除） -->
        <ElTableColumn prop="updatedAt" label="更新时间" width="170">
          <template #default="{ row }">
            {{ formatDateTime(row.updatedAt ?? row.createdAt) }}
          </template>
        </ElTableColumn>
        <ElTableColumn label="操作" width="140" fixed="right">
          <template #default="{ row }">
            <ElTooltip
              :content="editDisabledReason"
              :disabled="!editDisabledReason"
              placement="top"
            >
              <span>
                <ElButton
                  type="primary"
                  size="small"
                  link
                  :disabled="!!editDisabledReason"
                  @click="openEditDialog(row)"
                >
                  编辑
                </ElButton>
                <ElButton
                  type="danger"
                  size="small"
                  link
                  :disabled="!!editDisabledReason"
                  @click="handleDelete(row)"
                >
                  删除
                </ElButton>
              </span>
            </ElTooltip>
          </template>
        </ElTableColumn>
      </ElTable>

      <ElEmpty
        v-if="!listLoading && selectedRuleSetVersionId === null"
        description="请先选择规则集版本"
      />
    </ElCard>

    <!-- 创建/编辑 Dialog -->
    <SetupExactDialog
      v-model="dialogVisible"
      :mode="dialogMode"
      :rule="editingRule"
      :prefill="dialogPrefill"
      @saved="handleSaved"
    />
  </div>
</template>

<style scoped>
.setup-exact-page {
  padding: 16px;
}

.page-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: 16px;
}

.header-left h2 {
  margin: 0 0 4px 0;
  font-size: 20px;
  font-weight: 600;
  color: var(--el-text-color-primary);
}

.header-left .subtitle {
  margin: 0;
  font-size: 13px;
  color: var(--el-text-color-secondary);
}

.error-alert {
  margin-bottom: 16px;
}

.selector-card {
  margin-bottom: 16px;
}

.selector-label {
  font-size: 13px;
  color: var(--el-text-color-secondary);
  margin-bottom: 8px;
}

.version-hint {
  margin-top: 12px;
  font-size: 12px;
  color: var(--el-color-warning);
}

.table-card {
  margin-bottom: 16px;
}

.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.mr-4 {
  margin-right: 4px;
}
</style>
