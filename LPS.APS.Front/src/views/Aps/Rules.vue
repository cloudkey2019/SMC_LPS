<script setup lang="ts">
/**
 * APS V1 4号位 — 规则与参数维护（页面 8 / U21 / U22）
 *
 * 4号位文档第 14 节约束：
 *  - DRAFT 可编辑；PUBLISHED 不可直接改；RETIRED 只读（U21/U22）
 *  - 优先级段位只展示 segmentName + orderInSegment，不暴露 PriorityScore（U21）
 *  - 发布产生新 Version，历史不覆盖（U22）
 *  - 前端不能直接 UPDATE Rule，所有写操作走 Controller（publish/retire/validate）
 *
 * 角色矩阵：
 *  - VIEWER / PMC              只读
 *  - RULE_ADMIN                可查看详情、提交校验
 *  - RULE_PUBLISHER / SYSTEM   可发布 / 退役
 */

import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import dayjs from 'dayjs'
import { storeToRefs } from 'pinia'
import { useRulesStore } from '@/store/modules/aps/rules'
import {
  RULE_STATUS_LABELS,
  RULE_STATUS_TAG,
  RULE_STATUSES,
  roleLabelOf,
  type RuleStatus,
  type BlockKey,
  BLOCK_KEYS
} from '@/api/aps-v1'
import type { ParameterRow, JsonValue } from '@/api/aps-v1/draftBuffer'

import {
  ElAlert,
  ElButton,
  ElCard,
  ElDescriptions,
  ElDescriptionsItem,
  ElDialog,
  ElEmpty,
  ElForm,
  ElFormItem,
  ElInput,
  ElInputNumber,
  ElMessageBox,
  ElRadioButton,
  ElRadioGroup,
  ElSwitch,
  ElTable,
  ElTableColumn,
  ElTag,
  ElTabs,
  ElTabPane,
  ElTooltip
} from 'element-plus'

const rulesStore = useRulesStore()
const {
  filteredRuleSets,
  statusCounts,
  currentDetail,
  currentDiff,
  lastPublish,
  lastRetire,
  statusFilter,
  loading,
  listLoading,
  actionRunning,
  validating,
  error,
  actor,
  actorRoles,
  canWrite,
  canPublishAction,
  // 双轨 + 6 态 getters
  actions,
  forkPermission,
  ruleSetDirty,
  parameterSetDirty,
  anyBufferDirty,
  parameterSetFlattenedRows,
  parameterSetBuffer,
  currentParameterSetVersionId
} = storeToRefs(rulesStore)

/** Non-null 在 v-if 块内用 */
const detailNN = computed(() => currentDetail.value!)
const diffNN = computed(() => currentDiff.value!)

/* ===== 字典 ===== */
/** v1.4 §二十：6 态常量从 common.ts 共享；本地不再定义 STATUS_TAG/STATUS_LABEL */
const VALIDATION_TAG: Record<'PENDING' | 'PASSED' | 'FAILED', 'info' | 'success' | 'danger'> = {
  PENDING: 'info',
  PASSED: 'success',
  FAILED: 'danger'
}

/** 校验状态中文标签（枚举值 → 中文） */
const VALIDATION_LABELS: Record<'PENDING' | 'PASSED' | 'FAILED', string> = {
  PENDING: '待校验',
  PASSED: '已通过',
  FAILED: '未通过'
}

/** 5 主题 JSON 块中文标签（B 设计稿 §1.1） */
const BLOCK_LABELS: Record<BlockKey, string> = {
  lock: '锁定策略',
  supply: '供应参数',
  procurement: '采购参数',
  solverStrategy: '求解器策略',
  candidateGuardrail: '候选版本守门'
}

/** 按块分组的扁平行（用于 5 块区隔渲染） */
const parameterRowsGrouped = computed<Record<BlockKey, ParameterRow[]>>(() => {
  const grouped: Record<BlockKey, ParameterRow[]> = {
    lock: [],
    supply: [],
    procurement: [],
    solverStrategy: [],
    candidateGuardrail: []
  }
  parameterSetFlattenedRows.value.forEach((row) => {
    grouped[row.block].push(row)
  })
  return grouped
})

/* ===== Tabs ===== */
type TabKey = 'parameters' | 'segments' | 'strategy' | 'diff' | 'history'
const activeTab = ref<TabKey>('parameters')

/* ===== 操作 ===== */
async function selectRuleSet(ruleSetId: number): Promise<void> {
  rulesStore.reset()
  activeTab.value = 'parameters'
  await rulesStore.loadDetail(ruleSetId)
}

async function onValidate(): Promise<void> {
  if (!detailNN.value) return
  await rulesStore.validateDraft(detailNN.value.summary.ruleSetId)
}

/* ===== [+ 新建草稿] Dialog（Step 3） ===== */
const createDraftDialog = ref(false)
const createDraftForm = ref({
  /** 注：4 号位 实现仅支持 fork（不含"全新创建 RuleSet"） */
  sourceRuleSetVersionId: 0,
  newVersionCode: '',
  changeReason: ''
})
/** Dialog 打开时初始化表单（基于当前选中 RuleSet）
 *  - 修复 forkDraft 传错 ID：之前传 currentVersion（业务版本号），真实模式会 404
 *  - 现从 detail.history 找 status=PUBLISHED/DISABLED 的 versionId（B 设计稿 §3.4）
 *  - 找不到时降级用 currentVersion（mock 兜底）
 */
function openCreateDraftDialog(): void {
  if (!detailNN.value) return
  const detail = detailNN.value
  const forkableStatuses: RuleStatus[] = ['PUBLISHED', 'DISABLED']
  const sourceVersion = detail.history
    .filter((h) => forkableStatuses.includes(h.status) && !!h.versionId)
    .slice(-1)[0]
  const sourceVersionId = sourceVersion?.versionId ?? detail.summary.currentVersion ?? 0
  // 新版本号基于源版本业务号 + 1（用户可在 Dialog 内调整）
  const baseVersion = sourceVersion?.version ?? detail.summary.currentVersion
  createDraftForm.value = {
    sourceRuleSetVersionId: sourceVersionId,
    newVersionCode: `v${baseVersion + 1}`,
    changeReason: ''
  }
  createDraftDialog.value = true
}
async function onCreateDraft(): Promise<void> {
  const reason = createDraftForm.value.changeReason.trim()
  if (!reason) {
    rulesStore.handleError(new Error('请填写变更原因'))
    return
  }
  const result = await rulesStore.forkDraft(
    createDraftForm.value.sourceRuleSetVersionId,
    createDraftForm.value.newVersionCode,
    reason
  )
  if (result) {
    createDraftDialog.value = false
    activeTab.value = 'parameters'
  }
}

/* ===== Publish Dialog ===== */
const publishDialog = ref(false)
const publishForm = ref({ changeReason: '' })

/** 当前是否需要双轨发布（PS Buffer 也持有 DRAFT → Q3 串行）
 *  - fork 后 parameterSetBuffer.governance.status === 'DRAFT'
 *  - 单轨发布（仅 RS）会留下 PS 仍是 DRAFT，状态不一致
 *  - 此时改走 publishDualTrack（先 RS 后 PS 串行）
 */
const needsDualPublish = computed(() => {
  const psStatus = parameterSetBuffer.value?.governance?.status
  return psStatus === 'DRAFT' && !!currentParameterSetVersionId.value
})

function openPublishDialog(): void {
  publishForm.value = { changeReason: '' }
  publishDialog.value = true
}
async function onPublish(): Promise<void> {
  if (!detailNN.value) return
  const reason = publishForm.value.changeReason.trim()
  if (!reason) {
    rulesStore.handleError(new Error('请填写变更原因'))
    return
  }
  // 任一 Buffer 脏 → 拒绝（强制先保存）
  if (anyBufferDirty.value) {
    rulesStore.handleError(new Error('存在未保存修改，请先 [保存草稿] 或 [取消未保存]'))
    return
  }
  // PS 也是 DRAFT → 双轨发布（Q3：独立不级联，前端按需串行调用）
  const ok = needsDualPublish.value
    ? await rulesStore.publishDualTrack(reason)
    : await (async () => {
        await rulesStore.publish(detailNN.value!.summary.ruleSetId, reason)
        return !error.value
      })()
  if (ok) publishDialog.value = false
}

/* ===== Retire Dialog ===== */
const retireDialog = ref(false)
const retireForm = ref({ reason: '' })

function openRetireDialog(): void {
  retireForm.value = { reason: '' }
  retireDialog.value = true
}
async function onRetire(): Promise<void> {
  if (!detailNN.value) return
  const reason = retireForm.value.reason.trim()
  if (!reason) {
    rulesStore.handleError(new Error('请填写退役原因'))
    return
  }
  await rulesStore.retire(detailNN.value.summary.ruleSetId, reason)
  if (!error.value) retireDialog.value = false
}

/* ===== [保存草稿] Dialog（Step 4） ===== */
const saveDraftDialog = ref(false)
const saveDraftForm = ref({ changeReason: '' })

function openSaveDraftDialog(): void {
  saveDraftForm.value = { changeReason: '' }
  saveDraftDialog.value = true
}
async function onSaveDraftConfirm(): Promise<void> {
  const reason = saveDraftForm.value.changeReason.trim()
  if (!reason) {
    rulesStore.handleError(new Error('请填写变更原因'))
    return
  }
  // 双轨保存：先 PS 后 RS（store 内部守卫）
  const psOk = await rulesStore.onSaveDraft('parameterSetBuffer', reason)
  if (!psOk) return
  // RS 脏则同步保存（通常初次 fork 后两条都干净；但保守实现）
  if (ruleSetDirty.value) {
    await rulesStore.onSaveDraft('ruleSetBuffer', reason)
  }
  if (!error.value) saveDraftDialog.value = false
}

/* ===== 参数行编辑包装（Step 4） ===== */
function onParameterCellEdit(row: ParameterRow, newValue: JsonValue): void {
  if (!row.editable) {
    rulesStore.handleError(new Error(`参数 ${row.path.join('.')} 不可编辑`))
    return
  }
  rulesStore.onCellEdit('parameterSetBuffer', row, newValue)
}

function setFilter(filter: RuleStatus | 'ALL'): void {
  rulesStore.setStatusFilter(filter)
}

/* ===== Issue 14：dirty 拦截（路由切换 + 浏览器关闭） ===== */
/**
 * 任一 Buffer 脏时：
 *  - 路由切换（onBeforeRouteLeave）→ ElMessageBox 二次确认
 *  - 浏览器关闭/刷新（beforeunload）→ 浏览器原生确认（Chrome/Firefox 自动弹"是否离开"）
 *
 * 注：beforeunload 监听器必须返回非空字符串才触发原生弹窗；现代浏览器会忽略自定义文案。
 */
async function confirmLeaveIfDirty(): Promise<boolean> {
  if (!anyBufferDirty.value) return true
  try {
    await ElMessageBox.confirm(
      '当前规则集或参数集有未保存的修改，离开将丢失。\n请先点击「保存草稿」或「取消未保存」。',
      '未保存修改',
      {
        confirmButtonText: '确认离开',
        cancelButtonText: '留在页面',
        type: 'warning',
        closeOnClickModal: false,
        closeOnPressEscape: false
      }
    )
    return true
  } catch {
    // 用户点"留在页面"
    return false
  }
}

/** 浏览器关闭/刷新拦截（Chrome/Firefox/Edge 自动弹原生确认）
 *  - 设置 returnValue 触发浏览器原生确认弹窗
 *  - 注：listener 返回值必须 void（addEventListener 契约）
 */
function handleBeforeUnload(e: BeforeUnloadEvent): void {
  if (anyBufferDirty.value) {
    e.preventDefault()
    e.returnValue = ''
  }
}

onMounted(() => {
  rulesStore.loadList()
  window.addEventListener('beforeunload', handleBeforeUnload)
})

onBeforeUnmount(() => {
  window.removeEventListener('beforeunload', handleBeforeUnload)
})

onBeforeRouteLeave(async () => {
  const ok = await confirmLeaveIfDirty()
  if (!ok) return false
  return true
})
</script>

<template>
  <div class="aps-rules">
    <!-- ===== 顶部 ===== -->
    <div class="page-header">
      <div>
        <h2 class="page-title">规则与参数维护</h2>
        <p class="page-sub">
          草稿可编辑；已发布 / 已停用不可直接修改；已提交 / 已批准 / 已归档为只读。
        </p>
      </div>
      <div class="header-actions">
        <ElTooltip
          :content="`当前操作者：${actor}（${actorRoles.map(roleLabelOf).join('、')}）`"
          placement="bottom"
        >
          <ElTag effect="plain" type="primary">
            <Icon icon="vi-mdi:account-circle" /> {{ actor }}
          </ElTag>
        </ElTooltip>
        <!-- [+ 新建草稿] 按钮（B 设计稿 §3.4 fork 守门） -->
        <ElTooltip
          v-if="currentDetail"
          :content="
            forkPermission.allowed ? '基于当前规则集新建草稿版本' : (forkPermission.reason ?? '')
          "
          placement="bottom"
        >
          <ElButton
            type="primary"
            :disabled="!actions.canCreateDraft || !forkPermission.allowed"
            @click="openCreateDraftDialog"
          >
            <Icon icon="vi-mdi:file-plus-outline" /> 新建草稿
          </ElButton>
        </ElTooltip>
        <ElButton :loading="listLoading" @click="rulesStore.loadList">
          <Icon icon="vi-ep:refresh" /> 刷新列表
        </ElButton>
      </div>
    </div>

    <!-- ===== 业务约束提示 ===== -->
    <ElAlert type="info" :closable="false" show-icon class="hint-bar">
      <template #title>使用说明</template>
      <span class="hint-text">
        优先级分段只展示<strong>段内顺序</strong>，不展示内部评分；发布规则会产生<strong>新版本</strong>，
        历史版本不会被覆盖；已发布版本的字段不可直接修改，只能新建版本。
      </span>
    </ElAlert>

    <!-- ===== 错误条 ===== -->
    <ElAlert v-if="error" type="error" :closable="false" show-icon :title="error" />

    <!-- ===== KPI 横条（v1.4 §二十 6 态；仅显示非零状态） ===== -->
    <div class="kpi-strip">
      <div class="kpi-pill">
        <span class="pill-label">规则集总数</span>
        <span class="pill-val">{{ rulesStore.ruleSets.length }}</span>
      </div>
      <div v-for="key in RULE_STATUSES" :key="key" class="kpi-pill">
        <span class="pill-label">{{ RULE_STATUS_LABELS[key] }}</span>
        <span class="pill-val">{{ statusCounts[key] ?? 0 }}</span>
      </div>
    </div>

    <!-- ===== 状态过滤（v1.4 §二十 6 态） ===== -->
    <div class="filter-row">
      <ElRadioGroup v-model="statusFilter" @change="setFilter">
        <ElRadioButton label="ALL">全部</ElRadioButton>
        <ElRadioButton v-for="key in RULE_STATUSES" :key="key" :label="key">
          {{ key }}
        </ElRadioButton>
      </ElRadioGroup>
    </div>

    <div class="content-row">
      <!-- ===== 左：规则集列表 ===== -->
      <ElCard class="panel left-panel">
        <template #header>
          <div class="panel-header">
            <span>规则集列表</span>
            <ElTag size="small" effect="plain">{{ filteredRuleSets.length }}</ElTag>
          </div>
        </template>
        <div v-if="!filteredRuleSets.length && !listLoading" class="panel-empty">
          <ElEmpty description="无符合条件的规则集" />
        </div>
        <div v-else class="ruleset-list">
          <div
            v-for="r in filteredRuleSets"
            :key="r.ruleSetId"
            class="ruleset-card"
            :class="{
              active: detailNN?.summary.ruleSetId === r.ruleSetId,
              draft: r.status === 'DRAFT',
              published: r.status === 'PUBLISHED',
              disabled: r.status === 'DISABLED'
            }"
            @click="selectRuleSet(r.ruleSetId)"
          >
            <div class="rs-head">
              <span class="rs-code">{{ r.ruleSetCode }}</span>
              <ElTag :type="RULE_STATUS_TAG[r.status]" size="small" effect="dark">
                {{ RULE_STATUS_LABELS[r.status] }}
              </ElTag>
            </div>
            <div class="rs-name">{{ r.ruleSetName }}</div>
            <div class="rs-meta">
              <span class="meta-key">排程域</span>
              <ElTag size="small" effect="plain">{{ r.domainKey || '—' }}</ElTag>
            </div>
            <div class="rs-meta">
              <span class="meta-key">当前版本</span>
              <span class="meta-val">{{ r.currentVersion ? `v${r.currentVersion}` : '—' }}</span>
              <span v-if="r.draftVersion" class="meta-draft"> / 草稿 v{{ r.draftVersion }} </span>
            </div>
            <div v-if="r.lastChangeReason" class="rs-reason">
              {{ r.lastChangeReason }}
            </div>
          </div>
        </div>
      </ElCard>

      <!-- ===== 右：详情 ===== -->
      <ElCard class="panel right-panel">
        <template v-if="!currentDetail">
          <ElEmpty description="从左侧选择一个规则集" />
        </template>

        <div v-else>
          <!-- ===== Header 条 ===== -->
          <div class="detail-head-row">
            <div>
              <ElTag :type="RULE_STATUS_TAG[detailNN.summary.status]" effect="dark" size="small">
                {{ RULE_STATUS_LABELS[detailNN.summary.status] }}
              </ElTag>
              {{ detailNN.summary.ruleSetCode }}
              <span class="muted-tag">v{{ detailNN.summary.currentVersion }}</span>
              <span v-if="detailNN.summary.draftVersion" class="muted-tag">
                → 草稿 v{{ detailNN.summary.draftVersion }}
              </span>
            </div>
            <div class="head-actions">
              <ElButton
                v-if="actions.canValidate"
                size="small"
                :loading="validating"
                @click="onValidate"
              >
                <Icon icon="vi-mdi:check-decagram" /> 校验草稿
              </ElButton>
              <ElButton
                v-if="actions.canPublish"
                type="primary"
                size="small"
                :disabled="!canPublishAction"
                @click="openPublishDialog"
              >
                <Icon icon="vi-mdi:rocket-launch" /> 发布
              </ElButton>
              <ElButton
                v-if="actions.canRetire"
                type="danger"
                size="small"
                plain
                :disabled="!canPublishAction"
                @click="openRetireDialog"
              >
                <Icon icon="vi-mdi:archive" /> 退役
              </ElButton>
            </div>
          </div>

          <!-- ===== Summary 描述 ===== -->
          <ElDescriptions :column="3" size="small" border>
            <ElDescriptionsItem label="规则集">{{
              detailNN.summary.ruleSetName
            }}</ElDescriptionsItem>
            <ElDescriptionsItem label="排程域">{{ detailNN.domainKey }}</ElDescriptionsItem>
            <ElDescriptionsItem label="状态">
              <ElTag :type="RULE_STATUS_TAG[detailNN.summary.status]" size="small">
                {{ RULE_STATUS_LABELS[detailNN.summary.status] }}
              </ElTag>
            </ElDescriptionsItem>
            <ElDescriptionsItem label="当前版本"
              >v{{ detailNN.summary.currentVersion }}</ElDescriptionsItem
            >
            <ElDescriptionsItem label="草稿版本">
              <span v-if="detailNN.summary.draftVersion">v{{ detailNN.summary.draftVersion }}</span>
              <span v-else class="muted">—</span>
            </ElDescriptionsItem>
            <ElDescriptionsItem label="校验状态">
              <ElTag :type="VALIDATION_TAG[detailNN.summary.validationStatus]" size="small">
                {{ VALIDATION_LABELS[detailNN.summary.validationStatus] }}
              </ElTag>
              <span v-if="detailNN.summary.validationMessage" class="msg-line">
                {{ detailNN.summary.validationMessage }}
              </span>
            </ElDescriptionsItem>
            <ElDescriptionsItem label="最近变更" :span="3">
              {{ detailNN.summary.lastChangeReason || '（无）' }}
            </ElDescriptionsItem>
            <ElDescriptionsItem v-if="!canPublishAction && canWrite" label="权限" :span="3">
              <ElTag type="warning" size="small">当前账号无发布/退役权限</ElTag>
              <span class="msg-line">如需发布或退役，请联系系统管理员授予相应权限</span>
            </ElDescriptionsItem>
          </ElDescriptions>

          <!-- ===== Tabs ===== -->
          <ElTabs v-model="activeTab" class="detail-tabs">
            <!-- 1. Parameters（B 设计稿 Step 4：5 块区隔 + 可编辑 + dirty + 保存草稿） -->
            <ElTabPane label="参数维护" name="parameters">
              <!-- 顶部状态条：当前规则集状态 + buffer dirty 状态 -->
              <ElAlert
                v-if="!actions.canEdit && currentDetail"
                type="info"
                :closable="false"
                show-icon
                class="tab-alert"
              >
                <template #title>
                  {{ `${RULE_STATUS_LABELS[detailNN.summary.status]} 状态：参数只读` }}
                </template>
                已发布 / 已停用的版本不可直接修改；如需变更，请新建草稿 → 校验 → 发布。
              </ElAlert>

              <ElAlert
                v-if="currentDetail && !currentParameterSetVersionId"
                type="warning"
                :closable="false"
                show-icon
                class="tab-alert"
              >
                <template #title>当前规则集未加载参数集详情</template>
                请点击右上角「+ 新建草稿」新建一份草稿；若数据暂不完整，请稍后重试或联系系统管理员。
              </ElAlert>

              <!-- 5 主题 JSON 块区隔渲染 -->
              <template v-if="parameterSetBuffer.workingBlocks">
                <div v-for="blockKey in BLOCK_KEYS" :key="blockKey" class="param-block-section">
                  <div class="block-header">
                    <h4 class="block-title">{{ BLOCK_LABELS[blockKey] }}</h4>
                    <ElTag
                      v-if="parameterSetBuffer.dirty[blockKey]"
                      type="warning"
                      size="small"
                      effect="dark"
                    >
                      ● 未保存
                    </ElTag>
                    <ElTag v-else size="small" effect="plain">
                      {{ parameterRowsGrouped[blockKey].length }} 项
                    </ElTag>
                  </div>
                  <ElEmpty
                    v-if="parameterRowsGrouped[blockKey].length === 0"
                    :description="`${BLOCK_LABELS[blockKey]} 暂无参数`"
                    :image-size="60"
                  />
                  <ElTable v-else :data="parameterRowsGrouped[blockKey]" size="small" border>
                    <ElTableColumn label="参数键" min-width="200">
                      <template #default="{ row }">
                        <code class="code-tag">{{ row.path.join('.') }}</code>
                      </template>
                    </ElTableColumn>
                    <ElTableColumn label="类型" width="90" align="center">
                      <template #default="{ row }">
                        <ElTag size="small" effect="plain">{{ row.type }}</ElTag>
                      </template>
                    </ElTableColumn>
                    <ElTableColumn label="当前值" min-width="240">
                      <template #default="{ row }">
                        <!-- 只读（不可编辑） -->
                        <span v-if="!row.editable || !actions.canEdit" class="readonly-val">
                          {{ row.value === null ? '—' : String(row.value) }}
                        </span>
                        <!-- 可编辑：根据 type 渲染不同控件 -->
                        <ElInputNumber
                          v-else-if="
                            row.type === 'NUMBER' ||
                            row.type === 'PERCENT' ||
                            row.type === 'DURATION'
                          "
                          :model-value="typeof row.value === 'number' ? row.value : 0"
                          :step="row.type === 'PERCENT' ? 0.01 : 1"
                          :precision="row.type === 'PERCENT' ? 4 : 0"
                          size="small"
                          @change="(v) => onParameterCellEdit(row, typeof v === 'number' ? v : 0)"
                        />
                        <ElSwitch
                          v-else-if="row.type === 'BOOLEAN'"
                          :model-value="row.value === true"
                          size="small"
                          @change="(v) => onParameterCellEdit(row, Boolean(v))"
                        />
                        <ElInput
                          v-else
                          :model-value="String(row.value ?? '')"
                          size="small"
                          @change="(v) => onParameterCellEdit(row, v)"
                        />
                      </template>
                    </ElTableColumn>
                    <ElTableColumn label="敏感" width="70" align="center">
                      <template #default="{ row }">
                        <ElTag v-if="row.sensitive" size="small" type="warning">是</ElTag>
                        <span v-else class="muted">否</span>
                      </template>
                    </ElTableColumn>
                    <ElTableColumn label="可编辑" width="80" align="center">
                      <template #default="{ row }">
                        <ElTag v-if="row.editable && actions.canEdit" size="small" type="success"
                          >是</ElTag
                        >
                        <ElTag v-else size="small" type="info">否</ElTag>
                      </template>
                    </ElTableColumn>
                  </ElTable>
                </div>

                <!-- 双轨保存操作条（仅草稿态 + 可编辑时可见） -->
                <div v-if="actions.canEdit" class="save-bar">
                  <ElTooltip :content="parameterSetDirty ? '有未保存修改' : '无修改'">
                    <ElTag :type="parameterSetDirty ? 'warning' : 'info'" size="small">
                      参数集 {{ parameterSetDirty ? '未保存' : '已保存' }}
                    </ElTag>
                  </ElTooltip>
                  <ElTooltip
                    v-if="ruleSetDirty"
                    :content="'规则集也有未保存修改，请一并保存'"
                    placement="top"
                  >
                    <ElTag type="warning" size="small">规则集未保存</ElTag>
                  </ElTooltip>
                  <ElButton
                    :disabled="!parameterSetDirty"
                    @click="rulesStore.onCancelDirty('parameterSetBuffer')"
                  >
                    <Icon icon="vi-mdi:undo" /> 取消未保存
                  </ElButton>
                  <ElButton
                    type="primary"
                    :loading="parameterSetBuffer.saving"
                    :disabled="!parameterSetDirty"
                    @click="openSaveDraftDialog"
                  >
                    <Icon icon="vi-mdi:content-save" /> 保存草稿
                  </ElButton>
                </div>
              </template>
              <ElEmpty v-else description="无参数集详情" />
            </ElTabPane>

            <!-- 2. 优先级段位 -->
            <ElTabPane label="优先级段位" name="segments">
              <ElAlert type="warning" :closable="false" show-icon class="tab-alert">
                <template #title>只展示段内顺序</template>
                内部优先级评分不对页面开放，只展示"段内第 N 位"的顺序语义。
              </ElAlert>
              <ElTable :data="detailNN.prioritySegments" size="small" border>
                <ElTableColumn label="顺序" width="80" align="center">
                  <template #default="{ row }">
                    <span class="order-circle">{{ row.orderInSegment }}</span>
                  </template>
                </ElTableColumn>
                <ElTableColumn prop="segmentCode" label="段码" width="100" />
                <ElTableColumn prop="segmentName" label="段位名" min-width="200" />
                <ElTableColumn prop="description" label="说明" min-width="280" />
              </ElTable>
            </ElTabPane>

            <!-- 3. Strategy -->
            <ElTabPane label="求解器策略" name="strategy">
              <div class="strategy-tab-head">
                <ElAlert
                  v-if="detailNN.strategyProfile.status !== 'PUBLISHED'"
                  type="info"
                  :closable="false"
                  show-icon
                  class="tab-alert"
                >
                  <template #title>
                    求解器策略当前为 {{ RULE_STATUS_LABELS[detailNN.strategyProfile.status] }}
                  </template>
                  修改需通过 StrategyController，本页只展示结果。
                </ElAlert>
                <ElButton type="primary" @click="$router.push('/aps/strategy-profile')">
                  打开独立策略配置 →
                </ElButton>
              </div>
              <ElDescriptions :column="2" size="small" border>
                <ElDescriptionsItem label="策略代码">{{
                  detailNN.strategyProfile.strategyProfileCode
                }}</ElDescriptionsItem>
                <ElDescriptionsItem label="策略名">{{
                  detailNN.strategyProfile.strategyProfileName
                }}</ElDescriptionsItem>
                <ElDescriptionsItem label="版本"
                  >v{{ detailNN.strategyProfile.version }}</ElDescriptionsItem
                >
                <ElDescriptionsItem label="状态">
                  <ElTag :type="RULE_STATUS_TAG[detailNN.strategyProfile.status]" size="small">
                    {{ RULE_STATUS_LABELS[detailNN.strategyProfile.status] }}
                  </ElTag>
                </ElDescriptionsItem>
                <ElDescriptionsItem label="启用特性" :span="2">
                  <ElTag
                    v-for="f in detailNN.strategyProfile.features"
                    :key="f"
                    size="small"
                    effect="plain"
                    style="margin-right: 6px"
                  >
                    {{ f }}
                  </ElTag>
                </ElDescriptionsItem>
                <ElDescriptionsItem label="求解器配置" :span="2">
                  <pre class="json-block">{{
                    JSON.stringify(detailNN.strategyProfile.solverStrategy, null, 2)
                  }}</pre>
                </ElDescriptionsItem>
                <ElDescriptionsItem
                  v-if="detailNN.strategyProfile.changeReason"
                  label="变更原因"
                  :span="2"
                >
                  {{ detailNN.strategyProfile.changeReason }}
                </ElDescriptionsItem>
              </ElDescriptions>
            </ElTabPane>

            <!-- 4. Diff -->
            <ElTabPane label="版本对比" name="diff">
              <template
                v-if="
                  !currentDiff ||
                  diffNN.summary.addedCount +
                    diffNN.summary.modifiedCount +
                    diffNN.summary.removedCount ===
                    0
                "
              >
                <ElEmpty description="无变更（base = target）" />
              </template>
              <template v-else>
                <div class="diff-summary">
                  <ElTag type="warning" size="small">+{{ diffNN.summary.addedCount }} 新增</ElTag>
                  <ElTag type="primary" size="small"
                    >~{{ diffNN.summary.modifiedCount }} 修改</ElTag
                  >
                  <ElTag type="danger" size="small">-{{ diffNN.summary.removedCount }} 删除</ElTag>
                  <span class="diff-meta">
                    base v{{ diffNN.baseVersion }} → target v{{ diffNN.targetVersion }}
                  </span>
                </div>
                <ElTable :data="diffNN.changedParameters" size="small" border>
                  <ElTableColumn prop="parameterKey" label="参数键" min-width="200">
                    <template #default="{ row }">
                      <code class="code-tag">{{ row.parameterKey }}</code>
                    </template>
                  </ElTableColumn>
                  <ElTableColumn label="旧值" width="180" align="right">
                    <template #default="{ row }">
                      <span class="diff-old">{{ row.oldDisplay ?? row.oldValue }}</span>
                    </template>
                  </ElTableColumn>
                  <ElTableColumn label="新值" width="180" align="right">
                    <template #default="{ row }">
                      <span class="diff-new">{{ row.newDisplay ?? row.newValue }}</span>
                    </template>
                  </ElTableColumn>
                </ElTable>
              </template>
            </ElTabPane>

            <!-- 5. History -->
            <ElTabPane label="历史版本" name="history">
              <ElTable :data="detailNN.history" size="small" border>
                <ElTableColumn label="版本" width="100" align="center">
                  <template #default="{ row }">
                    <span :class="{ 'history-current': row.isCurrent }">v{{ row.version }}</span>
                  </template>
                </ElTableColumn>
                <ElTableColumn label="状态" width="110" align="center">
                  <template #default="{ row }">
                    <ElTag :type="RULE_STATUS_TAG[row.status]" size="small">
                      {{ RULE_STATUS_LABELS[row.status] }}
                    </ElTag>
                  </template>
                </ElTableColumn>
                <ElTableColumn prop="changeReason" label="变更原因" min-width="220" />
                <ElTableColumn label="发布者" width="140">
                  <template #default="{ row }">
                    <code class="code-tag">{{ row.publishedBy }}</code>
                  </template>
                </ElTableColumn>
                <ElTableColumn label="发布时间" width="180">
                  <template #default="{ row }">
                    {{ row.publishedAt ? dayjs(row.publishedAt).format('YYYY-MM-DD HH:mm') : '—' }}
                  </template>
                </ElTableColumn>
                <ElTableColumn label="当前" width="80" align="center">
                  <template #default="{ row }">
                    <ElTag v-if="row.isCurrent" size="small" type="success" effect="dark">是</ElTag>
                    <span v-else class="muted">否</span>
                  </template>
                </ElTableColumn>
              </ElTable>
              <ElAlert type="info" :closable="false" show-icon class="u22-alert">
                <template #title>发布产生新版本，历史不覆盖</template>
                每次发布产生新的 v+1，历史版本完整保留，可随时回看变更轨迹。
              </ElAlert>
            </ElTabPane>
          </ElTabs>

          <!-- ===== 最近发布结果（可追溯） ===== -->
          <ElCard v-if="lastPublish" class="result-card publish-card">
            <template #header>
              <span>
                <Icon icon="vi-mdi:rocket-launch" />
                最近发布结果
              </span>
            </template>
            <ElDescriptions :column="3" size="small" border>
              <ElDescriptionsItem label="规则集">{{ lastPublish.ruleSetCode }}</ElDescriptionsItem>
              <ElDescriptionsItem label="新版本号">
                <ElTag type="success" size="small" effect="dark"
                  >v{{ lastPublish.newVersion }}</ElTag
                >
              </ElDescriptionsItem>
              <ElDescriptionsItem label="操作者">{{ lastPublish.actor }}</ElDescriptionsItem>
              <ElDescriptionsItem label="发布时间">
                {{ dayjs(lastPublish.publishedAt).format('YYYY-MM-DD HH:mm:ss') }}
              </ElDescriptionsItem>
              <ElDescriptionsItem label="历史版本" :span="2">
                <ElTag
                  v-for="v in lastPublish.historicalVersions"
                  :key="v"
                  size="small"
                  effect="plain"
                  style="margin-right: 4px"
                >
                  v{{ v }}
                </ElTag>
              </ElDescriptionsItem>
            </ElDescriptions>
          </ElCard>

          <ElCard v-if="lastRetire" class="result-card retire-card">
            <template #header>
              <span>
                <Icon icon="vi-mdi:archive" />
                最近退役结果
              </span>
            </template>
            <ElDescriptions :column="3" size="small" border>
              <ElDescriptionsItem label="规则集">{{ lastRetire.ruleSetCode }}</ElDescriptionsItem>
              <ElDescriptionsItem label="退役版本">v{{ lastRetire.version }}</ElDescriptionsItem>
              <ElDescriptionsItem label="操作者">{{ lastRetire.actor }}</ElDescriptionsItem>
              <ElDescriptionsItem label="退役时间" :span="3">
                {{ dayjs(lastRetire.retiredAt).format('YYYY-MM-DD HH:mm:ss') }}
              </ElDescriptionsItem>
            </ElDescriptions>
          </ElCard>
        </div>
      </ElCard>
    </div>

    <!-- ===== 加载态 ===== -->
    <div v-if="loading" class="loading-tip">加载规则集详情中…</div>

    <!-- ===== Publish Dialog ===== -->
    <ElDialog
      v-model="publishDialog"
      :title="needsDualPublish ? '双轨发布草稿（规则集 + 参数集依次发布）' : '发布草稿'"
      width="520px"
      :close-on-click-modal="false"
    >
      <ElForm label-width="100px" size="default">
        <ElFormItem label="规则集">
          <span class="d-val">{{ detailNN?.summary.ruleSetCode }}</span>
        </ElFormItem>
        <ElFormItem label="新版本号">
          <span class="d-val">v{{ detailNN?.summary.draftVersion }}</span>
        </ElFormItem>
        <ElFormItem v-if="needsDualPublish" label="参数集">
          <span class="d-val">
            {{ parameterSetBuffer.governance?.versionCode }}（{{ RULE_STATUS_LABELS.DRAFT }}）
          </span>
        </ElFormItem>
        <ElFormItem label="变更原因 (必填)">
          <ElInput
            v-model="publishForm.changeReason"
            type="textarea"
            :rows="3"
            placeholder="必须填写；将出现在审计记录"
          />
        </ElFormItem>
        <ElAlert
          :type="needsDualPublish ? 'warning' : 'info'"
          :closable="false"
          show-icon
          class="d-alert"
        >
          <template #title>
            {{
              needsDualPublish
                ? '双轨发布：规则集与参数集相互独立，依次发布'
                : '发布产生新版本，历史不覆盖'
            }}
          </template>
          <template v-if="needsDualPublish">
            检测到当前规则集的草稿还关联着一份参数集草稿；将先发布参数集、再发布规则集。<br />
            <strong>任一失败即停 + 保留前端 dirty 状态</strong>（不会半发布）。
          </template>
          <template v-else>
            提交后此规则集进入已发布状态；原已发布版本成为历史版本，可随时回看。
          </template>
        </ElAlert>
      </ElForm>
      <template #footer>
        <ElButton @click="publishDialog = false">取消</ElButton>
        <ElButton
          type="primary"
          :loading="actionRunning"
          :disabled="!canPublishAction"
          @click="onPublish"
        >
          确认发布
        </ElButton>
      </template>
    </ElDialog>

    <!-- ===== Retire Dialog ===== -->
    <ElDialog
      v-model="retireDialog"
      title="退役已发布规则集"
      width="480px"
      :close-on-click-modal="false"
    >
      <ElForm label-width="100px" size="default">
        <ElFormItem label="规则集">
          <span class="d-val">{{ detailNN?.summary.ruleSetCode }}</span>
        </ElFormItem>
        <ElFormItem label="退役版本">v{{ detailNN?.summary.currentVersion }}</ElFormItem>
        <ElFormItem label="退役原因 (必填)">
          <ElInput
            v-model="retireForm.reason"
            type="textarea"
            :rows="3"
            placeholder="必须填写；将出现在审计记录"
          />
        </ElFormItem>
        <ElAlert type="error" :closable="false" show-icon class="d-alert">
          <template #title>退役后不可恢复</template>
          退役后规则集进入只读状态；如需恢复，需基于此版本重新创建草稿。
        </ElAlert>
      </ElForm>
      <template #footer>
        <ElButton @click="retireDialog = false">取消</ElButton>
        <ElButton
          type="danger"
          :loading="actionRunning"
          :disabled="!canPublishAction"
          @click="onRetire"
        >
          确认退役
        </ElButton>
      </template>
    </ElDialog>

    <!-- ===== [+ 新建草稿] Dialog（Step 3） ===== -->
    <ElDialog
      v-model="createDraftDialog"
      title="新建参数集草稿（基于当前规则集）"
      width="520px"
      :close-on-click-modal="false"
    >
      <ElForm label-width="100px" size="default">
        <ElFormItem label="规则集">
          <span class="d-val"
            >{{ detailNN?.summary.ruleSetCode }} v{{ detailNN?.summary.currentVersion }}</span
          >
        </ElFormItem>
        <ElFormItem label="新版本号">
          <ElInput v-model="createDraftForm.newVersionCode" placeholder="如 v13" />
        </ElFormItem>
        <ElFormItem label="变更原因 (必填)">
          <ElInput
            v-model="createDraftForm.changeReason"
            type="textarea"
            :rows="3"
            placeholder="必须填写；将出现在审计记录"
          />
        </ElFormItem>
        <ElAlert
          :type="forkPermission.allowed ? 'info' : 'warning'"
          :closable="false"
          show-icon
          class="d-alert"
        >
          <template #title>
            {{
              forkPermission.allowed
                ? '分支（fork）产生新的规则集草稿'
                : (forkPermission.reason ?? '当前状态不允许 fork')
            }}
          </template>
          新草稿与当前规则集一一对应（规则集版本与参数集版本同时创建）。
          提交后状态为草稿，可进入参数维护页签编辑。
        </ElAlert>
      </ElForm>
      <template #footer>
        <ElButton @click="createDraftDialog = false">取消</ElButton>
        <ElButton
          type="primary"
          :loading="actionRunning"
          :disabled="!forkPermission.allowed"
          @click="onCreateDraft"
        >
          确认 fork
        </ElButton>
      </template>
    </ElDialog>

    <!-- ===== [保存草稿] Dialog（Step 4） ===== -->
    <ElDialog
      v-model="saveDraftDialog"
      title="保存参数集草稿"
      width="480px"
      :close-on-click-modal="false"
    >
      <ElForm label-width="100px" size="default">
        <ElFormItem label="参数集">
          <span class="d-val">{{ parameterSetBuffer.governance?.versionCode }}</span>
        </ElFormItem>
        <ElFormItem label="变更原因 (必填)">
          <ElInput
            v-model="saveDraftForm.changeReason"
            type="textarea"
            :rows="3"
            placeholder="必须填写；将出现在审计记录"
          />
        </ElFormItem>
        <ElAlert type="warning" :closable="false" show-icon class="d-alert">
          <template #title>草稿保存说明</template>
          保存会以整份草稿覆盖当前版本内容，不保留逐项修改历史。 若规则集与参数集都有未保存修改，
          会按「先参数集、后规则集」的顺序一并提交。
        </ElAlert>
      </ElForm>
      <template #footer>
        <ElButton @click="saveDraftDialog = false">取消</ElButton>
        <ElButton
          type="primary"
          :loading="parameterSetBuffer.saving || parameterSetBuffer.saving"
          @click="onSaveDraftConfirm"
        >
          确认保存
        </ElButton>
      </template>
    </ElDialog>
  </div>
</template>

<style lang="less" scoped>
.aps-rules {
  display: flex;
  min-height: calc(100vh - 100px);
  padding: 16px;
  background: #f5f7fa;
  flex-direction: column;
  gap: 16px;
}

.page-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-end;

  .page-title {
    margin: 0;
    font-size: 20px;
    font-weight: 600;
    color: #1e293b;
  }

  .page-sub {
    margin: 4px 0 0;
    font-size: 12px;
    color: #94a3b8;
  }

  .header-actions {
    display: flex;
    gap: 8px;
    align-items: center;
  }
}

.hint-bar {
  :deep(.el-alert__title) {
    font-weight: 600;
  }

  .hint-text {
    margin-left: 8px;
    font-size: 13px;
    line-height: 1.8;
    color: #475569;
  }
}

.panel {
  border-radius: 8px;
}

.panel-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-size: 14px;
  font-weight: 600;
  color: #1e293b;
}

.panel-empty {
  padding: 20px 0;
}

.muted {
  color: #94a3b8;
}

.muted-tag {
  margin-left: 8px;
  font-size: 12px;
  font-weight: 400;
  color: #94a3b8;
}

.msg-line {
  margin-left: 8px;
  font-size: 12px;
  color: #64748b;
}

.code-tag {
  padding: 2px 6px;
  font-family: 'Courier New', monospace;
  font-size: 12px;
  color: #475569;
  background: #f1f5f9;
  border-radius: 3px;
}

.loading-tip {
  padding: 30px;
  font-size: 14px;
  color: #94a3b8;
  text-align: center;
}

/* KPI 条 */
.kpi-strip {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
}

.kpi-pill {
  display: inline-flex;
  padding: 6px 14px;
  font-size: 12px;
  background: #f8fafc;
  border-radius: 16px;
  align-items: center;
  gap: 6px;

  &.warn {
    background: #fef3c7;
  }

  &.ok {
    background: #d1fae5;
  }

  &.muted {
    background: #f1f5f9;
  }

  .pill-label {
    color: #64748b;
  }

  .pill-val {
    font-weight: 600;
    color: #1e293b;
  }
}

/* 过滤行 */
.filter-row {
  padding: 8px 12px;
  background: #fff;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
}

/* 主内容 */
.content-row {
  display: grid;
  grid-template-columns: 380px 1fr;
  gap: 16px;
}

/* 左：规则集列表 */
.ruleset-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
  max-height: 720px;
  overflow-y: auto;
}

.ruleset-card {
  padding: 12px 14px;
  cursor: pointer;
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-left: 3px solid #94a3b8;
  border-radius: 4px;
  transition: all 0.2s;

  &:hover {
    border-color: #3b82f6;
  }

  &.active {
    background: #eff6ff;
    border-color: #3b82f6;
    box-shadow: 0 0 0 2px rgb(59 130 246 / 12%);
  }

  &.draft {
    border-left-color: #f59e0b;
  }

  &.published {
    border-left-color: #10b981;
  }

  &.disabled {
    border-left-color: #94a3b8;
  }

  .rs-head {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 6px;

    .rs-code {
      font-family: 'Courier New', monospace;
      font-weight: 600;
      color: #1e293b;
    }
  }

  .rs-name {
    margin-bottom: 8px;
    font-size: 13px;
    color: #334155;
  }

  .rs-meta {
    display: flex;
    margin-bottom: 4px;
    font-size: 12px;
    gap: 6px;
    align-items: center;

    .meta-key {
      min-width: 60px;
      color: #94a3b8;
    }

    .meta-val {
      font-weight: 600;
      color: #1e293b;
    }

    .meta-draft {
      margin-left: 4px;
      font-weight: 600;
      color: #f59e0b;
    }
  }

  .rs-reason {
    padding-top: 6px;
    margin-top: 6px;
    font-size: 11px;
    line-height: 1.4;
    color: #64748b;
    border-top: 1px dashed #e2e8f0;
  }
}

/* 右：详情 */
.detail-head {
  align-items: center;

  .head-actions {
    display: flex;
    gap: 8px;
  }
}

.detail-head-row {
  display: flex;
  padding: 14px 18px;
  margin: -1px -1px 12px;
  font-size: 14px;
  font-weight: 600;
  color: #1e293b;
  background: #f8fafc;
  border-bottom: 1px solid #e2e8f0;
  border-radius: 4px 4px 0 0;
  justify-content: space-between;
  align-items: center;

  .head-actions {
    display: flex;
    gap: 8px;
  }
}

.detail-tabs {
  margin-top: 12px;
}

.tab-alert {
  margin-bottom: 12px;
}
.strategy-tab-head {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 12px;
  flex-wrap: wrap;
}
.strategy-tab-head .tab-alert {
  flex: 1 1 auto;
  margin-bottom: 0;
}

.u22-alert {
  margin-top: 12px;
}

/* Parameter 敏感值 */
.sensitive {
  padding: 2px 8px;
  font-weight: 600;
  letter-spacing: 2px;
  color: #92400e;
  background: #fef3c7;
  border-radius: 4px;
}

/* 优先级段位「段内第 N 位」顺序圆 */
.order-circle {
  display: inline-flex;
  width: 26px;
  height: 26px;
  font-size: 12px;
  font-weight: 600;
  color: #fff;
  background: #3b82f6;
  border-radius: 50%;
  align-items: center;
  justify-content: center;
}

/* Strategy JSON */
.json-block {
  max-height: 200px;
  padding: 10px 12px;
  margin: 0;
  overflow: auto;
  font-family: 'Courier New', monospace;
  font-size: 12px;
  color: #e2e8f0;
  background: #1e293b;
  border-radius: 4px;
}

/* Diff */
.diff-summary {
  display: flex;
  gap: 8px;
  align-items: center;
  margin-bottom: 12px;

  .diff-meta {
    margin-left: 8px;
    font-family: 'Courier New', monospace;
    font-size: 12px;
    color: #64748b;
  }
}

.diff-old {
  color: #94a3b8;
  text-decoration: line-through;
}

.diff-new {
  font-weight: 600;
  color: #059669;
}

/* History */
.history-current {
  font-weight: 700;
  color: #059669;
}

/* 结果卡 */
.result-card {
  margin-top: 12px;

  &.publish-card {
    background: #f0fdf4;
    border: 1px solid #86efac;
  }

  &.retire-card {
    background: #fef2f2;
    border: 1px solid #fca5a5;
  }
}

/* Dialog */
.d-val {
  font-family: 'Courier New', monospace;
  color: #1e293b;
}

.d-alert {
  margin-bottom: -10px;
}

/* 参数维护 Tab — 5 主题 JSON 块区隔（B 设计稿 Step 4） */
.param-block-section {
  margin-bottom: 20px;
  padding: 12px 14px;
  background: #fff;
  border: 1px solid #e2e8f0;
  border-radius: 6px;

  .block-header {
    display: flex;
    align-items: center;
    gap: 12px;
    margin-bottom: 10px;
    padding-bottom: 8px;
    border-bottom: 1px dashed #e2e8f0;

    .block-title {
      margin: 0;
      font-size: 14px;
      font-weight: 600;
      color: #1e293b;
    }
  }

  .readonly-val {
    color: #64748b;
    font-family: 'Courier New', monospace;
  }
}

.save-bar {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-top: 16px;
  padding: 10px 14px;
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
}
</style>
