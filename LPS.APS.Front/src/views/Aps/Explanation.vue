<script setup lang="ts">
/**
 * APS V1 4号位 — 异常与原因解释（页面 6 / U09 / U14 / U20）
 *
 * 4号位文档第 11-12 节约束：
 *  - 根因优先显示，不只 DUE_DATE_RISK（U09）
 *  - 设备故障：只提示影响/建议，不直接改 Task 状态（U20 / 4号位文档第 12 节；V1 不建设 PAUSE/RESUME 状态闭环）
 *  - 跨 Domain 共享设备阻挡原因清晰可见（U14）
 *
 * 写接口：本页面只读，0 个写调用（4号位文档第 2.1 节）
 *
 * 页面分区：
 *  - 顶部 3 个严重度 KPI（ERROR / WARN / INFO，v5.1.10 §八.1 :4864 3 码权威）
 *  - 排程解释事实（按 objectType 分组，5 码：ORDER/TASK/RESOURCE/STAGE/DOMAIN）
 *  - 业务事实问题（@see BusinessFactIssueDto）
 *  - 重排建议（含 estimatedImpact）
 *  - 设备故障（不直接改 Task 状态）
 *
 * 2026-10-09 改造（按 5号位 函件 R1 回执）：
 *  - R1 ReasonCode 字典对齐 v5.1.10 §八.1 15 码全集（@see types/explanation.ts REASON_CODES）
 *  - FROZEN_ZONE_LOCK：15 码唯一锁码；FIRM/FROZEN/其它执行锁统一并入；细分走 EvidenceJson.evidenceType = LOCK
 *  - 撤 5号位 关键纠正 3 占位面板（后端无对应实体）改用 Summary 端点的 errorCount / warnCount / infoCount 三计数 + reasonCodeCounts 分布
 *  - 撤 BusinessFactIssue.domainKey 4号位 占位字段（对齐 lps/LPS.APS.Core/Dto/BusinessFactIssueDto.cs 14 字段）
 *  - 修 planVersionId 必填 400 bug（store 调 getByPlanVersion 透传）
 *
 * R2 关键路径 / R3 failureSeverity 跨号位（1号位 求解 + 2号位 落盘 + 0号位 裁决 V1 范围）—— 前端不动
 *  - verify [E] 段新增 E11/E12 skip 占位
 *
 * 2026-10-10 改造（按 10-10 撤函回检发现的真问题）：
 *  - 业务事实问题分页 + total（@see 函件 frontNew/docs/4号位-2026-10-10-business-fact-issues分页severity排序total-给5号位.md）
 *  - store 层缓存：版本切换保留，仅"刷新"按钮失效（数据按设计跨版本）
 *  - view 层用 sortedBusinessFactIssues：severity desc + occurredAt desc 兜底排序（即便后端未落地 ORDER BY severity，关键 ERROR 在前）
 *  - ElCard body 末尾增分页区（10-10 首版：pageSize ElSelect）
 *
 * 2026-10-10 续改：标准 ElPagination 分页
 *  - store fetchBizPage(page, size) 替换式（不累积）；ElPagination v-model 双绑 page / pageSize
 *  - @current-change → store.fetchBizPage；@size-change → store.changeBizPageSize（自回第 1 页）
 *  - ElPagination layout="total, sizes, prev, pager, next, jumper" 一次给齐
 *  - view ElCard header 移除 "显示 N 条 / 共 M 条" 双 tag（与 ElPagination 总数显示重复）
 *  - verify E23 静态锁标准 ElPagination 契约
 */

import { computed, onMounted } from 'vue'
import dayjs from 'dayjs'
import { storeToRefs } from 'pinia'
import { useExplanationStore } from '@/store/modules/aps/explanation'
import { useScheduleStore } from '@/store/modules/aps/schedule'

import {
  ElAlert,
  ElButton,
  ElCard,
  ElEmpty,
  ElPagination,
  ElTag,
  ElTable,
  ElTableColumn
} from 'element-plus'
import {
  EXPLANATION_SEVERITY_LABELS,
  EXPLANATION_SEVERITY_TAG,
  OBJECT_TYPE_LABELS,
  REASON_CODE_LABELS
} from '@/api/aps-v1/types/explanation'

const explanationStore = useExplanationStore()
const scheduleStore = useScheduleStore()
const {
  data,
  loading,
  error,
  severityCounts,
  totalFacts,
  factsByObjectType,
  businessFactIssues,
  businessFactIssuesTotal,
  businessFactIssuesPage,
  businessFactIssuesPageSize,
  businessFactIssuesLoading,
  sortedBusinessFactIssues
} = storeToRefs(explanationStore)
const { versions, currentVersionId, displayedVersions } = storeToRefs(scheduleStore)
// actions 直接解构（Pinia 文档：storeToRefs 只包 state/getters；actions 直接解构）
const { fetchBizPage, refreshBizIssues, changeBizPageSize } = explanationStore

/* ===== 顶部 KPI（3 码：ERROR / WARN / INFO） ===== */
const kpiItems = computed(() => [
  { key: 'ERROR', label: '错误', count: severityCounts.value.ERROR, color: '#ef4444' },
  { key: 'WARN', label: '警告', count: severityCounts.value.WARN, color: '#f59e0b' },
  { key: 'INFO', label: '提示', count: severityCounts.value.INFO, color: '#3b82f6' }
])

/* ===== ReasonCode 字典兜底（15 码未命中时原样展示） ===== */
const reasonLabel = (code: string): string =>
  REASON_CODE_LABELS[code as keyof typeof REASON_CODE_LABELS] ?? code

/* ===== BusinessFactIssue 类型中文映射（4号位 业务抽象，非冻结 15 码） =====
 *  - 与 ScheduleExplanationFact.ReasonCode 区分（后者 = v5.1.10 15 码权威枚举）
 *  - BusinessFactIssueDto.IssueType 是 string（lps/LPS.APS.Core/Dto/BusinessFactIssueDto.cs:19），无后端枚举约束
 *  - 当前 4号位 业务层抽象 6 分类保持不变
 */
const BIZ_ISSUE_TYPE_LABEL: Record<string, string> = {
  EQUIPMENT_FAILURE: '设备故障',
  MATERIAL_AVAILABILITY: '物料供应',
  PI_POSITION_MISMATCH: 'PI Position 不匹配',
  CONTRACT_VIOLATION: '契约违反',
  CAPACITY_SHORTAGE: '产能不足',
  OTHER: '其他'
}

/* ===== BusinessFactIssue Source 中文映射（对齐 5号位 DTO Source 字段） ===== */
const BIZ_SOURCE_LABEL: Record<string, string> = {
  BOM_WORKSET: 'BOM 维护',
  MATERIAL_STAGE_CONTEXT: '物料工段上下文',
  PI_POSITION: 'PI Position',
  MANUAL_ETA: '手工 ETA'
}

/* ===== ReviewStatus 中文映射 ===== */
const REVIEW_STATUS_LABEL: Record<string, string> = {
  PENDING: '待复核',
  CONFIRMED: '已确认',
  IGNORED: '已忽略',
  FIXED: '已修复'
}
const REVIEW_STATUS_TAG: Record<string, 'warning' | 'success' | 'info' | 'primary'> = {
  PENDING: 'warning',
  CONFIRMED: 'success',
  IGNORED: 'info',
  FIXED: 'primary'
}

/* ===== 加载入口 ===== */
function refresh(): void {
  explanationStore.load()
  // 顶部刷新按钮同时 invalidate business-fact-issues 缓存（@see store load() 缓存语义）
  void refreshBizIssues()
}

function selectVersion(id: number): void {
  if (id === currentVersionId.value) return
  // 10-10 懒加载：Explanation 页只用 scheduleStore 的 currentVersionId 高亮，不需 gantt 数据
  // 改用 setCurrentVersion(id)（仅 ref 更新），不再拖一份甘特图
  // Schedule / Gantt 页仍用 selectVersion(id)（更新 + fetch）
  scheduleStore.setCurrentVersion(id)
  explanationStore.load(id)
  // 版本切换保留 biz-issues 缓存（数据按设计跨版本，@see BusinessFactIssueController.cs 无 planVersionId 参数）
  // 不调用 refreshBizIssues，避免重复请求相同数据
}

/** 业务事实问题 pageSize 变更（10-10 增） */
function onBizSizeChange(size: number): void {
  void changeBizPageSize(size)
}

/** 业务事实问题分页跳转（10-10 续改：标准 ElPagination）
 *  - 用 v-model:current-page 双向绑定后，store businessFactIssuesPage 已被 ElPagination 同步更新
 *  - 这里仅触发拉取；fetchBizPage 入参沿用 store 当前值以避免竞态
 */
function onBizPageChange(p: number): void {
  void fetchBizPage(p, businessFactIssuesPageSize.value)
}

onMounted(async () => {
  if (versions.value.length === 0) await scheduleStore.loadVersions()
  // 联动：explanation 默认与 scheduleStore 的当前选中版本（一般是 ACTIVE）保持一致，
  // 避免首次 mount summary 拿的是 store ref 默认值 1001，与 schedule 选中版本错位
  await explanationStore.load(currentVersionId.value ?? undefined)
})
</script>

<template>
  <div class="aps-explanation">
    <!-- ===== 顶部 ===== -->
    <div class="page-header">
      <div>
        <h2 class="page-title">异常与原因解释</h2>
        <p class="page-sub">
          展示排程决策的真实根因与影响；设备故障仅提示影响评估，不会自动暂停任务
        </p>
      </div>
      <ElButton :loading="loading" @click="refresh"> <Icon icon="vi-ep:refresh" /> 刷新 </ElButton>
    </div>

    <!-- ===== 错误条 ===== -->
    <ElAlert v-if="error" type="error" :closable="false" show-icon :title="`加载失败：${error}`" />

    <!-- ===== 版本切换（10-09 重构：按业务优先级渲染） =====
         优先级：ACTIVE 置顶 → 最近 3 条 Computed → 失败（最多 2 条）→ 当前已选兜底
         替代旧"取前三条"硬截（@see store/schedule.ts displayedVersions getter）
    -->
    <div class="version-bar">
      <span class="bar-label">当前计划版本：</span>
      <ElButton
        v-for="v in displayedVersions"
        :key="v.id"
        size="small"
        :type="v.id === currentVersionId ? 'primary' : 'default'"
        @click="selectVersion(v.id)"
      >
        <span class="version-code">{{ v.versionCode }}</span>
      </ElButton>
    </div>

    <!-- ===== KPI 卡片（3 卡：ERROR / WARN / INFO；CRITICAL 已撤，v5.1.10 :4864） ===== -->
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
        <div class="kpi-ratio">3 类汇总</div>
      </div>
    </div>

    <!-- ===== 排程解释事实（按 objectType 分组；5 码：v5.1.10 :4858） ===== -->
    <ElCard class="panel">
      <template #header>
        <div class="panel-header">
          <span> 排程解释事实 </span>
          <span class="hint-text">按真实约束分组</span>
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
            <span class="group-name">{{ OBJECT_TYPE_LABELS[objType] ?? objType }}</span>
            <ElTag size="small" effect="plain">{{ items.length }} 项</ElTag>
          </div>
          <div class="fact-list">
            <div
              v-for="f in items"
              :key="f.factId"
              class="fact-item"
              :class="{ error: f.severity === 'ERROR' }"
            >
              <div class="fact-head">
                <ElTag size="small" :type="EXPLANATION_SEVERITY_TAG[f.severity]">
                  {{ EXPLANATION_SEVERITY_LABELS[f.severity] }}
                </ElTag>
                <span class="fact-ref">{{ f.objectRef }}</span>
                <!-- 15 码 ReasonCode 徽章（v5.1.10 :4863 权威枚举） -->
                <ElTag size="small" type="primary" effect="plain">
                  {{ reasonLabel(f.reasonCode) }}
                </ElTag>
                <span v-if="f.impactHours && f.impactHours > 0" class="fact-impact">
                  {{ f.impactHours }}h
                </span>
              </div>
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
      <!-- 业务事实问题（@see BusinessFactIssueDto） -->
      <ElCard class="panel">
        <template #header>
          <div class="panel-header">
            <span> 业务事实问题 </span>
            <!-- 总数与当前页信息由下方 ElPagination layout="total,..." 自带渲染，此处不再重复（10-10 续改） -->
          </div>
        </template>
        <div v-if="!businessFactIssues.length" class="panel-empty">
          <ElEmpty description="暂无业务问题" />
        </div>
        <ElTable v-else :data="sortedBusinessFactIssues" size="small" border>
          <ElTableColumn label="来源" width="120">
            <template #default="{ row }">
              <ElTag size="small" effect="plain">
                {{ BIZ_SOURCE_LABEL[row.source] ?? row.source }}
              </ElTag>
            </template>
          </ElTableColumn>
          <ElTableColumn label="类型" width="130">
            <template #default="{ row }">
              {{ BIZ_ISSUE_TYPE_LABEL[row.issueType] ?? row.issueType }}
            </template>
          </ElTableColumn>
          <ElTableColumn prop="detail" label="问题描述" min-width="200" />
          <ElTableColumn label="影响单据" width="140">
            <template #default="{ row }">
              <span v-if="row.documentNo">{{ row.documentNo }}</span>
              <span v-else-if="row.materialCode">{{ row.materialCode }}</span>
              <span v-else-if="row.stageCode">{{ row.stageCode }}</span>
              <span v-else class="muted-tag">—</span>
            </template>
          </ElTableColumn>
          <ElTableColumn label="复核" width="100">
            <template #default="{ row }">
              <ElTag size="small" :type="REVIEW_STATUS_TAG[row.reviewStatus]">
                {{ REVIEW_STATUS_LABEL[row.reviewStatus] ?? row.reviewStatus }}
              </ElTag>
            </template>
          </ElTableColumn>
          <ElTableColumn label="发生时间" width="140">
            <template #default="{ row }">
              {{ dayjs(row.occurredAt).format('MM-DD HH:mm') }}
            </template>
          </ElTableColumn>
        </ElTable>
        <!-- ===== 分页（10-10 续改：标准 ElPagination） ===== -->
        <div v-if="businessFactIssuesTotal > 0" class="biz-pagination">
          <ElPagination
            v-model:current-page="businessFactIssuesPage"
            v-model:page-size="businessFactIssuesPageSize"
            :total="businessFactIssuesTotal"
            :page-sizes="[10, 20, 50]"
            :disabled="businessFactIssuesLoading"
            layout="total, sizes, prev, pager, next, jumper"
            background
            :pager-count="5"
            @current-change="onBizPageChange"
            @size-change="onBizSizeChange"
          />
        </div>
      </ElCard>
    </div>

    <!-- ===== ReasonCode 分布（来自 Summary 端点 reasonCodeCounts） ===== -->
    <ElCard v-if="data?.reasonCodeCounts?.length" class="panel">
      <template #header>
        <div class="panel-header">
          <span> 原因代码分布 </span>
        </div>
      </template>
      <div class="code-distribution">
        <div v-for="c in data.reasonCodeCounts" :key="c.reasonCode" class="code-chip">
          <ElTag type="primary" effect="plain" size="small">{{ reasonLabel(c.reasonCode) }}</ElTag>
          <span class="code-count">{{ c.count }} 项</span>
          <span v-if="c.totalImpactHours && c.totalImpactHours > 0" class="code-impact">
            / {{ c.totalImpactHours }}h
          </span>
        </div>
      </div>
    </ElCard>
  </div>
</template>

<style lang="less" scoped>
.aps-explanation {
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-height: calc(100vh - 100px);
  padding: 16px;
  background: #f5f7fa;
}

/* ===== 顶部 ===== */
.page-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding-bottom: 4px;

  .page-title {
    margin: 0;
    font-size: 18px;
    font-weight: 600;
    color: #1e293b;
  }

  .page-sub {
    margin: 4px 0 0;
    font-size: 12px;
    color: #94a3b8;
  }
}

/* ===== 版本切换条 ===== */
.version-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  padding: 8px 12px;
  background: #fff;
  border-radius: 6px;
  box-shadow: 0 1px 2px rgb(15 23 42 / 4%);

  .bar-label {
    margin-right: 4px;
    font-size: 12px;
    color: #64748b;
  }

  .version-code {
    font-size: 12px;
  }
}

/* ===== KPI 4 卡 ===== */
.kpi-row {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 12px;
}

.kpi-card {
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  min-width: 0;
  min-height: 88px;
  padding: 14px 16px;
  background: #fff;
  border-top: 3px solid #94a3b8;
  border-radius: 6px;
  box-shadow: 0 1px 2px rgb(15 23 42 / 4%);
  transition: box-shadow 0.15s ease;

  &:hover {
    box-shadow: 0 2px 6px rgb(15 23 42 / 8%);
  }

  .kpi-label {
    font-size: 12px;
    font-weight: 500;
    color: #64748b;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .kpi-value {
    font-size: 26px;
    font-weight: 600;
    line-height: 1.2;
    color: #1e293b;
  }

  .kpi-ratio {
    font-size: 11px;
    color: #94a3b8;
    white-space: nowrap;
  }

  &.total {
    background: #f8fafc;
    border-top-color: #cbd5e1;

    .kpi-label {
      color: #475569;
    }

    .kpi-value {
      color: #334155;
    }
  }
}

/* ===== 内容面板通用 ===== */
.panel {
  border-radius: 6px;

  :deep(.el-card__header) {
    padding: 12px 16px;
    background: #fafbfc;
    border-bottom: 1px solid #f1f5f9;
  }

  :deep(.el-card__body) {
    padding: 16px;
  }
}

.panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
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
  padding: 24px 0;
  text-align: center;
}

.content-row {
  display: grid;
  grid-template-columns: 1fr;
  gap: 12px;
}

/* ===== 排程解释事实网格（按 objectType 分组） ===== */
.fact-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
  gap: 12px;
}

.fact-group {
  padding: 12px;
  background: #fafbfc;
  border: 1px solid #e2e8f0;
  border-radius: 6px;

  .group-head {
    display: flex;
    align-items: center;
    justify-content: space-between;
    margin-bottom: 10px;

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
  gap: 8px;
}

.fact-item {
  padding: 10px 12px;
  background: #fff;
  border-left: 3px solid #f59e0b;
  border-radius: 4px;

  &.error {
    background: #fef2f2;
    border-left-color: #ef4444;
  }

  .fact-head {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 6px;
  }

  .fact-ref {
    font-family: 'Courier New', monospace;
    font-size: 12px;
    font-weight: 600;
    color: #1e293b;
  }

  .fact-impact {
    margin-left: auto;
    font-size: 11px;
    font-weight: 600;
    color: #b45309;
  }

  .fact-time {
    margin-top: 6px;
    font-size: 11px;
    color: #94a3b8;
  }
}

/* ===== 业务事实问题分页（10-10 续改：标准 ElPagination） ===== */
.biz-pagination {
  display: flex;
  justify-content: flex-end;
  padding: 12px 16px;
  border-top: 1px solid #f1f5f9;

  :deep(.el-pagination) {
    --el-pagination-bg-color: #fafbfc;
  }

  :deep(.el-pagination__total) {
    margin-right: auto;
    font-size: 12px;
    color: #64748b;
  }
}

/* ===== ReasonCode 分布（chip 行） ===== */
.code-distribution {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.code-chip {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 6px 10px;
  background: #fafbfc;
  border: 1px solid #e2e8f0;
  border-radius: 4px;

  .code-count {
    font-size: 12px;
    font-weight: 600;
    color: #1e293b;
  }

  .code-impact {
    font-size: 11px;
    color: #b45309;
  }
}
</style>
