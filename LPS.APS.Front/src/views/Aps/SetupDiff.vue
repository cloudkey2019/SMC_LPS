<!--
  APS V1 4号位 — Setup 规则版本 Diff（v1.5 Setup 专项 §6，页面 2.4）

  职责：对比两个 RuleSetVersion 的 SetupRule 差异（EXACT/DEFAULT 规则的 added/modified/removed）
  复用：RuleSetVersion Diff 机制（v1.4 §十八），扩展 Setup 维度（§6.3 setupRuleChanges）

  列表字段（§6.2）：RuleSetVersionId/OtherVersionId/AddedCount/ModifiedCount/RemovedCount/PublishStatus
  Diff 操作（§6.3）：选定两个 RuleSetVersion → 调 GET /rule-set-versions/{id}/diff?otherVersionId=...
  红线（§6.4）：不展示非 Setup 维度的 Diff / 不允许跨 RuleSet 直接编辑

  权限：aps.setup.view（查看 Diff）+ aps.setup.publish（发布）

  架构：复用 StrategyProfiles.vue 五段式（加载 / 过滤 / 表格 / Diff 展示 / 红线注释）
-->

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { storeToRefs } from 'pinia'
import dayjs from 'dayjs'
import {
  ElAlert,
  ElButton,
  ElCard,
  ElCol,
  ElDialog,
  ElEmpty,
  ElForm,
  ElFormItem,
  ElInput,
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
import { useSetupDiffStore } from '@/store/modules/aps/setupDiff'
import { useApsAuthStore } from '@/store/modules/aps/auth'
import {
  SETUP_RULE_STATUS_META,
  SETUP_VERSION_STATUS_META,
  roleLabelOf,
  type SetupRuleChangeDto
} from '@/api/aps-v1'
import SetupRedlineAlert from './components/SetupRedlineAlert.vue'

defineOptions({ name: 'ApsSetupDiff' })

const diffStore = useSetupDiffStore()
const apsAuth = useApsAuthStore()

const {
  diff,
  selectedVersionId,
  otherVersionId,
  ruleSetVersions,
  diffLoading,
  versionsLoading,
  actionRunning,
  error,
  hasDiff,
  canPublishPerm,
  currentVersion,
  otherVersion,
  canPublishAction,
  totalChanges
} = storeToRefs(diffStore)

// ===== 发布 Dialog =====
const publishDialogVisible = ref(false)
const publishForm = ref({ changeReason: '' })

// ===== 加载 =====
async function loadAll() {
  await diffStore.loadRuleSetVersions()
  // 默认选中第一个 DRAFT 版本
  const draftVersion = ruleSetVersions.value.find((v) => v.status === 'DRAFT')
  if (draftVersion) {
    diffStore.selectVersion(draftVersion.id)
  }
}

onMounted(() => {
  void loadAll()
})

// ===== Diff 操作 =====
async function applyDiff() {
  if (!selectedVersionId.value || !otherVersionId.value) {
    ElMessage.warning('请选择源版本和目标版本')
    return
  }
  if (selectedVersionId.value === otherVersionId.value) {
    ElMessage.warning('源版本和目标版本相同，无法对比')
    return
  }
  await diffStore.loadDiff(selectedVersionId.value, otherVersionId.value)
}

// ===== 发布操作 =====
function openPublishDialog() {
  if (!canPublishPerm.value) {
    ElMessage.warning('当前操作者无发布权限，请联系系统管理员授予')
    return
  }
  if (!canPublishAction.value) {
    ElMessage.warning('只能发布草稿（DRAFT）状态的版本')
    return
  }
  publishForm.value.changeReason = ''
  publishDialogVisible.value = true
}

async function confirmPublish() {
  const reason = publishForm.value.changeReason.trim()
  if (!reason) {
    ElMessage.error('变更原因必填')
    return
  }

  let confirmed = false
  try {
    await ElMessageBox.confirm(
      `即将发布规则集版本 "${currentVersion.value?.versionCode}"。发布后该版本的换型规则将生效，且不可再编辑。是否继续？`,
      '发布确认',
      {
        type: 'warning',
        confirmButtonText: '确认发布',
        cancelButtonText: '取消',
        confirmButtonClass: 'el-button--primary'
      }
    )
    confirmed = true
  } catch {
    confirmed = false
  }

  if (!confirmed) return

  const ok = await diffStore.publish(selectedVersionId.value!, { changeReason: reason })
  if (ok) {
    ElMessage.success('发布成功')
    publishDialogVisible.value = false
    await loadAll()
  }
}

// ===== 格式化 =====
function formatDateTime(iso?: string) {
  return iso ? dayjs(iso).format('YYYY-MM-DD HH:mm:ss') : '—'
}

function getStatusTag(status: string) {
  // 版本选择器/publishStatus 传治理六态、规则变更行传派生三态；两表 DRAFT 同义，合并回退安全
  const meta =
    SETUP_RULE_STATUS_META[status as keyof typeof SETUP_RULE_STATUS_META] ??
    SETUP_VERSION_STATUS_META[status as keyof typeof SETUP_VERSION_STATUS_META]
  return meta ?? { label: status, tag: 'info' as const }
}

/** 规则类型中文标签（枚举值 → 中文） */
function ruleTypeLabel(ruleType: string): string {
  return ruleType === 'EXACT' ? '明确转换规则（EXACT）' : '默认换型规则（DEFAULT）'
}

function formatRuleChange(change: SetupRuleChangeDto) {
  const parts = [
    change.departmentCode,
    change.stageCode,
    change.operationCode,
    change.resourceCode
  ].filter(Boolean)

  if (change.ruleType === 'EXACT' && change.fromMaterialCode && change.toMaterialCode) {
    parts.push(`${change.fromMaterialCode} → ${change.toMaterialCode}`)
  }

  if (change.setupMinutes !== undefined) {
    parts.push(`${change.setupMinutes} 分钟`)
  }

  return parts.join(' / ')
}
</script>

<template>
  <div class="setup-diff-page">
    <!-- 页面头部 -->
    <div class="page-header">
      <div class="header-left">
        <h2>换型规则版本对比</h2>
        <p class="subtitle">对比两个规则版本的换型规则差异</p>
      </div>
      <div class="header-right">
        <ElTooltip
          :content="`当前操作者：${apsAuth.userInfo?.userCode ?? '—'}（${apsAuth.roles.map(roleLabelOf).join('、')}）`"
        >
          <ElTag type="info" size="small">{{ apsAuth.userInfo?.userCode ?? '—' }}</ElTag>
        </ElTooltip>
      </div>
    </div>

    <!-- 红线 Alert（§6.4） -->
    <SetupRedlineAlert page-type="diff" />

    <!-- 错误提示 -->
    <ElAlert
      v-if="error"
      type="error"
      :title="error"
      :closable="false"
      show-icon
      class="error-alert"
    />

    <!-- 版本选择器 -->
    <ElCard class="selector-card" shadow="never">
      <ElRow :gutter="16" align="middle">
        <ElCol :span="8">
          <div class="selector-label">源版本（当前）</div>
          <ElSelect
            :model-value="selectedVersionId ?? undefined"
            placeholder="选择源版本"
            :loading="versionsLoading"
            style="width: 100%"
            @change="diffStore.selectVersion"
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
        <ElCol :span="8">
          <div class="selector-label">目标版本（对比）</div>
          <ElSelect
            :model-value="otherVersionId ?? undefined"
            placeholder="选择目标版本"
            :loading="versionsLoading"
            style="width: 100%"
            @change="diffStore.selectOtherVersion"
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
        <ElCol :span="8">
          <div class="selector-label">&nbsp;</div>
          <ElButton
            type="primary"
            :loading="diffLoading"
            :disabled="
              !selectedVersionId || !otherVersionId || selectedVersionId === otherVersionId
            "
            @click="applyDiff"
          >
            <Icon icon="vi-ep:switch" class="mr-4" />
            对比
          </ElButton>
          <ElButton
            v-if="canPublishPerm"
            type="success"
            :disabled="!canPublishAction"
            @click="openPublishDialog"
          >
            <Icon icon="vi-ep:upload" class="mr-4" />
            发布
          </ElButton>
        </ElCol>
      </ElRow>
    </ElCard>

    <!-- Diff 结果 -->
    <ElCard v-if="hasDiff" class="diff-card" shadow="never">
      <template #header>
        <div class="card-header">
          <span>对比结果：{{ currentVersion?.versionCode }} → {{ otherVersion?.versionCode }}</span>
          <div>
            <ElTag type="success" size="small">+{{ diff?.addedCount }} 新增</ElTag>
            <ElTag type="warning" size="small" style="margin-left: 8px"
              >~{{ diff?.modifiedCount }} 修改</ElTag
            >
            <ElTag type="danger" size="small" style="margin-left: 8px"
              >-{{ diff?.removedCount }} 删除</ElTag
            >
          </div>
        </div>
      </template>

      <!-- 新增规则 -->
      <div v-if="diff?.setupRuleChanges.added.length" class="change-section">
        <h4>
          <ElTag type="success" size="small">新增</ElTag>
          {{ diff.setupRuleChanges.added.length }} 条规则
        </h4>
        <ElTable :data="diff.setupRuleChanges.added" border size="small">
          <ElTableColumn prop="ruleType" label="类型" width="100">
            <template #default="{ row }">
              <ElTag :type="row.ruleType === 'EXACT' ? 'primary' : 'info'" size="small">
                {{ ruleTypeLabel(row.ruleType) }}
              </ElTag>
            </template>
          </ElTableColumn>
          <ElTableColumn label="规则详情">
            <template #default="{ row }">
              {{ formatRuleChange(row) }}
            </template>
          </ElTableColumn>
        </ElTable>
      </div>

      <!-- 修改规则 -->
      <div v-if="diff?.setupRuleChanges.modified.length" class="change-section">
        <h4>
          <ElTag type="warning" size="small">修改</ElTag>
          {{ diff.setupRuleChanges.modified.length }} 条规则
        </h4>
        <ElTable :data="diff.setupRuleChanges.modified" border size="small">
          <ElTableColumn prop="ruleType" label="类型" width="100">
            <template #default="{ row }">
              <ElTag :type="row.ruleType === 'EXACT' ? 'primary' : 'info'" size="small">
                {{ ruleTypeLabel(row.ruleType) }}
              </ElTag>
            </template>
          </ElTableColumn>
          <ElTableColumn label="规则详情">
            <template #default="{ row }">
              {{ formatRuleChange(row) }}
            </template>
          </ElTableColumn>
          <ElTableColumn label="变更" width="200">
            <template #default="{ row }">
              <span v-if="row.before?.setupMinutes !== undefined" class="diff-old">
                {{ row.before.setupMinutes }} 分钟
              </span>
              <span
                v-if="
                  row.before?.setupMinutes !== undefined && row.after?.setupMinutes !== undefined
                "
              >
                →
              </span>
              <span v-if="row.after?.setupMinutes !== undefined" class="diff-new">
                {{ row.after.setupMinutes }} 分钟
              </span>
            </template>
          </ElTableColumn>
        </ElTable>
      </div>

      <!-- 删除规则 -->
      <div v-if="diff?.setupRuleChanges.removed.length" class="change-section">
        <h4>
          <ElTag type="danger" size="small">删除</ElTag>
          {{ diff.setupRuleChanges.removed.length }} 条规则
        </h4>
        <ElTable :data="diff.setupRuleChanges.removed" border size="small">
          <ElTableColumn prop="ruleType" label="类型" width="100">
            <template #default="{ row }">
              <ElTag :type="row.ruleType === 'EXACT' ? 'primary' : 'info'" size="small">
                {{ ruleTypeLabel(row.ruleType) }}
              </ElTag>
            </template>
          </ElTableColumn>
          <ElTableColumn label="规则详情">
            <template #default="{ row }">
              {{ formatRuleChange(row) }}
            </template>
          </ElTableColumn>
        </ElTable>
      </div>

      <!-- 无变更 -->
      <ElEmpty v-if="totalChanges === 0" description="无变更（源版本 = 目标版本）" />

      <div v-if="diff?.comparedAt" class="compared-at">
        对比时间：{{ formatDateTime(diff.comparedAt) }}
      </div>
    </ElCard>

    <ElEmpty v-else-if="!diffLoading" description="请选择源版本和目标版本进行对比" />

    <!-- 发布 Dialog -->
    <ElDialog
      v-model="publishDialogVisible"
      title="发布规则集版本"
      width="480px"
      :close-on-click-modal="false"
    >
      <ElAlert type="warning" :closable="false" show-icon style="margin-bottom: 16px">
        <template #title>发布后不可再编辑</template>
        发布后该版本的换型规则将生效，且不可再编辑。
      </ElAlert>
      <ElForm label-width="100px">
        <ElFormItem label="版本">
          <ElTag>{{ currentVersion?.versionCode }}</ElTag>
        </ElFormItem>
        <ElFormItem label="变更原因" required>
          <ElInput
            v-model="publishForm.changeReason"
            type="textarea"
            :rows="3"
            placeholder="请填写变更原因（必填）"
            maxlength="500"
            show-word-limit
          />
        </ElFormItem>
      </ElForm>
      <template #footer>
        <ElButton @click="publishDialogVisible = false">取消</ElButton>
        <ElButton
          type="primary"
          :loading="actionRunning"
          :disabled="!publishForm.changeReason.trim()"
          @click="confirmPublish"
        >
          确认发布
        </ElButton>
      </template>
    </ElDialog>
  </div>
</template>

<style scoped>
.setup-diff-page {
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

.diff-card {
  margin-bottom: 16px;
}

.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.change-section {
  margin-bottom: 20px;
}

.change-section:last-child {
  margin-bottom: 0;
}

.change-section h4 {
  margin: 0 0 12px 0;
  font-size: 14px;
  font-weight: 600;
  color: var(--el-text-color-primary);
  display: flex;
  align-items: center;
  gap: 8px;
}

.diff-old {
  color: var(--el-color-danger);
  text-decoration: line-through;
}

.diff-new {
  color: var(--el-color-success);
  font-weight: 600;
}

.compared-at {
  margin-top: 16px;
  padding-top: 12px;
  border-top: 1px solid var(--el-border-color-lighter);
  font-size: 12px;
  color: var(--el-text-color-secondary);
  text-align: right;
}

.mr-4 {
  margin-right: 4px;
}
</style>
