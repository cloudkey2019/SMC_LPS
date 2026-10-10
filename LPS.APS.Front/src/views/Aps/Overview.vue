<script setup lang="ts">
/**
 * APS V1 4号位 — 排产总览（页面 1 / U01 / U02 / U07）
 *
 * 4号位文档第 4 节约束：
 *  - 顶部展示各 Domain 当前 ACTIVE 版本（多 Domain 同时支持）
 *  - 订单摘要按 5 种状态聚合：按期/延期/风险/未排/仅估算
 *  - 仅展示，不触发任何重算/触发接口
 *  - "仅估算"订单必须显示 ESTIMATED_BADGE_TEXT（4号位文档第 7 节 / U07）
 *  - Domain 异常清晰区分 FAILED / BLOCKED_BY_UPSTREAM / PI_POSITION_ISSUE / ODS_CONTRACT_FAILED
 *
 * 写接口：本页面只读，0 个写调用（4号位文档第 2.1 节）
 */

import { computed, onMounted } from 'vue'
import dayjs from 'dayjs'
import { storeToRefs } from 'pinia'
import { useOverviewStore } from '@/store/modules/aps/overview'
import { useDomainStore } from '@/store/modules/aps/domain'
import {
  ORDER_SUMMARY_STATUS_LABELS,
  ESTIMATED_BADGE_TEXT,
  PLAN_VERSION_STATUS_LABELS,
  SETUP_SOURCES,
  SETUP_SOURCE_META,
  type OrderSummaryStatus,
  type PlanVersionStatus
} from '@/api/aps-v1/types'

import {
  ElAlert,
  ElButton,
  ElCard,
  ElDescriptions,
  ElDescriptionsItem,
  ElEmpty,
  ElIcon,
  ElTag
} from 'element-plus'

const overviewStore = useOverviewStore()
const { data, loading, error } = storeToRefs(overviewStore)

const domainStore = useDomainStore()
function humanLabel(domainKey: string | undefined | null): string {
  return domainStore.label(domainKey)
}

/* ===== 订单状态颜色 ===== */
const ORDER_STATUS_COLORS: Record<OrderSummaryStatus, string> = {
  ON_TIME: '#059669',
  AT_RISK: '#f59e0b',
  DELAYED: '#ef4444',
  UNSCHEDULED: '#94a3b8',
  ESTIMATED_ONLY: '#8b5cf6'
}

const ORDER_STATUS_ORDER: OrderSummaryStatus[] = [
  'ON_TIME',
  'AT_RISK',
  'DELAYED',
  'ESTIMATED_ONLY',
  'UNSCHEDULED'
]

/* ===== 版本状态标签（从 common.ts 共享 6 态中文映射） ===== */
const VERSION_STATUS_LABELS = PLAN_VERSION_STATUS_LABELS

const VERSION_STATUS_COLORS: Record<
  PlanVersionStatus,
  'success' | 'primary' | 'warning' | 'info' | 'danger'
> = {
  ACTIVE: 'success',
  BUILDING: 'primary',
  CANDIDATE: 'warning',
  FAILED: 'danger',
  ARCHIVED: 'info'
}

/* ===== Domain 异常标签 ===== */
const ISSUE_LABELS: Record<string, string> = {
  FAILED: '排程域失败',
  BLOCKED_BY_UPSTREAM: '上游阻断',
  PI_POSITION_ISSUE: 'PI 位置缺失',
  ODS_CONTRACT_FAILED: 'ODS 契约失败'
}

const ISSUE_COLORS: Record<string, 'danger' | 'warning' | 'info'> = {
  FAILED: 'danger',
  BLOCKED_BY_UPSTREAM: 'warning',
  PI_POSITION_ISSUE: 'info',
  ODS_CONTRACT_FAILED: 'danger'
}

/* ===== 订单摘要聚合（保证 5 种状态都出现） ===== */
const orderSummaryItems = computed(() => {
  const items = data.value?.orderSummary ?? []
  const map = new Map(items.map((it) => [it.status, it.count]))
  return ORDER_STATUS_ORDER.map((status) => ({
    status,
    label: ORDER_SUMMARY_STATUS_LABELS[status],
    count: map.get(status) ?? 0,
    color: ORDER_STATUS_COLORS[status]
  }))
})

const totalOrders = computed(() =>
  orderSummaryItems.value.reduce((sum, item) => sum + item.count, 0)
)

/* ===== 资源利用率 ===== */
const bottleneckResources = computed(() =>
  (data.value?.resourceSummary ?? []).filter((r) => r.isBottleneck)
)

/* ===== 加载 ===== */
function refresh() {
  overviewStore.load()
}

onMounted(refresh)
</script>

<template>
  <div class="aps-overview">
    <!-- ===== 顶部 ===== -->
    <div class="page-header">
      <div>
        <h2 class="page-title">排产总览</h2>
        <p class="page-sub">排产只读视图，所有改动通过相关页面操作</p>
      </div>
      <ElButton :loading="loading" @click="refresh"> <Icon icon="vi-ep:refresh" /> 刷新 </ElButton>
    </div>

    <!-- ===== 4号位约束提示 ===== -->
    <ElAlert type="info" :closable="false" show-icon class="hint-bar">
      <template #title>
        {{ ESTIMATED_BADGE_TEXT }}
      </template>
      <span class="hint-text">
        订单状态含 <strong style="color: #8b5cf6">"仅估算"</strong> 时显示该徽章；该类订单依赖
        PLANNING_PURCHASE_PLACEHOLDER，未与采购正式承诺。
      </span>
    </ElAlert>

    <!-- ===== 错误 / 加载态 ===== -->
    <ElAlert v-if="error" type="error" :closable="false" show-icon :title="`加载失败：${error}`" />

    <!-- ===== KPI 卡片：5 种订单状态 ===== -->
    <div class="kpi-row">
      <div
        v-for="item in orderSummaryItems"
        :key="item.status"
        class="kpi-card"
        :style="{ borderTopColor: item.color }"
      >
        <div class="kpi-label">{{ item.label }}</div>
        <div class="kpi-value" :style="{ color: item.color }">{{ item.count }}</div>
        <div class="kpi-ratio">
          {{ totalOrders > 0 ? Math.round((item.count / totalOrders) * 100) : 0 }}%
        </div>
      </div>
      <div class="kpi-card total">
        <div class="kpi-label">订单合计</div>
        <div class="kpi-value">{{ totalOrders }}</div>
        <div class="kpi-ratio">5 类汇总</div>
      </div>
    </div>

    <!-- ===== 中部两列 ===== -->
    <div class="content-row">
      <!-- 当前生效版本（多排程域） -->
      <ElCard class="panel">
        <template #header>
          <div class="panel-header">
            <span>当前生效版本</span>
            <ElTag v-if="data?.activeVersions?.length" type="success" size="small">
              {{ data.activeVersions.length }} 排程域
            </ElTag>
          </div>
        </template>
        <div v-if="!data?.activeVersions?.length" class="panel-empty">
          <ElEmpty description="当前无生效的计划版本" />
        </div>
        <div v-else class="version-list">
          <div v-for="v in data.activeVersions" :key="v.planVersionId" class="version-card">
            <div class="version-card-head">
              <div class="version-code">{{ v.versionCode }}</div>
              <ElTag
                :type="VERSION_STATUS_COLORS[v.status as PlanVersionStatus] || 'info'"
                size="small"
              >
                {{ VERSION_STATUS_LABELS[v.status as PlanVersionStatus] || v.status }}
              </ElTag>
            </div>
            <ElDescriptions :column="2" size="small" class="version-desc">
              <ElDescriptionsItem label="排程域">{{ humanLabel(v.domainKey) }}</ElDescriptionsItem>
              <ElDescriptionsItem label="激活时间">
                {{ dayjs(v.activatedAt).format('MM-DD HH:mm') }}
              </ElDescriptionsItem>
              <ElDescriptionsItem label="计划区间">
                {{ dayjs(v.planHorizonStart).format('YYYY-MM-DD') }}
                ~
                {{ dayjs(v.planHorizonEnd).format('YYYY-MM-DD') }}
              </ElDescriptionsItem>
              <ElDescriptionsItem label="来源 Run">
                {{ v.sourceScheduleRunId ?? '-' }}
              </ElDescriptionsItem>
            </ElDescriptions>
          </div>
        </div>
      </ElCard>

      <!-- Domain 异常 -->
      <ElCard class="panel">
        <template #header>
          <div class="panel-header">
            <span>排程域异常</span>
            <ElTag v-if="data?.domainIssues?.length" type="danger" size="small">
              {{ data.domainIssues.length }} 项
            </ElTag>
          </div>
        </template>
        <div v-if="!data?.domainIssues?.length" class="panel-empty">
          <ElEmpty description="当前无排程域异常" />
        </div>
        <div v-else class="issue-list">
          <div v-for="(issue, idx) in data.domainIssues" :key="idx" class="issue-item">
            <ElTag :type="ISSUE_COLORS[issue.issueType] || 'info'" size="small">
              {{ ISSUE_LABELS[issue.issueType] || issue.issueType }}
            </ElTag>
            <div class="issue-body">
              <div class="issue-domain">{{ humanLabel(issue.domainKey) }}</div>
              <div class="issue-msg">{{ issue.message }}</div>
              <div class="issue-time">{{ dayjs(issue.occurredAt).format('YYYY-MM-DD HH:mm') }}</div>
            </div>
          </div>
        </div>
      </ElCard>
    </div>

    <!-- ===== 资源利用率 ===== -->
    <ElCard class="panel">
      <template #header>
        <div class="panel-header">
          <span>资源利用率</span>
          <span v-if="bottleneckResources.length" class="hint-text">
            <ElIcon><Icon icon="vi-mdi:alert" /></ElIcon>
            <strong>{{ bottleneckResources.length }} 个瓶颈资源</strong>
          </span>
        </div>
      </template>

      <!-- 换型来源五值图例（v1.5 §9/§10；标签取自 SETUP_SOURCE_META，与 SetupUncovered.vue + Gantt.vue Drawer 一致） -->
      <ElAlert type="info" :closable="false" show-icon class="legend-alert">
        <template #title>换型来源五值图例（与甘特 Task Drawer 一致）</template>
        <div class="legend-row">
          <ElTag
            v-for="s in SETUP_SOURCES"
            :key="s"
            size="small"
            :type="SETUP_SOURCE_META[s].tag"
            effect="dark"
          >
            {{ SETUP_SOURCE_META[s].label }}
          </ElTag>
        </div>
      </ElAlert>

      <div v-if="!data?.resourceSummary?.length" class="panel-empty">
        <ElEmpty description="暂无资源数据" />
      </div>
      <table v-else class="resource-table">
        <thead>
          <tr>
            <th>资源编码</th>
            <th>名称</th>
            <th>利用率</th>
            <th>状态</th>
            <th>不可用窗口</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="r in data.resourceSummary"
            :key="r.resourceId"
            :class="{ bottleneck: r.isBottleneck }"
          >
            <td>{{ r.resourceCode }}</td>
            <td>{{ r.resourceName }}</td>
            <td>
              <div class="util-bar">
                <div
                  class="util-fill"
                  :style="{
                    width: Math.round(r.utilization * 100) + '%',
                    background:
                      r.utilization >= 0.9
                        ? '#ef4444'
                        : r.utilization >= 0.7
                          ? '#f59e0b'
                          : '#10b981'
                  }"
                ></div>
                <span class="util-text">{{ Math.round(r.utilization * 100) }}%</span>
              </div>
            </td>
            <td>
              <ElTag v-if="r.isBottleneck" type="danger" size="small">瓶颈</ElTag>
              <ElTag v-else type="success" size="small">正常</ElTag>
            </td>
            <td>
              <span v-if="!r.unavailableWindows.length" class="muted">无</span>
              <span v-else>
                {{ r.unavailableWindows[0].from }} ~ {{ r.unavailableWindows[0].to }}
              </span>
            </td>
          </tr>
        </tbody>
      </table>
    </ElCard>

    <!-- ===== 底部：待确认候选 + 估算订单统计 ===== -->
    <div class="footer-row">
      <ElCard class="footer-card">
        <div class="footer-item">
          <ElIcon class="footer-icon"><Icon icon="vi-mdi:clock-alert-outline" /></ElIcon>
          <div>
            <div class="footer-label">待确认 Candidate</div>
            <div class="footer-value">{{ data?.pendingCandidateCount ?? 0 }}</div>
            <div class="footer-sub">阶段 B 入口：Ctp / Candidate 对比</div>
          </div>
        </div>
      </ElCard>
      <ElCard class="footer-card estimated">
        <div class="footer-item">
          <ElIcon class="footer-icon"><Icon icon="vi-mdi:alert-decagram-outline" /></ElIcon>
          <div>
            <div class="footer-label">仅估算订单</div>
            <div class="footer-value">{{ data?.estimatedOnlyOrderCount ?? 0 }}</div>
            <div class="footer-sub">{{ ESTIMATED_BADGE_TEXT }}</div>
          </div>
        </div>
      </ElCard>
    </div>
  </div>
</template>

<style lang="less" scoped>
.aps-overview {
  display: flex;
  min-height: calc(100vh - 100px);
  padding: 16px;
  background: #f5f7fa;
  flex-direction: column;
  gap: 16px;
}

/* 顶部 */
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

/* 4号位约束提示 */
.hint-bar {
  :deep(.el-alert__title) {
    font-weight: 600;
  }

  .hint-text {
    margin-left: 8px;
    font-size: 13px;
    color: #475569;
  }
}

/* KPI 卡片 */
.kpi-row {
  display: grid;
  grid-template-columns: repeat(6, 1fr);
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
    font-size: 24px;
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

/* 中部两列 */
.content-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
}

/* 卡片 */
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

/* ACTIVE 版本卡片 */
.version-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.version-card {
  padding: 12px;
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 6px;

  &-head {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 8px;
  }

  .version-code {
    font-weight: 600;
    color: #1e293b;
  }

  .version-desc {
    :deep(.el-descriptions__label) {
      width: 80px;
      color: #94a3b8;
    }

    :deep(.el-descriptions__content) {
      color: #475569;
    }
  }
}

/* Domain 异常 */
.issue-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.issue-item {
  display: flex;
  gap: 12px;
  padding: 10px 12px;
  background: #fef2f2;
  border-left: 3px solid #ef4444;
  border-radius: 4px;
}

.issue-body {
  flex: 1;
}

.issue-domain {
  font-size: 13px;
  font-weight: 600;
  color: #1e293b;
}

.issue-msg {
  margin-top: 2px;
  font-size: 13px;
  color: #475569;
}

.issue-time {
  margin-top: 4px;
  font-size: 11px;
  color: #94a3b8;
}

/* 资源表 */
.resource-table {
  width: 100%;
  border-collapse: collapse;

  th,
  td {
    padding: 10px 12px;
    font-size: 13px;
    text-align: left;
    border-bottom: 1px solid #f1f5f9;
  }

  th {
    font-weight: 500;
    color: #64748b;
    background: #f8fafc;
  }

  tbody tr.bottleneck {
    background: #fef2f2;
  }

  tbody tr:hover {
    background: #f8fafc;
  }
}

.util-bar {
  position: relative;
  width: 100%;
  height: 18px;
  overflow: hidden;
  background: #f1f5f9;
  border-radius: 9px;

  .util-fill {
    position: absolute;
    top: 0;
    left: 0;
    height: 100%;
    border-radius: 9px;
    transition: width 0.3s;
  }

  .util-text {
    position: absolute;
    top: 50%;
    left: 50%;
    z-index: 1;
    font-size: 11px;
    font-weight: 500;
    color: #1e293b;
    transform: translate(-50%, -50%);
  }
}

.muted {
  color: #cbd5e1;
}

/* 底部 */
.footer-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
}

.footer-card {
  border-radius: 8px;

  &.estimated {
    background: #faf5ff;
  }
}

.footer-item {
  display: flex;
  align-items: center;
  gap: 16px;

  .footer-icon {
    font-size: 32px;
    color: #3b82f6;
  }

  .footer-label {
    font-size: 13px;
    color: #64748b;
  }

  .footer-value {
    margin: 4px 0;
    font-size: 24px;
    font-weight: 600;
    color: #1e293b;
  }

  .footer-sub {
    font-size: 11px;
    color: #94a3b8;
  }
}
</style>
