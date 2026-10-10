<script setup lang="ts">
/**
 * APS V1 4号位 — 异常与原因解释（页面 6 / U09 / U14 / U20）
 *
 * 4号位文档第 11-12 节约束：
 *  - 根因优先显示，不只 DUE_DATE_RISK（U09）
 *  - 设备故障：只提示影响/建议，不直接改 Task 状态（U20 / 4号位文档第 12 节；V1 不建设 PAUSE/RESUME 状态闭环）
 *  - 跨 Domain 共享设备阻挡原因清晰可见（U14）
 *  - 阶段 A 阶段所有数据来自 mock，后端就绪后切换真实 API
 *
 * 写接口：本页面只读，0 个写调用（4号位文档第 2.1 节）
 *
 * 页面分区：
 *  - 顶部 4 个严重度 KPI（INFO / WARNING / ERROR / CRITICAL）
 *  - 排程解释事实（按 objectType 分组）
 *  - 业务事实问题
 *  - 重排建议（含 estimatedImpact）
 *  - 设备故障（不直接改 Task 状态）
 *  - Domain 失败
 */

import { computed, onMounted } from 'vue'
import dayjs from 'dayjs'
import { storeToRefs } from 'pinia'
import { useExplanationStore } from '@/store/modules/aps/explanation'
import { useScheduleStore } from '@/store/modules/aps/schedule'
import { useDomainStore } from '@/store/modules/aps/domain'

import {
  ElAlert,
  ElButton,
  ElCard,
  ElEmpty,
  ElTag,
  ElTable,
  ElTableColumn,
  ElTooltip
} from 'element-plus'

const explanationStore = useExplanationStore()
const scheduleStore = useScheduleStore()
const { data, loading, error, severityCounts, factsByObjectType } = storeToRefs(explanationStore)
const { versions, currentVersionId, ganttData } = storeToRefs(scheduleStore)

const domainStore = useDomainStore()
function humanLabel(domainKey: string | undefined | null): string {
  return domainStore.label(domainKey)
}

/* ===== 严重度与颜色 ===== */
const SEVERITY_TAG: Record<string, 'info' | 'warning' | 'danger' | undefined> = {
  INFO: 'info',
  WARNING: 'warning',
  ERROR: 'danger',
  CRITICAL: 'danger'
}
const SEVERITY_LABEL: Record<string, string> = {
  INFO: '提示',
  WARNING: '警告',
  ERROR: '错误',
  CRITICAL: '严重'
}

/* ===== objectType 中文映射 ===== */
const OBJ_TYPE_LABEL: Record<string, string> = {
  TASK: '任务',
  ORDER: '订单',
  PI: 'PI 在制',
  PO: 'PO 采购',
  RESOURCE: '资源',
  STAGE: '工段'
}

/* ===== issueType 中文映射 ===== */
const ISSUE_TYPE_LABEL: Record<string, string> = {
  EQUIPMENT_FAILURE: '设备故障',
  MATERIAL_AVAILABILITY: '物料供应',
  PI_POSITION_MISMATCH: 'PI Position 不匹配',
  CONTRACT_VIOLATION: '契约违反',
  CAPACITY_SHORTAGE: '产能不足',
  OTHER: '其他'
}

/* ===== recommendationType 中文映射 ===== */
const REC_TYPE_LABEL: Record<string, string> = {
  MANUAL_RESCHEDULE: '手工重排',
  CANDIDATE_GENERATION: '生成候选版本',
  NO_ACTION_REQUIRED: '无需操作'
}
const REC_TAG_TYPE: Record<string, 'success' | 'warning' | 'info'> = {
  MANUAL_RESCHEDULE: 'warning',
  CANDIDATE_GENERATION: 'primary' as any,
  NO_ACTION_REQUIRED: 'success'
}

/* ===== failureType 中文映射 ===== */
const FAIL_TYPE_LABEL: Record<string, string> = {
  DOMAIN_FAILED: '排程域失败',
  BLOCKED_BY_UPSTREAM: '上游阻断',
  ENGINE_ERROR: '引擎错误'
}

/* ===== issueCategory 中文 ===== */
const CAT_LABEL: Record<string, string> = {
  DATA_ISSUE: '数据问题',
  SCHEDULING_ISSUE: '排程问题'
}

/* ===== 顶部 KPI ===== */
const totalFacts = computed(
  () =>
    severityCounts.value.CRITICAL +
    severityCounts.value.ERROR +
    severityCounts.value.WARNING +
    severityCounts.value.INFO
)

const kpiItems = computed(() => [
  { key: 'CRITICAL', label: '严重', count: severityCounts.value.CRITICAL, color: '#7c2d12' },
  { key: 'ERROR', label: '错误', count: severityCounts.value.ERROR, color: '#ef4444' },
  { key: 'WARNING', label: '警告', count: severityCounts.value.WARNING, color: '#f59e0b' },
  { key: 'INFO', label: '提示', count: severityCounts.value.INFO, color: '#3b82f6' }
])

/* ===== 加载入口 ===== */
function refresh(): void {
  explanationStore.load()
}

function selectVersion(id: number): void {
  if (id === currentVersionId.value) return
  scheduleStore.selectVersion(id)
  explanationStore.load(id)
}

onMounted(async () => {
  if (versions.value.length === 0) await scheduleStore.loadVersions()
  await explanationStore.load()
})
</script>

<template>
  <div class="aps-explanation">
    <!-- ===== 顶部 ===== -->
    <div class="page-header">
      <div>
        <h2 class="page-title">异常与原因解释</h2>
        <p class="page-sub">
          根因优先展示；设备故障只提示影响与建议，不直接改动任务状态；跨域阻挡原因可见
        </p>
      </div>
      <ElButton :loading="loading" @click="refresh"> <Icon icon="vi-ep:refresh" /> 刷新 </ElButton>
    </div>

    <!-- ===== 错误条 ===== -->
    <ElAlert v-if="error" type="error" :closable="false" show-icon :title="`加载失败：${error}`" />

    <!-- ===== 版本切换 + 全局 KPI ===== -->
    <div class="version-bar">
      <span class="bar-label">当前版本：</span>
      <ElTag v-if="currentVersionId" type="primary" effect="dark">
        {{ versions.find((v) => v.id === currentVersionId)?.versionCode || currentVersionId }}
        <span v-if="ganttData?.versionCode" class="muted-tag"> ({{ ganttData.versionCode }}) </span>
      </ElTag>
      <ElButton
        v-for="v in versions.slice(0, 3)"
        :key="v.id"
        size="small"
        :type="v.id === currentVersionId ? 'primary' : 'default'"
        @click="selectVersion(v.id)"
      >
        {{ v.versionCode }}
      </ElButton>
    </div>

    <!-- ===== KPI 卡片 ===== -->
    <div class="kpi-row">
      <div
        v-for="item in kpiItems"
        :key="item.key"
        class="kpi-card"
        :style="{ borderTopColor: item.color }"
      >
        <div class="kpi-label">{{ item.label }}</div>
        <div class="kpi-value" :style="{ color: item.color }">{{ item.count }}</div>
        <div class="kpi-ratio">
          {{ totalFacts > 0 ? Math.round((item.count / totalFacts) * 100) : 0 }}%
        </div>
      </div>
      <div class="kpi-card total">
        <div class="kpi-label">解释事实合计</div>
        <div class="kpi-value">{{ totalFacts }}</div>
        <div class="kpi-ratio">4 类汇总</div>
      </div>
    </div>

    <!-- ===== 排程解释事实（按 objectType 分组） ===== -->
    <ElCard class="panel">
      <template #header>
        <div class="panel-header">
          <span> 排程解释事实 </span>
          <span class="hint-text">根因优先（不只交期风险）</span>
        </div>
      </template>
      <div v-if="!data?.scheduleExplanationFacts?.length" class="panel-empty">
        <ElEmpty description="暂无解释事实" />
      </div>
      <div v-else class="fact-grid">
        <div
          v-for="(items, objType) in factsByObjectType"
          v-show="items.length > 0"
          :key="objType"
          class="fact-group"
        >
          <div class="group-head">
            <span class="group-name">{{ OBJ_TYPE_LABEL[objType] }}</span>
            <ElTag size="small" effect="plain">{{ items.length }} 项</ElTag>
          </div>
          <div class="fact-list">
            <div
              v-for="f in items"
              :key="f.factId"
              class="fact-item"
              :class="{ critical: f.severity === 'CRITICAL' }"
            >
              <div class="fact-head">
                <ElTag size="small" :type="SEVERITY_TAG[f.severity]">
                  {{ SEVERITY_LABEL[f.severity] }}
                </ElTag>
                <span class="fact-ref">{{ f.objectRef }}</span>
                <ElTag size="small" effect="plain">{{ f.factType }}</ElTag>
                <ElTag size="small" type="info" effect="plain">
                  {{ CAT_LABEL[f.issueCategory] }}
                </ElTag>
              </div>
              <div class="fact-cause">{{ f.rootCause }}</div>
              <div class="fact-time">
                {{ dayjs(f.occurredAt).format('YYYY-MM-DD HH:mm') }}
              </div>
            </div>
          </div>
        </div>
      </div>
    </ElCard>

    <!-- ===== 业务事实问题 + 重排建议（两列） ===== -->
    <div class="content-row">
      <!-- 业务事实问题 -->
      <ElCard class="panel">
        <template #header>
          <div class="panel-header">
            <span> 业务事实问题 </span>
            <ElTag v-if="data?.businessFactIssues?.length" type="danger" size="small">
              {{ data.businessFactIssues.length }} 项
            </ElTag>
          </div>
        </template>
        <div v-if="!data?.businessFactIssues?.length" class="panel-empty">
          <ElEmpty description="暂无业务问题" />
        </div>
        <ElTable v-else :data="data.businessFactIssues" size="small" border>
          <ElTableColumn label="排程域" width="120">
            <template #default="{ row }">
              <ElTag size="small" effect="plain">{{ humanLabel(row.domainKey) }}</ElTag>
            </template>
          </ElTableColumn>
          <ElTableColumn label="类型" width="120">
            <template #default="{ row }">
              {{ ISSUE_TYPE_LABEL[row.issueType] || row.issueType }}
            </template>
          </ElTableColumn>
          <ElTableColumn prop="message" label="问题描述" min-width="200" />
          <ElTableColumn prop="relatedObjectRef" label="关联对象" width="120" />
          <ElTableColumn label="发生时间" width="140">
            <template #default="{ row }">
              {{ dayjs(row.occurredAt).format('MM-DD HH:mm') }}
            </template>
          </ElTableColumn>
        </ElTable>
      </ElCard>

      <!-- 重排建议 -->
      <ElCard class="panel">
        <template #header>
          <div class="panel-header">
            <span>重排建议</span>
            <span class="hint-text">仅展示，不自动触发</span>
          </div>
        </template>
        <div v-if="!data?.rescheduleRecommendations?.length" class="panel-empty">
          <ElEmpty description="暂无建议" />
        </div>
        <div v-else class="rec-list">
          <div
            v-for="r in data.rescheduleRecommendations"
            :key="r.recommendationId"
            class="rec-item"
            :class="{ warn: r.estimatedImpact.mayIntroduceNewDelay }"
          >
            <div class="rec-head">
              <ElTag :type="REC_TAG_TYPE[r.recommendationType]" size="small">
                {{ REC_TYPE_LABEL[r.recommendationType] }}
              </ElTag>
              <span class="rec-time">
                {{ dayjs(r.generatedAt).format('YYYY-MM-DD HH:mm') }}
              </span>
            </div>
            <div class="rec-desc">{{ r.description }}</div>
            <div class="rec-impact">
              <span
                >影响范围：{{ r.estimatedImpact.orderCount }} 单 /
                {{ r.estimatedImpact.taskCount }} task</span
              >
              <ElTooltip
                v-if="r.estimatedImpact.mayIntroduceNewDelay"
                content="重排可能引入新的延期，需要 PMC 评估"
                placement="top"
              >
                <span class="rec-warn"> <Icon icon="vi-mdi:alert" /> 可能引入新延期 </span>
              </ElTooltip>
            </div>
          </div>
        </div>
      </ElCard>
    </div>

    <!-- ===== 设备故障 + Domain 失败（两列） ===== -->
    <div class="content-row">
      <!-- 设备故障 -->
      <ElCard class="panel">
        <template #header>
          <div class="panel-header">
            <span> 设备故障 </span>
            <span class="hint-text">只提示影响/建议，不直接改任务状态</span>
          </div>
        </template>
        <ElAlert
          type="warning"
          :closable="false"
          show-icon
          class="u20-alert"
          title="设备故障仅展示影响评估与建议，重排决策由 PMC 在阶段 B/D 发起 LOCAL_RESCHEDULE / MANUAL_RESCHEDULE"
        />
        <div v-if="!data?.equipmentFailures?.length" class="panel-empty">
          <ElEmpty description="当前无设备故障" />
        </div>
        <div v-else class="equip-list">
          <div v-for="(eq, idx) in data.equipmentFailures" :key="idx" class="equip-item">
            <div class="equip-head">
              <Icon class="equip-icon" icon="vi-mdi:wrench" />
              <span class="equip-code">{{ eq.equipmentCode }}</span>
              <ElTag size="small" type="danger" effect="light">
                影响 {{ eq.impactedOrderCount }} 单
              </ElTag>
            </div>
            <div class="equip-meta">
              <span>停机开始：{{ dayjs(eq.downFrom).format('YYYY-MM-DD HH:mm') }}</span>
              <span v-if="eq.downTo">
                预计恢复：{{ dayjs(eq.downTo).format('YYYY-MM-DD HH:mm') }}
              </span>
              <span v-else class="muted-tag">预计恢复：未确定</span>
            </div>
            <div class="equip-field">
              <span class="equip-label">影响评估</span>
              <span>{{ eq.impactAssessment }}</span>
            </div>
            <div class="equip-field">
              <span class="equip-label">建议</span>
              <span>{{ eq.recommendation }}</span>
            </div>
          </div>
        </div>
      </ElCard>

      <!-- Domain 失败 -->
      <ElCard class="panel">
        <template #header>
          <div class="panel-header">
            <span>排程域失败</span>
            <ElTag v-if="data?.failures?.length" type="danger" size="small">
              {{ data.failures.length }} 项
            </ElTag>
          </div>
        </template>
        <div v-if="!data?.failures?.length" class="panel-empty">
          <ElEmpty description="当前无排程域失败" />
        </div>
        <ElTable v-else :data="data.failures" size="small" border>
          <ElTableColumn label="排程域" width="120">
            <template #default="{ row }">
              <ElTag size="small" effect="plain">{{ humanLabel(row.domainKey) }}</ElTag>
            </template>
          </ElTableColumn>
          <ElTableColumn label="失败类型" width="130">
            <template #default="{ row }">
              <ElTag size="small" type="danger" effect="light">
                {{ FAIL_TYPE_LABEL[row.failureType] || row.failureType }}
              </ElTag>
            </template>
          </ElTableColumn>
          <ElTableColumn prop="errorMessage" label="错误信息" min-width="220" />
          <ElTableColumn label="可重试" width="80" align="center">
            <template #default="{ row }">
              <ElTag size="small" :type="row.retryable ? 'warning' : 'info'">
                {{ row.retryable ? '是' : '否' }}
              </ElTag>
            </template>
          </ElTableColumn>
          <ElTableColumn label="失败时间" width="140">
            <template #default="{ row }">
              {{ dayjs(row.failedAt).format('MM-DD HH:mm') }}
            </template>
          </ElTableColumn>
        </ElTable>
      </ElCard>
    </div>
  </div>
</template>

<style lang="less" scoped>
.aps-explanation {
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
}

.version-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  padding: 8px 12px;
  background: #fff;
  border-radius: 8px;
  box-shadow: 0 1px 4px rgb(0 0 0 / 6%);

  .bar-label {
    margin-right: 4px;
    font-size: 12px;
    color: #64748b;
  }

  .muted-tag {
    margin-left: 4px;
    font-size: 11px;
    color: #cbd5e1;
  }
}

.kpi-row {
  display: grid;
  grid-template-columns: repeat(5, 1fr);
  gap: 12px;
}

.kpi-card {
  padding: 14px 16px;
  background: #fff;
  border-top: 3px solid #94a3b8;
  border-radius: 8px;
  box-shadow: 0 1px 4px rgb(0 0 0 / 6%);

  .kpi-label {
    font-size: 12px;
    color: #94a3b8;
  }

  .kpi-value {
    margin-top: 4px;
    font-size: 22px;
    font-weight: 600;
    color: #1e293b;
  }

  .kpi-ratio {
    margin-top: 2px;
    font-size: 11px;
    color: #cbd5e1;
  }

  &.total {
    background: #f1f5f9;

    .kpi-value {
      color: #475569;
    }
  }
}

.content-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
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

  .hint-text {
    font-size: 12px;
    font-weight: 400;
    color: #94a3b8;
  }
}

.panel-empty {
  padding: 20px 0;
}

/* 排程解释事实网格 */
.fact-grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 14px;
}

.fact-group {
  padding: 10px 12px;
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 6px;

  .group-head {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 8px;

    .group-name {
      font-size: 13px;
      font-weight: 600;
      color: #1e293b;
    }
  }
}

.fact-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.fact-item {
  padding: 8px 10px;
  background: #fff;
  border-left: 3px solid #f59e0b;
  border-radius: 4px;

  &.critical {
    background: #fef2f2;
    border-left-color: #7c2d12;
  }

  .fact-head {
    display: flex;
    align-items: center;
    gap: 6px;
    flex-wrap: wrap;
  }

  .fact-ref {
    font-family: 'Courier New', monospace;
    font-size: 12px;
    font-weight: 600;
    color: #1e293b;
  }

  .fact-cause {
    margin-top: 6px;
    font-size: 13px;
    line-height: 1.5;
    color: #475569;
  }

  .fact-time {
    margin-top: 4px;
    font-size: 11px;
    color: #94a3b8;
  }
}

/* 重排建议 */
.rec-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.rec-item {
  padding: 10px 12px;
  background: #f0f9ff;
  border-left: 3px solid #3b82f6;
  border-radius: 4px;

  &.warn {
    background: #fef3c7;
    border-left-color: #f59e0b;
  }

  .rec-head {
    display: flex;
    justify-content: space-between;
    align-items: center;
  }

  .rec-time {
    font-size: 11px;
    color: #94a3b8;
  }

  .rec-desc {
    margin: 6px 0;
    font-size: 13px;
    color: #1e293b;
  }

  .rec-impact {
    display: flex;
    justify-content: space-between;
    font-size: 12px;
    color: #64748b;
  }

  .rec-warn {
    display: inline-flex;
    font-weight: 600;
    color: #b45309;
    align-items: center;
    gap: 2px;
  }
}

/* 设备故障 */
.u20-alert {
  margin-bottom: 12px;
}

.equip-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.equip-item {
  padding: 12px 14px;
  background: #fef2f2;
  border: 1px solid #fecaca;
  border-radius: 6px;

  .equip-head {
    display: flex;
    align-items: center;
    gap: 10px;
    margin-bottom: 8px;
  }

  .equip-icon {
    font-size: 18px;
    color: #ef4444;
  }

  .equip-code {
    font-family: 'Courier New', monospace;
    font-weight: 600;
    color: #1e293b;
  }

  .equip-meta {
    display: flex;
    margin-bottom: 8px;
    font-size: 12px;
    color: #64748b;
    gap: 16px;
  }

  .equip-field {
    display: flex;
    margin: 4px 0;
    font-size: 13px;
    gap: 6px;

    .equip-label {
      flex-shrink: 0;
      width: 70px;
      color: #94a3b8;
    }
  }

  .muted-tag {
    color: #cbd5e1;
  }
}
</style>
