<!--
  APS V1 4号位 — 策略配置独立维护页（v1.4 §十八.10 / 长期欠账 v1.0/v1.1/v1.2 收口）

  复用 RBAC 4 页架构（加载 / 过滤 / 表格 / Dialog / 二次确认 / 红线注释 五段式）
  + Rules.vue Publish Dialog 模式（changeReason 必填 + ElMessageBox 二次确认 + ElAlert 红线）

  布局：
    左侧 Profile 列表（卡片式，含 RunType + IsActive）
    右侧 选中 Profile 后：
      顶部 KPI（6 态分布 + 已发布数 + 默认数）
      版本表格（6 态 Tag + 操作列）
      版本详情（Descriptions）
      Diff 面板（选 base + target → loadDiff）
      History 面板（保留以备扩展，目前简化）

  权限：
    - 路由级 aps.strategy.{view, edit, publish} 三码 OR（路由 meta）
    - 按钮级 用 store.canWrite / canPublishPerm / canRetire
-->
<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import dayjs from 'dayjs'
import { storeToRefs } from 'pinia'
import {
  ElAlert,
  ElButton,
  ElCard,
  ElCol,
  ElDescriptions,
  ElDescriptionsItem,
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
import {
  STRATEGY_STATUS_META,
  type StrategyProfileVersionDto,
  type StrategyVersionDraftInput,
  type StrategyVersionStatus
} from '@/api/aps-v1'
import { useStrategyProfileStore } from '@/store/modules/aps/strategyProfile'
import StrategyDraftDialog from './components/StrategyDraftDialog.vue'
import StrategyDiffPanel from './components/StrategyDiffPanel.vue'

const store = useStrategyProfileStore()
const {
  profiles,
  selectedProfileId,
  versions,
  currentDiff,
  publishValidation,
  runTrace,
  listLoading,
  versionsLoading,
  actionRunning,
  validating,
  error,
  currentProfile,
  currentVersion,
  canEdit,
  canPublishAction,
  canRetire,
  canWrite,
  canPublishPerm,
  hasPublished,
  statusCounts
} = storeToRefs(store)

/* ==================== 加载 ==================== */

async function loadAll(): Promise<void> {
  await store.loadProfiles()
  // 默认选中第一个 Profile
  if (selectedProfileId.value === null && profiles.value.length > 0) {
    await selectProfile(profiles.value[0].id)
  }
}

onMounted(loadAll)

/* ==================== 选择 Profile / Version ==================== */

async function selectProfile(profileId: number): Promise<void> {
  store.selectProfile(profileId)
  await store.loadVersions(profileId)
}

function selectVersion(versionId: number): void {
  store.selectVersion(versionId)
}

/* ==================== Diff 模式 ==================== */

const diffBaseId = ref<number | undefined>(undefined)
const diffTargetId = ref<number | undefined>(undefined)

watch(
  () => currentVersion.value?.id,
  (id) => {
    // 选中版本时默认 diff = (上一 PUBLISHED) → (当前)
    if (!id) {
      diffBaseId.value = undefined
      diffTargetId.value = undefined
      return
    }
    const published = versions.value.find((v) => v.status === 'PUBLISHED')
    diffBaseId.value = published?.id ?? undefined
    diffTargetId.value = id
  },
  { immediate: true }
)

async function applyDiff(): Promise<void> {
  if (diffBaseId.value === undefined || diffTargetId.value === undefined) return
  if (diffBaseId.value === diffTargetId.value) {
    store.handleError(new Error('源和目标版本相同'))
    return
  }
  await store.loadDiff(diffBaseId.value, diffTargetId.value)
}

/* ==================== Create / Edit DRAFT ==================== */

const draftDialogVisible = ref(false)
const draftMode = ref<'create' | 'edit'>('create')

function openCreateDraft(): void {
  if (!canWrite.value) {
    ElMessage.warning('当前操作者无创建权限，请联系系统管理员授予')
    return
  }
  if (!currentProfile.value) {
    ElMessage.warning('请先选择求解器策略')
    return
  }
  draftMode.value = 'create'
  draftDialogVisible.value = true
}

function openEditDraft(v: StrategyProfileVersionDto): void {
  if (!canEdit.value) {
    ElMessage.warning('仅草稿状态可编辑')
    return
  }
  if (v.status !== 'DRAFT') {
    ElMessage.warning(`当前状态为 ${STRATEGY_STATUS_META[v.status].label}，不可编辑`)
    return
  }
  draftMode.value = 'edit'
  // 当前版本会被 Dialog 通过 props 接收
  selectVersion(v.id)
  draftDialogVisible.value = true
}

async function handleDraftSubmit(input: StrategyVersionDraftInput): Promise<void> {
  if (draftMode.value === 'create') {
    const created = await store.createDraft(input)
    if (created) {
      ElMessage.success(`已创建策略版本 ${created.versionCode}（草稿）`)
      draftDialogVisible.value = false
      selectVersion(created.id)
    }
  } else if (currentVersion.value) {
    const ok = await store.updateDraft(currentVersion.value.id, input)
    if (ok) {
      ElMessage.success(`已更新策略版本 ${input.versionCode}`)
      draftDialogVisible.value = false
    }
  }
}

/* ==================== Publish Dialog ==================== */

const publishDialogVisible = ref(false)
const publishForm = reactive<{ changeReason: string }>({ changeReason: '' })

function openPublish(): void {
  if (!canPublishPerm.value) {
    ElMessage.warning('当前操作者无发布权限，请联系系统管理员授予')
    return
  }
  if (!canPublishAction.value) {
    ElMessage.warning(`当前状态 ${currentStatusText.value} 不可发布`)
    return
  }
  publishForm.changeReason = ''
  publishDialogVisible.value = true
}

async function confirmPublish(): Promise<void> {
  if (!currentVersion.value) return
  if (!publishForm.changeReason.trim()) {
    ElMessage.error('变更原因必填')
    return
  }
  // 二次确认（红线 UI 三重防护 #2）
  let confirmed = true
  try {
    await ElMessageBox.confirm(
      `即将发布策略版本「${currentVersion.value.versionCode}」。\n\n风险：若该版本设为默认，同一策略包下原有的默认版本会被自动取消默认。\n\n确认发布？`,
      '发布确认',
      {
        type: 'warning',
        confirmButtonText: '确认发布',
        cancelButtonText: '取消',
        confirmButtonClass: 'el-button--primary'
      }
    )
  } catch {
    confirmed = false
  }
  if (!confirmed) return
  const ok = await store.publish(currentVersion.value.id, {
    changeReason: publishForm.changeReason.trim()
  })
  if (ok) {
    ElMessage.success(`已发布策略版本 ${currentVersion.value.versionCode}`)
    publishDialogVisible.value = false
  }
}

/* ==================== Disable Dialog ==================== */

const disableDialogVisible = ref(false)
const disableForm = reactive<{ reason: string }>({ reason: '' })

function openDisable(): void {
  if (!canWrite.value) {
    ElMessage.warning('当前操作者无退役权限，请联系系统管理员授予')
    return
  }
  if (!canRetire.value) {
    ElMessage.warning(`当前状态 ${currentStatusText.value} 不可退役（仅已发布版本可退役）`)
    return
  }
  disableForm.reason = ''
  disableDialogVisible.value = true
}

async function confirmDisable(): Promise<void> {
  if (!currentVersion.value) return
  let confirmed = true
  try {
    await ElMessageBox.confirm(
      `即将退役策略版本「${currentVersion.value.versionCode}」。\n\n退役后该版本不可再被启用，仅保留审计追溯。`,
      '退役确认',
      {
        type: 'error',
        confirmButtonText: '确认退役',
        cancelButtonText: '取消',
        confirmButtonClass: 'el-button--danger'
      }
    )
  } catch {
    confirmed = false
  }
  if (!confirmed) return
  const ok = await store.disable(currentVersion.value.id, {
    reason: disableForm.reason.trim() || null
  })
  if (ok) {
    ElMessage.success(`已退役策略版本 ${currentVersion.value.versionCode}`)
    disableDialogVisible.value = false
  }
}

/* ==================== Validate ==================== */

async function onValidate(): Promise<void> {
  if (!currentVersion.value) return
  await store.validateDraft(currentVersion.value.id)
}

/* ==================== Trace ==================== */

async function loadTrace(): Promise<void> {
  if (!currentVersion.value) return
  await store.loadTrace(currentVersion.value.id)
}

/* ==================== 工具 ==================== */

const currentStatusText = computed<string>(() => {
  const s = currentVersion.value?.status
  if (!s) return '—'
  return STRATEGY_STATUS_META[s as StrategyVersionStatus].label
})

const profileRunTypeLabel = computed<string>(() => {
  const rt = currentProfile.value?.runType
  if (!rt) return '—'
  const map: Record<string, string> = {
    FULL_SCHEDULE: '全量排产',
    MANUAL_RESCHEDULE: '人工重排',
    LOCAL_RESCHEDULE: '局部插单',
    SIMULATION: '仿真演练',
    INSERT_ORDER_WHATIF: '插单 WhatIf'
  }
  return map[rt] ?? rt
})

function formatDateTime(s: string | null | undefined): string {
  return s ? dayjs(s).format('YYYY-MM-DD HH:mm:ss') : '—'
}

const statusTagType = (s: StrategyVersionStatus): 'info' | 'primary' | 'success' | 'warning' => {
  return STRATEGY_STATUS_META[s].tag as 'info' | 'primary' | 'success' | 'warning'
}

const statusTagEffect = (s: StrategyVersionStatus): 'light' | 'dark' | 'plain' | undefined => {
  return STRATEGY_STATUS_META[s].effect
}
</script>

<template>
  <div class="strategy-profile-page">
    <!-- 顶部 Header -->
    <div class="page-header">
      <div>
        <h2 class="page-title">策略配置</h2>
        <span class="page-subtitle">策略包与版本维护（草稿 / 发布 / 默认版本 / 版本对比）</span>
      </div>
      <ElButton @click="loadAll" :loading="listLoading">刷新求解器策略列表</ElButton>
    </div>

    <!-- 错误条 -->
    <ElAlert
      v-if="error"
      type="error"
      :closable="true"
      show-icon
      class="page-alert"
      @close="store.handleError(new Error(''))"
    >
      {{ error }}
    </ElAlert>

    <ElRow :gutter="16" class="page-body">
      <!-- 左侧：Profile 列表 -->
      <ElCol :span="8">
        <ElCard shadow="hover" header="策略 Profile 列表">
          <template v-if="profiles.length === 0 && !listLoading">
            <ElEmpty description="暂无 Profile" :image-size="80" />
          </template>
          <div v-else class="profile-list">
            <div
              v-for="p in profiles"
              :key="p.id"
              :class="['profile-card', { active: selectedProfileId === p.id }]"
              @click="selectProfile(p.id)"
            >
              <div class="profile-card-head">
                <code class="profile-code">{{ p.strategyProfileCode }}</code>
                <ElTag v-if="!p.isActive" size="small" type="info">停用</ElTag>
              </div>
              <div class="profile-name">{{ p.strategyProfileName }}</div>
              <div class="profile-meta">
                <span class="profile-run-type">{{ p.runType ?? '—' }}</span>
                <span class="profile-time">创建于 {{ formatDateTime(p.createdAt) }}</span>
              </div>
              <div v-if="p.description" class="profile-desc">{{ p.description }}</div>
            </div>
          </div>
        </ElCard>
      </ElCol>

      <!-- 右侧：版本管理 -->
      <ElCol :span="16">
        <template v-if="!currentProfile">
          <ElCard>
            <ElEmpty description="请选择左侧 Profile" :image-size="80" />
          </ElCard>
        </template>
        <template v-else>
          <!-- Profile 头部信息 -->
          <ElCard shadow="hover" class="profile-head-card">
            <div class="profile-head-row">
              <div>
                <code class="profile-code-lg">{{ currentProfile.strategyProfileCode }}</code>
                <span class="profile-name-lg">{{ currentProfile.strategyProfileName }}</span>
              </div>
              <div class="profile-head-actions">
                <ElTooltip content="仅创建草稿版本，不允许其它状态" placement="top">
                  <ElButton type="primary" :disabled="!canWrite" @click="openCreateDraft">
                    新建草稿
                  </ElButton>
                </ElTooltip>
              </div>
            </div>
            <ElDescriptions :column="3" size="small" border class="profile-desc-table">
              <ElDescriptionsItem label="批次类型">
                {{ profileRunTypeLabel }} <span class="d-sub">({{ currentProfile.runType }})</span>
              </ElDescriptionsItem>
              <ElDescriptionsItem label="状态">
                <ElTag :type="currentProfile.isActive ? 'success' : 'info'" size="small">
                  {{ currentProfile.isActive ? '启用' : '停用' }}
                </ElTag>
              </ElDescriptionsItem>
              <ElDescriptionsItem label="创建人 / 时间">
                {{ currentProfile.createdBy ?? '—' }} /
                {{ formatDateTime(currentProfile.createdAt) }}
              </ElDescriptionsItem>
              <ElDescriptionsItem label="描述" :span="3">
                {{ currentProfile.description ?? '—' }}
              </ElDescriptionsItem>
            </ElDescriptions>
          </ElCard>

          <!-- KPI 行 -->
          <ElRow :gutter="12" class="kpi-row">
            <ElCol v-for="(meta, key) in STRATEGY_STATUS_META" :key="key" :span="4">
              <ElCard shadow="never" class="kpi-card">
                <div class="kpi-label">{{ meta.label }}</div>
                <div class="kpi-value">{{ statusCounts[key as StrategyVersionStatus] }}</div>
              </ElCard>
            </ElCol>
          </ElRow>

          <!-- 版本表格 -->
          <ElCard shadow="hover" class="version-card">
            <template #header>
              <div class="card-head">
                <span class="card-title">版本列表（{{ versions.length }}）</span>
                <div>
                  <ElButton
                    size="small"
                    @click="selectProfile(currentProfile.id)"
                    :loading="versionsLoading"
                  >
                    刷新
                  </ElButton>
                </div>
              </div>
            </template>
            <ElTable
              :data="versions"
              v-loading="versionsLoading"
              border
              highlight-current-row
              @row-click="(row: StrategyProfileVersionDto) => selectVersion(row.id)"
            >
              <ElTableColumn type="index" label="#" width="50" />
              <ElTableColumn prop="versionCode" label="版本号" width="180">
                <template #default="{ row }: { row: StrategyProfileVersionDto }">
                  <code class="code-tag">{{ row.versionCode }}</code>
                </template>
              </ElTableColumn>
              <ElTableColumn label="状态" width="100">
                <template #default="{ row }: { row: StrategyProfileVersionDto }">
                  <ElTag
                    :type="statusTagType(row.status)"
                    :effect="statusTagEffect(row.status)"
                    size="small"
                  >
                    {{ STRATEGY_STATUS_META[row.status].label }}
                  </ElTag>
                </template>
              </ElTableColumn>
              <ElTableColumn label="默认" width="60" align="center">
                <template #default="{ row }: { row: StrategyProfileVersionDto }">
                  <ElTag v-if="row.isDefault" size="small" type="warning" effect="dark">★</ElTag>
                  <span v-else>—</span>
                </template>
              </ElTableColumn>
              <ElTableColumn prop="ruleSetVersionId" label="规则集版本" width="100" />
              <ElTableColumn prop="parameterSetVersionId" label="参数集版本" width="100" />
              <ElTableColumn label="发布时间" width="160">
                <template #default="{ row }: { row: StrategyProfileVersionDto }">
                  {{ formatDateTime(row.publishedAt) }}
                </template>
              </ElTableColumn>
              <ElTableColumn label="发布人" width="120">
                <template #default="{ row }: { row: StrategyProfileVersionDto }">
                  {{ row.publishedBy ?? '—' }}
                </template>
              </ElTableColumn>
              <ElTableColumn label="操作" width="280" fixed="right">
                <template #default="{ row }: { row: StrategyProfileVersionDto }">
                  <ElButton
                    size="small"
                    :disabled="!canWrite || row.status !== 'DRAFT'"
                    @click.stop="openEditDraft(row)"
                  >
                    编辑
                  </ElButton>
                  <ElButton
                    size="small"
                    type="primary"
                    :disabled="
                      !canPublishPerm ||
                      !(['DRAFT', 'SUBMITTED', 'APPROVED'] as StrategyVersionStatus[]).includes(
                        row.status
                      )
                    "
                    @click.stop="
                      () => {
                        selectVersion(row.id)
                        openPublish()
                      }
                    "
                  >
                    发布
                  </ElButton>
                  <ElButton
                    size="small"
                    type="danger"
                    :disabled="!canWrite || row.status !== 'PUBLISHED'"
                    @click.stop="
                      () => {
                        selectVersion(row.id)
                        openDisable()
                      }
                    "
                  >
                    退役
                  </ElButton>
                </template>
              </ElTableColumn>
            </ElTable>
          </ElCard>

          <!-- 选中版本的详情 + Diff + Trace -->
          <ElCard v-if="currentVersion" shadow="hover" class="version-detail-card">
            <template #header>
              <div class="card-head">
                <span class="card-title">
                  详情：
                  <ElTag
                    :type="statusTagType(currentVersion.status)"
                    :effect="statusTagEffect(currentVersion.status)"
                    size="small"
                  >
                    {{ STRATEGY_STATUS_META[currentVersion.status].label }}
                  </ElTag>
                  <code class="code-tag">{{ currentVersion.versionCode }}</code>
                </span>
                <div class="card-head-actions">
                  <ElButton size="small" @click="onValidate" :loading="validating">校验</ElButton>
                  <ElButton size="small" @click="loadTrace">追溯</ElButton>
                </div>
              </div>
            </template>

            <ElDescriptions :column="3" size="small" border>
              <ElDescriptionsItem label="版本号">
                <code class="code-tag">{{ currentVersion.versionCode }}</code>
              </ElDescriptionsItem>
              <ElDescriptionsItem label="状态">
                <ElTag
                  :type="statusTagType(currentVersion.status)"
                  :effect="statusTagEffect(currentVersion.status)"
                  size="small"
                >
                  {{ STRATEGY_STATUS_META[currentVersion.status].label }}
                </ElTag>
              </ElDescriptionsItem>
              <ElDescriptionsItem label="默认">
                <ElTag v-if="currentVersion.isDefault" type="warning" effect="dark" size="small"
                  >是</ElTag
                >
                <span v-else>—</span>
              </ElDescriptionsItem>
              <ElDescriptionsItem label="规则集版本 ID">{{
                currentVersion.ruleSetVersionId
              }}</ElDescriptionsItem>
              <ElDescriptionsItem label="参数集版本 ID">{{
                currentVersion.parameterSetVersionId
              }}</ElDescriptionsItem>
              <ElDescriptionsItem label="生效窗口">
                {{ formatDateTime(currentVersion.effectiveFrom) }} ~
                {{ formatDateTime(currentVersion.effectiveTo) }}
              </ElDescriptionsItem>
              <ElDescriptionsItem label="创建人 / 时间">
                {{ currentVersion.createdBy ?? '—' }} /
                {{ formatDateTime(currentVersion.createdAt) }}
              </ElDescriptionsItem>
              <ElDescriptionsItem label="发布人 / 时间">
                {{ currentVersion.publishedBy ?? '—' }} /
                {{ formatDateTime(currentVersion.publishedAt) }}
              </ElDescriptionsItem>
              <ElDescriptionsItem label="批准人 / 时间">
                {{ currentVersion.approvedBy ?? '—' }} /
                {{ formatDateTime(currentVersion.approvedAt) }}
              </ElDescriptionsItem>
            </ElDescriptions>

            <!-- Diff 面板 -->
            <div class="diff-section">
              <div class="diff-toolbar">
                <span class="section-title">版本对比</span>
                <ElSelect
                  v-model="diffBaseId"
                  placeholder="源版本"
                  size="small"
                  style="width: 200px"
                >
                  <ElOption
                    v-for="v in versions"
                    :key="`b-${v.id}`"
                    :label="`${v.versionCode} (#${v.id})`"
                    :value="v.id"
                  />
                </ElSelect>
                <span class="arrow">→</span>
                <ElSelect
                  v-model="diffTargetId"
                  placeholder="目标版本"
                  size="small"
                  style="width: 200px"
                >
                  <ElOption
                    v-for="v in versions"
                    :key="`t-${v.id}`"
                    :label="`${v.versionCode} (#${v.id})`"
                    :value="v.id"
                  />
                </ElSelect>
                <ElButton
                  size="small"
                  :disabled="
                    diffBaseId === null || diffTargetId === null || diffBaseId === diffTargetId
                  "
                  @click="applyDiff"
                >
                  查看 Diff
                </ElButton>
              </div>
              <StrategyDiffPanel :diff="currentDiff" />
            </div>

            <!-- 校验结果 -->
            <ElAlert
              v-if="publishValidation"
              :type="publishValidation.isValid ? 'success' : 'error'"
              :closable="true"
              show-icon
              class="page-alert"
              @close="publishValidation = null"
            >
              <template #title>
                发布前校验：{{ publishValidation.isValid ? '通过' : '未通过' }}
              </template>
              <ul v-if="publishValidation.errors.length > 0" class="validation-list">
                <li v-for="(e, idx) in publishValidation.errors" :key="`err-${idx}`">
                  <code class="code-tag">[{{ e.code }}]</code> {{ e.message }}
                  <span v-if="e.fieldName" class="d-sub">（{{ e.fieldName }}）</span>
                </li>
              </ul>
              <ul v-if="publishValidation.warnings.length > 0" class="validation-list">
                <li v-for="(w, idx) in publishValidation.warnings" :key="`warn-${idx}`">
                  <code class="code-tag">[{{ w.code }}]</code> {{ w.message }}
                </li>
              </ul>
              <span
                v-if="
                  publishValidation.errors.length === 0 && publishValidation.warnings.length === 0
                "
                class="d-sub"
              >
                校验时间：{{ formatDateTime(publishValidation.validatedAt) }}
              </span>
            </ElAlert>

            <!-- Run 引用追溯 -->
            <div v-if="runTrace" class="trace-section">
              <div class="section-title">排产批次引用追溯</div>
              <ElDescriptions :column="2" size="small" border>
                <ElDescriptionsItem label="策略版本">{{ runTrace.versionCode }}</ElDescriptionsItem>
                <ElDescriptionsItem label="状态">
                  <ElTag :type="statusTagType(runTrace.status)" size="small">
                    {{ STRATEGY_STATUS_META[runTrace.status].label }}
                  </ElTag>
                </ElDescriptionsItem>
                <ElDescriptionsItem label="规则集版本 ID">{{
                  runTrace.ruleSetVersionId
                }}</ElDescriptionsItem>
                <ElDescriptionsItem label="参数集版本 ID">{{
                  runTrace.parameterSetVersionId
                }}</ElDescriptionsItem>
              </ElDescriptions>
              <div class="trace-runs">
                <ElEmpty
                  v-if="runTrace.referencingRuns.length === 0"
                  description="无引用此版本的排产批次"
                  :image-size="60"
                />
                <ElTable v-else :data="runTrace.referencingRuns" size="small" border>
                  <ElTableColumn prop="runCode" label="批次编码" />
                  <ElTableColumn prop="runType" label="类型" width="120" />
                  <ElTableColumn prop="status" label="状态" width="120" />
                  <ElTableColumn label="创建时间" width="170">
                    <template #default="{ row }: { row: { createdAt: string } }">
                      {{ formatDateTime(row.createdAt) }}
                    </template>
                  </ElTableColumn>
                </ElTable>
              </div>
            </div>
          </ElCard>
        </template>
      </ElCol>
    </ElRow>

    <!-- Create / Edit DRAFT Dialog -->
    <StrategyDraftDialog
      v-if="currentProfile"
      v-model:visible="draftDialogVisible"
      :mode="draftMode"
      :profile="currentProfile"
      :version="currentVersion"
      :has-published="hasPublished"
      :submitting="actionRunning"
      @submit="handleDraftSubmit"
    />

    <!-- Publish Dialog -->
    <ElDialog
      v-model="publishDialogVisible"
      title="发布策略版本"
      width="500px"
      :close-on-click-modal="false"
      :close-on-press-escape="!actionRunning"
    >
      <ElForm label-width="100px">
        <ElFormItem label="求解器策略">
          <span class="d-val">{{ currentProfile?.strategyProfileCode ?? '—' }}</span>
        </ElFormItem>
        <ElFormItem label="版本号">
          <code class="code-tag">{{ currentVersion?.versionCode ?? '—' }}</code>
        </ElFormItem>
        <ElFormItem label="状态">
          <ElTag v-if="currentVersion" :type="statusTagType(currentVersion.status)" size="small">
            {{ STRATEGY_STATUS_META[currentVersion.status].label }}
          </ElTag>
        </ElFormItem>
        <ElFormItem label="变更原因（必填）">
          <ElInput
            v-model="publishForm.changeReason"
            type="textarea"
            :rows="3"
            placeholder="必须填写；将出现在审计记录"
          />
        </ElFormItem>
        <ElAlert type="warning" :closable="false" show-icon class="d-alert">
          <template #title>发布即不可原地修改</template>
          发布后此版本状态变为 PUBLISHED，不可再编辑；如需调整，请基于此版本创建新的 DRAFT。
        </ElAlert>
      </ElForm>
      <template #footer>
        <ElButton :disabled="actionRunning" @click="publishDialogVisible = false">取消</ElButton>
        <ElButton
          type="primary"
          :loading="actionRunning"
          :disabled="!canPublishPerm"
          @click="confirmPublish"
        >
          确认发布
        </ElButton>
      </template>
    </ElDialog>

    <!-- Disable Dialog -->
    <ElDialog
      v-model="disableDialogVisible"
      title="退役策略版本"
      width="500px"
      :close-on-click-modal="false"
      :close-on-press-escape="!actionRunning"
    >
      <ElForm label-width="100px">
        <ElFormItem label="求解器策略">
          <span class="d-val">{{ currentProfile?.strategyProfileCode ?? '—' }}</span>
        </ElFormItem>
        <ElFormItem label="版本号">
          <code class="code-tag">{{ currentVersion?.versionCode ?? '—' }}</code>
        </ElFormItem>
        <ElFormItem label="退役原因">
          <ElInput
            v-model="disableForm.reason"
            type="textarea"
            :rows="3"
            placeholder="可选；将出现在审计记录"
          />
        </ElFormItem>
        <ElAlert type="error" :closable="false" show-icon class="d-alert">
          <template #title>退役后不可恢复</template>
          退役后此版本进入 DISABLED 状态，不可再被启用；若 IsDefault=1 会被后端自动清除默认标志。
        </ElAlert>
      </ElForm>
      <template #footer>
        <ElButton :disabled="actionRunning" @click="disableDialogVisible = false">取消</ElButton>
        <ElButton
          type="danger"
          :loading="actionRunning"
          :disabled="!canWrite"
          @click="confirmDisable"
        >
          确认退役
        </ElButton>
      </template>
    </ElDialog>
  </div>
</template>

<style scoped>
.strategy-profile-page {
  padding: 16px;
}
.page-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-end;
  margin-bottom: 16px;
}
.page-title {
  margin: 0 0 4px 0;
  font-size: 20px;
  font-weight: 600;
}
.page-subtitle {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
.page-alert {
  margin-bottom: 12px;
}
.page-body {
  align-items: stretch;
}

.profile-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.profile-card {
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 4px;
  padding: 10px 12px;
  cursor: pointer;
  transition: all 0.2s;
}
.profile-card:hover {
  border-color: var(--el-color-primary-light-5);
  background: var(--el-color-primary-light-9);
}
.profile-card.active {
  border-color: var(--el-color-primary);
  background: var(--el-color-primary-light-9);
  box-shadow: 0 0 0 1px var(--el-color-primary);
}
.profile-card-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 4px;
}
.profile-code {
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 12px;
  color: var(--el-color-primary);
  background: var(--el-color-primary-light-9);
  padding: 1px 6px;
  border-radius: 3px;
}
.profile-name {
  font-weight: 600;
  margin-bottom: 4px;
}
.profile-meta {
  display: flex;
  justify-content: space-between;
  font-size: 11px;
  color: var(--el-text-color-secondary);
  margin-bottom: 4px;
}
.profile-run-type {
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
}
.profile-desc {
  font-size: 12px;
  color: var(--el-text-color-regular);
  margin-top: 4px;
}

.profile-head-card {
  margin-bottom: 12px;
}
.profile-head-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}
.profile-code-lg {
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 14px;
  color: var(--el-color-primary);
  background: var(--el-color-primary-light-9);
  padding: 2px 8px;
  border-radius: 3px;
  margin-right: 8px;
}
.profile-name-lg {
  font-size: 16px;
  font-weight: 600;
}
.profile-desc-table {
  margin-top: 8px;
}
.d-sub {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.kpi-row {
  margin-bottom: 12px;
}
.kpi-card {
  text-align: center;
}
.kpi-label {
  font-size: 12px;
  color: var(--el-text-color-secondary);
  margin-bottom: 4px;
}
.kpi-value {
  font-size: 20px;
  font-weight: 600;
  color: var(--el-color-primary);
}

.version-card,
.version-detail-card {
  margin-bottom: 12px;
}
.card-head {
  display: flex;
  justify-content: space-between;
  align-items: center;
}
.card-title {
  font-weight: 600;
}
.card-head-actions {
  display: flex;
  gap: 8px;
}

.diff-section,
.trace-section {
  margin-top: 16px;
  padding-top: 16px;
  border-top: 1px dashed var(--el-border-color-lighter);
}
.diff-toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 12px;
  flex-wrap: wrap;
}
.arrow {
  color: var(--el-color-primary);
  font-size: 16px;
}
.section-title {
  font-weight: 600;
  margin-right: 12px;
  font-size: 13px;
}

.validation-list {
  margin: 4px 0 0 0;
  padding-left: 20px;
}
.code-tag {
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 12px;
  background: var(--el-fill-color-light);
  padding: 1px 4px;
  border-radius: 2px;
}

.d-val {
  font-weight: 600;
}
.d-alert {
  margin-top: 8px;
}
</style>
