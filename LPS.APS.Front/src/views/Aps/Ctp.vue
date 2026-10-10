<script setup lang="ts">
/**
 * APS V1 4号位 — CTP / 插单评估（页面 4 / U07 / U08 / U09 / U11 / U15）
 *
 * 4号位文档第 7-8 节约束：
 *  - CTP 仅展示评估结果，不暴露"采用"按钮（U11）
 *  - Estimated 供给必须显示徽章（U07）
 *  - 根因优先（U09），不只显示 DUE_DATE_RISK
 *  - impactedOrderCount 超阈值仅 Warning，不表示截断（U15）
 *  - 跨 Domain 时显示链式 WHATIF（V1 仅汇总展示）
 *
 * Mock 设计：
 *  - M-001 (≤100) → 按期，全部 FACT
 *  - M-003 → 单 Domain 延期（CAPACITY + ESTIMATED）
 *  - M-005 → 跨 Domain 链式（INJECTION→ASSEMBLY→TEST，超阈值）
 *  - 其它 → 默认 DUE_DATE_RISK 场景
 */

import { computed, onMounted, reactive } from 'vue'
import dayjs from 'dayjs'
import { storeToRefs } from 'pinia'
import { useRoute } from 'vue-router'
import { useCtpStore } from '@/store/modules/aps/ctp'
import { useDomainStore } from '@/store/modules/aps/domain'
import { ESTIMATED_BADGE_TEXT, type CtpReason } from '@/api/aps-v1/types'

import {
  ElAlert,
  ElButton,
  ElCard,
  ElDatePicker,
  ElEmpty,
  ElInput,
  ElInputNumber,
  ElOption,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag,
  ElTooltip
} from 'element-plus'

const ctpStore = useCtpStore()
const { result, history, loading, error, hasResult, isOverThreshold } = storeToRefs(ctpStore)

const domainStore = useDomainStore()
function humanLabel(domainKey: string | undefined | null): string {
  return domainStore.label(domainKey)
}

/** 模板内拿到的就一定是 NonNull 的（与 hasResult 配套使用） */
const resultNN = computed(() => result.value!)

/** P1-08 入口：Gantt/Workbench 可带 ?purpose=INSERT_IMPACT_ANALYSIS 跳转；Ctp 在挂载时读取并覆盖默认值 */
const route = useRoute()
onMounted(() => {
  if (route.query.purpose === 'INSERT_IMPACT_ANALYSIS') {
    form.purpose = 'INSERT_IMPACT_ANALYSIS'
  }
})

/* ===== 表单本地副本（输入体验用，提交时再写回 store）===== */
const form = reactive({
  orderCanonicalId: '',
  materialCode: '',
  quantity: 100,
  factoryCode: 'F-SUZ-01',
  requestedDueDate: new Date(Date.now() + 10 * 86400_000).toISOString().slice(0, 10),
  /** 业务用途标签（P0-04：审核报告第 8 节 — 页面提交必须真正传 Purpose） */
  purpose: 'CTP' as 'CTP' | 'INSERT_IMPACT_ANALYSIS'
})

const MATERIAL_OPTIONS = [
  { code: 'M-001', name: '电机壳体-A' },
  { code: 'M-002', name: '机柜上盖-B' },
  { code: 'M-003', name: '控制箱-C' },
  { code: 'M-004', name: '标准泵-D' },
  { code: 'M-005', name: '包装套件-E' },
  { code: 'M-006', name: '机架焊接件-F' },
  { code: 'M-007', name: '电路板组件-G' }
]
const FACTORY_OPTIONS = [
  { code: 'F-SUZ-01', name: '苏州厂' },
  { code: 'F-CDG-02', name: '成都厂' }
]

/* ===== 字典 ===== */
const REASON_LABEL: Record<CtpReason['reasonCode'], string> = {
  MATERIAL_AVAILABLE: '物料就绪',
  CAPACITY_LIMIT: '产能受限',
  CROSS_DOMAIN_HANDOFF: '跨域交接',
  DEMAND_PROTECTION_CONFLICT: '需求保护冲突',
  DUE_DATE_RISK: '交期风险'
}
const REASON_TAG_TYPE: Record<CtpReason['reasonCode'], 'success' | 'warning' | 'danger' | 'info'> =
  {
    MATERIAL_AVAILABLE: 'success',
    CAPACITY_LIMIT: 'warning',
    // v1.2 §15：跨域交接必须用真实 DomainKey（reasonCode 联动 domainResults）；warning 警示
    CROSS_DOMAIN_HANDOFF: 'warning',
    DEMAND_PROTECTION_CONFLICT: 'danger',
    DUE_DATE_RISK: 'warning'
  }

/* ===== KPI: 评估结果核心指标 ===== */
const requestedDueMs = computed(() => new Date(form.requestedDueDate).getTime())
const earliestMs = computed(() =>
  result.value ? new Date(result.value.earliestCompletion).getTime() : 0
)
const delayDays = computed(() => {
  if (!result.value) return 0
  return Math.max(0, Math.round((earliestMs.value - requestedDueMs.value) / 86400_000))
})

/** P0-04：purpose-aware 文案（标题/说明/历史过滤）
 *  - CTP 给客户承诺
 *  - INSERT_IMPACT_ANALYSIS 给内部决策，仅试算不可激活
 */
const PURPOSE_TITLE: Record<'CTP' | 'INSERT_IMPACT_ANALYSIS', string> = {
  CTP: 'CTP / 插单评估',
  INSERT_IMPACT_ANALYSIS: '插单影响分析'
}
const PURPOSE_SUB: Record<'CTP' | 'INSERT_IMPACT_ANALYSIS', string> = {
  CTP: '输入插单参数 → 立即获得按期/延期判定、根因、影响订单数',
  INSERT_IMPACT_ANALYSIS: '输入插单参数 → 内部评估对其它订单/资源的影响（仅试算，不会改为正式计划）'
}
const PURPOSE_HISTORY_TAG: Record<'CTP' | 'INSERT_IMPACT_ANALYSIS', 'primary' | 'info'> = {
  CTP: 'primary',
  INSERT_IMPACT_ANALYSIS: 'info'
}

/** 按当前 purpose 过滤 HISTORY（CTP / INSERT_IMPACT_ANALYSIS 隔离回看） */
const filteredHistory = computed(() =>
  history.value.filter((h) => h.input.purpose === form.purpose)
)

/* ===== 操作 ===== */
async function onSubmit(): Promise<void> {
  await ctpStore.evaluate({
    orderCanonicalId: form.orderCanonicalId || `PO-${Date.now()}`,
    materialCode: form.materialCode,
    quantity: form.quantity,
    factoryCode: form.factoryCode,
    requestedDueDate: form.requestedDueDate,
    purpose: form.purpose
  })
}
function onReset(): void {
  Object.assign(form, {
    orderCanonicalId: '',
    materialCode: '',
    quantity: 100,
    factoryCode: 'F-SUZ-01',
    requestedDueDate: new Date(Date.now() + 10 * 86400_000).toISOString().slice(0, 10),
    purpose: 'CTP'
  })
  ctpStore.reset()
}
function onLoadHistory(entryId: number): void {
  const entry = history.value.find((h) => h.id === entryId)
  if (!entry) return
  ctpStore.loadFromHistory(entry)
  Object.assign(form, {
    orderCanonicalId: entry.input.orderCanonicalId,
    materialCode: entry.input.materialCode,
    quantity: entry.input.quantity,
    factoryCode: entry.input.factoryCode,
    requestedDueDate: entry.input.requestedDueDate,
    purpose: entry.input.purpose
  })
}
</script>

<template>
  <div class="aps-ctp">
    <!-- ===== 顶部 ===== -->
    <div class="page-header">
      <div>
        <h2 class="page-title">
          {{ PURPOSE_TITLE[form.purpose] }}
          <ElTag
            v-if="form.purpose === 'INSERT_IMPACT_ANALYSIS'"
            size="small"
            effect="dark"
            type="info"
            style="margin-left: 8px; vertical-align: middle"
          >
            WHATIF
          </ElTag>
        </h2>
        <p class="page-sub">{{ PURPOSE_SUB[form.purpose] }}</p>
      </div>
    </div>

    <!-- ===== 业务约束提示 ===== -->
    <ElAlert type="info" :closable="false" show-icon class="hint-bar">
      <template #title>使用说明</template>
      <span class="hint-text">
        依赖采购估算时，结果中会显示对应徽章；根因优先展示（不会只给一条笼统的"交期风险"）；
        <strong>本页只做评估展示，不提供"采用"按钮</strong
        >；影响订单数超过阈值时只做警示提示，不截断结果。
        评估用途分为「可靠交期判断」与「插单影响分析」两类，两者结果均不可激活。
      </span>
      <span class="hint-text hint-text-v12">
        跨排程域链式依赖（跨域交接）使用真实排程域标识，页面会对跨域结果与根因逐条
        双向校验，并以警示配色突出跨域交接项。
      </span>
    </ElAlert>

    <!-- ===== 输入表单 ===== -->
    <ElCard class="panel">
      <template #header>
        <span>评估输入</span>
      </template>
      <div class="input-form">
        <div class="input-field">
          <span class="field-label">订单号</span>
          <ElInput
            v-model="form.orderCanonicalId"
            placeholder="可空，留空将自动生成"
            size="default"
            class="field-input"
          />
        </div>
        <div class="input-field">
          <span class="field-label">物料（产品族编码）</span>
          <ElSelect v-model="form.materialCode" placeholder="选择物料" class="field-input">
            <ElOption
              v-for="opt in MATERIAL_OPTIONS"
              :key="opt.code"
              :label="`${opt.code} - ${opt.name}`"
              :value="opt.code"
            />
          </ElSelect>
        </div>
        <div class="input-field">
          <span class="field-label">数量</span>
          <ElInputNumber
            v-model="form.quantity"
            :min="1"
            :max="9999"
            :step="10"
            class="field-input"
          />
        </div>
        <div class="input-field">
          <span class="field-label">工厂</span>
          <ElSelect v-model="form.factoryCode" placeholder="选择工厂" class="field-input">
            <ElOption
              v-for="opt in FACTORY_OPTIONS"
              :key="opt.code"
              :label="opt.name"
              :value="opt.code"
            />
          </ElSelect>
        </div>
        <div class="input-field">
          <span class="field-label">客户请求交期</span>
          <ElDatePicker
            v-model="form.requestedDueDate"
            type="date"
            value-format="YYYY-MM-DD"
            placeholder="选择日期"
            class="field-input"
          />
        </div>
        <div class="input-field">
          <span class="field-label">业务用途</span>
          <ElSelect v-model="form.purpose" class="field-input">
            <ElOption label="可靠交期判断" value="CTP" />
            <ElOption label="插单影响分析" value="INSERT_IMPACT_ANALYSIS" />
          </ElSelect>
        </div>
        <div class="input-actions">
          <ElButton type="primary" :loading="loading" @click="onSubmit">
            <Icon icon="vi-ep:circle-check" /> 评估
          </ElButton>
          <ElButton @click="onReset">重置</ElButton>
        </div>
      </div>
    </ElCard>

    <!-- ===== 错误条 ===== -->
    <ElAlert v-if="error" type="error" :closable="false" show-icon :title="`评估失败：${error}`" />

    <!-- ===== 结果区 ===== -->
    <div v-if="hasResult" class="result-area">
      <!-- 顶部判定卡 -->
      <div class="verdict-row">
        <div class="verdict-card" :class="resultNN.meetsRequestedDueDate ? 'pass' : 'fail'">
          <Icon
            class="verdict-icon"
            :icon="
              resultNN.meetsRequestedDueDate ? 'vi-mdi:check-decagram' : 'vi-mdi:alert-octagon'
            "
          />
          <div class="verdict-body">
            <div class="verdict-title">
              <template v-if="form.purpose === 'CTP'">
                {{ resultNN.meetsRequestedDueDate ? '可按期完成' : '无法按期完成' }}
              </template>
              <template v-else>
                {{
                  resultNN.meetsRequestedDueDate
                    ? `插单可插入（影响 ${resultNN.impactedOrderCount} 单）`
                    : `插单需权衡（影响 ${resultNN.impactedOrderCount} 单）`
                }}
              </template>
            </div>
            <div class="verdict-sub">
              评估时间：{{ dayjs().format('YYYY-MM-DD HH:mm') }} ·
              {{ form.purpose === 'CTP' ? '当前生效版本' : '仅试算，不改为正式计划' }}
            </div>
          </div>
        </div>
        <div class="verdict-card stat">
          <div class="stat-label">客户请求交期</div>
          <div class="stat-val">{{ form.requestedDueDate }}</div>
        </div>
        <div class="verdict-card stat warn" v-if="!resultNN.meetsRequestedDueDate">
          <div class="stat-label">最早完成日期</div>
          <div class="stat-val">{{ dayjs(resultNN.earliestCompletion).format('YYYY-MM-DD') }}</div>
          <div class="stat-sub">延期 {{ delayDays }} 天</div>
        </div>
        <div class="verdict-card stat ok" v-else>
          <div class="stat-label">最早完成日期</div>
          <div class="stat-val">{{ dayjs(resultNN.earliestCompletion).format('YYYY-MM-DD') }}</div>
          <div class="stat-sub">准时</div>
        </div>
      </div>

      <!-- Estimated 徽章 (U07) -->
      <ElAlert
        v-if="resultNN.dependsOnEstimatedSupply"
        type="warning"
        :closable="false"
        show-icon
        class="est-alert"
      >
        <template #title>
          <ElTag effect="dark" size="small" style="background: #8b5cf6; border-color: #8b5cf6">
            估算值
          </ElTag>
          {{ ESTIMATED_BADGE_TEXT }}
        </template>
      </ElAlert>

      <!-- 受影响订单数超阈值（U15） -->
      <ElAlert v-if="isOverThreshold" type="warning" :closable="false" show-icon class="over-alert">
        <template #title>
          <ElTag effect="dark" size="small" type="warning">超出阈值</ElTag>
          评估影响 <strong>{{ resultNN.impactedOrderCount }}</strong> 个订单，已超过阈值
          <strong>{{ resultNN.maxImpactedOrdersThreshold }}</strong
          >，仅给出警示，不表示截断。
        </template>
      </ElAlert>

      <!-- 主要瓶颈 + 原因 -->
      <div class="content-row">
        <ElCard class="panel">
          <template #header>
            <div class="panel-header">
              <span> 主要瓶颈 </span>
              <span class="muted-tag">根因优先</span>
            </div>
          </template>
          <div class="bottleneck-box">
            <Icon class="bn-icon" icon="vi-mdi:chart-gantt" />
            <span>{{ resultNN.mainBottleneck }}</span>
          </div>
          <h4 class="reason-title">原因（按优先级排序）</h4>
          <div class="reason-list">
            <div
              v-for="(r, idx) in resultNN.reasons"
              :key="idx"
              class="reason-item"
              :class="{ est: r.isEstimated }"
            >
              <span class="reason-rank">{{ idx + 1 }}</span>
              <div class="reason-body">
                <div class="reason-head">
                  <ElTag size="small" :type="REASON_TAG_TYPE[r.reasonCode]" effect="light">
                    {{ REASON_LABEL[r.reasonCode] }}
                  </ElTag>
                  <ElTooltip v-if="r.isEstimated" :content="ESTIMATED_BADGE_TEXT" placement="top">
                    <ElTag
                      size="small"
                      effect="dark"
                      style="background: #8b5cf6; border-color: #8b5cf6"
                    >
                      估算值
                    </ElTag>
                  </ElTooltip>
                  <span v-if="r.relatedRef" class="reason-rel">
                    {{ r.relatedRef }}
                  </span>
                </div>
                <div class="reason-desc">{{ r.description }}</div>
              </div>
            </div>
          </div>
        </ElCard>

        <!-- 受影响订单 -->
        <ElCard class="panel">
          <template #header>
            <div class="panel-header">
              <span> 受影响订单 </span>
              <ElTag size="small" :type="isOverThreshold ? 'danger' : 'info'">
                {{ resultNN.impactedOrders.length }} / {{ resultNN.maxImpactedOrdersThreshold }}
              </ElTag>
            </div>
          </template>
          <div v-if="!resultNN.impactedOrders.length" class="panel-empty">
            <ElEmpty description="无受影响订单" />
          </div>
          <ElTable v-else :data="resultNN.impactedOrders" size="small" border>
            <ElTableColumn prop="orderNo" label="订单号" width="150" />
            <ElTableColumn label="原完成" width="140">
              <template #default="{ row }">
                {{ dayjs(row.originalCompletion).format('MM-DD') }}
              </template>
            </ElTableColumn>
            <ElTableColumn label="新完成" width="140">
              <template #default="{ row }">
                <span :class="{ del: row.becomesDelayed }">
                  {{ row.newCompletion ? dayjs(row.newCompletion).format('MM-DD') : '-' }}
                </span>
              </template>
            </ElTableColumn>
            <ElTableColumn label="延期" width="70" align="center">
              <template #default="{ row }">
                <ElTag size="small" :type="row.becomesDelayed ? 'danger' : 'success'">
                  {{ row.becomesDelayed ? '是' : '否' }}
                </ElTag>
              </template>
            </ElTableColumn>
            <ElTableColumn label="需求保护冲突" width="120" align="center">
              <template #default="{ row }">
                <ElTag v-if="row.hasProtectionConflict" size="small" type="warning"> 冲突 </ElTag>
                <span v-else class="muted">-</span>
              </template>
            </ElTableColumn>
          </ElTable>

          <!-- 需求保护冲突明细 -->
          <h4 v-if="resultNN.protectionConflicts.length" class="reason-title">
            需求保护冲突明细
          </h4>
          <div v-if="resultNN.protectionConflicts.length" class="conflict-list">
            <div v-for="(c, i) in resultNN.protectionConflicts" :key="i" class="conflict-item">
              <ElTag size="small" type="warning" effect="dark">{{ c.conflictType }}</ElTag>
              <span class="conflict-msg">{{ c.description }}</span>
            </div>
          </div>
        </ElCard>
      </div>

      <!-- 跨 Domain 链式 WHATIF -->
      <ElCard v-if="resultNN.isCrossDomainChained && resultNN.domainResults?.length" class="panel">
        <template #header>
          <div class="panel-header">
            <span>
              跨排程域链式评估
              <ElTag size="small" type="info" effect="plain">阶段 B 跨域</ElTag>
            </span>
            <span class="muted-tag">
              {{
                form.purpose === 'INSERT_IMPACT_ANALYSIS'
                  ? 'INSERT链式：仅汇总影响，不展开递归'
                  : 'V1 仅展示汇总，不展开递归 WHATIF'
              }}
            </span>
            <ElTag
              v-if="form.purpose === 'INSERT_IMPACT_ANALYSIS'"
              size="small"
              type="info"
              effect="plain"
              style="margin-left: 8px"
            >
              插单影响分析
            </ElTag>
          </div>
        </template>
        <div class="domain-chain">
          <div
            v-for="(d, idx) in resultNN.domainResults"
            :key="d.domainKey"
            class="chain-item"
            :class="{ ok: d.success, fail: !d.success }"
          >
            <div class="chain-head">
              <ElTag size="small" :type="d.success ? 'success' : 'danger'" effect="dark">
                {{ humanLabel(d.domainKey) }}
              </ElTag>
              <span class="chain-time">
                最早完成 {{ dayjs(d.earliestCompletion).format('MM-DD') }}
              </span>
              <Icon
                v-if="idx < (resultNN.domainResults?.length ?? 0) - 1"
                class="chain-arrow"
                icon="vi-mdi:arrow-right-thick"
              />
            </div>
            <div class="chain-bn">{{ d.mainBottleneck }}</div>
            <div v-if="d.failureReason" class="chain-fail">{{ d.failureReason }}</div>
          </div>
        </div>
      </ElCard>

      <!-- 历史评估（P0-04：按 purpose 过滤，CTP / INSERT_IMPACT_ANALYSIS 隔离回看） -->
      <ElCard v-if="filteredHistory.length" class="panel">
        <template #header>
          <div class="panel-header">
            <span>
              本次会话评估历史
              <ElTag size="small" effect="plain" :type="PURPOSE_HISTORY_TAG[form.purpose]">
                {{ form.purpose }}
              </ElTag>
              （最多 5 条）
            </span>
            <ElButton size="small" link @click="ctpStore.clearHistory">清空</ElButton>
          </div>
        </template>
        <ElTable :data="filteredHistory" size="small" border>
          <ElTableColumn label="时间" width="140">
            <template #default="{ row }">
              {{ dayjs(row.evaluatedAt).format('MM-DD HH:mm:ss') }}
            </template>
          </ElTableColumn>
          <ElTableColumn label="用途" width="100">
            <template #default="{ row }">
              <ElTag size="small" effect="plain" :type="PURPOSE_HISTORY_TAG[row.input.purpose]">
                {{ row.input.purpose }}
              </ElTag>
            </template>
          </ElTableColumn>
          <ElTableColumn prop="input.orderCanonicalId" label="订单号" width="160" />
          <ElTableColumn prop="input.materialCode" label="物料" width="80" />
          <ElTableColumn prop="input.quantity" label="数量" width="80" />
          <ElTableColumn label="判定" width="100">
            <template #default="{ row }">
              <ElTag size="small" :type="row.result.meetsRequestedDueDate ? 'success' : 'danger'">
                {{ row.result.meetsRequestedDueDate ? '按期' : '延期' }}
              </ElTag>
            </template>
          </ElTableColumn>
          <ElTableColumn label="最早完成" width="140">
            <template #default="{ row }">
              {{ dayjs(row.result.earliestCompletion).format('YYYY-MM-DD') }}
            </template>
          </ElTableColumn>
          <ElTableColumn label="影响/阈值" width="100">
            <template #default="{ row }">
              {{ row.result.impactedOrderCount }} / {{ row.result.maxImpactedOrdersThreshold }}
            </template>
          </ElTableColumn>
          <ElTableColumn label="操作" width="80" align="center">
            <template #default="{ row }">
              <a href="javascript:;" @click="onLoadHistory(row.id)">载入</a>
            </template>
          </ElTableColumn>
        </ElTable>
      </ElCard>
    </div>

    <!-- ===== 空态 ===== -->
    <ElCard v-else class="empty-card">
      <ElEmpty description="请填写左侧插单参数后点击「评估」">
        <template #image>
          <Icon style="font-size: 48px; color: #cbd5e1" icon="vi-mdi:target" />
        </template>
      </ElEmpty>
    </ElCard>
  </div>
</template>

<style lang="less" scoped>
.aps-ctp {
  display: flex;
  min-height: calc(100vh - 100px);
  padding: 16px;
  background: #f5f7fa;
  flex-direction: column;
  gap: 16px;
}

.page-header {
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

  .hint-text-v12 {
    display: block;
    margin-top: 4px;
    margin-left: 8px;
    padding-top: 4px;
    font-size: 12px;
    line-height: 1.7;
    color: #64748b;
    border-top: 1px dashed #cbd5e1;
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

  .muted-tag {
    font-size: 12px;
    font-weight: 400;
    color: #94a3b8;
  }
}

.panel-empty {
  padding: 20px 0;
}

.muted {
  color: #94a3b8;
}

/* 表单 */
.input-form {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 14px 20px;
}

.input-field {
  display: flex;
  align-items: center;
  gap: 12px;

  .field-label {
    width: 130px;
    font-size: 13px;
    color: #64748b;
    text-align: right;
    flex-shrink: 0;
  }

  .field-input {
    flex: 1;
  }
}

.input-actions {
  display: flex;
  padding-top: 14px;
  margin-top: 4px;
  border-top: 1px dashed #e2e8f0;
  grid-column: 1 / -1;
  justify-content: flex-end;
  gap: 8px;
}

/* Verdict 判定 */
.verdict-row {
  display: grid;
  grid-template-columns: 1fr 1fr 1fr;
  gap: 14px;
}

.verdict-card {
  display: flex;
  padding: 16px 20px;
  background: #fff;
  border-left: 4px solid #94a3b8;
  border-radius: 8px;
  box-shadow: 0 1px 4px rgb(0 0 0 / 6%);
  align-items: center;
  gap: 14px;

  .verdict-icon {
    font-size: 36px;
  }

  .verdict-title {
    font-size: 16px;
    font-weight: 600;
    color: #1e293b;
  }

  .verdict-sub {
    margin-top: 4px;
    font-size: 11px;
    color: #94a3b8;
  }

  &.pass {
    border-left-color: #059669;

    .verdict-icon {
      color: #059669;
    }
  }

  &.fail {
    border-left-color: #ef4444;

    .verdict-icon {
      color: #ef4444;
    }
  }

  &.stat {
    flex-direction: column;
    align-items: flex-start;
    gap: 4px;

    .stat-label {
      font-size: 12px;
      color: #94a3b8;
    }

    .stat-val {
      font-size: 20px;
      font-weight: 600;
      color: #1e293b;
    }

    .stat-sub {
      font-size: 11px;
      color: #cbd5e1;
    }

    &.warn .stat-val {
      color: #ef4444;
    }

    &.ok .stat-val {
      color: #059669;
    }
  }
}

/* 受影响订单列 */
.content-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
}

/* 原因列表 */
.bottleneck-box {
  display: flex;
  padding: 12px 16px;
  margin-bottom: 16px;
  font-size: 14px;
  color: #1e293b;
  background: #fef3c7;
  border-left: 3px solid #f59e0b;
  border-radius: 4px;
  align-items: center;
  gap: 10px;

  .bn-icon {
    font-size: 22px;
    color: #f59e0b;
  }
}

.reason-title {
  margin: 16px 0 8px;
  font-size: 13px;
  font-weight: 600;
  color: #475569;
}

.reason-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.reason-item {
  display: flex;
  gap: 10px;
  padding: 10px 12px;
  background: #f8fafc;
  border-left: 3px solid #3b82f6;
  border-radius: 4px;

  &.est {
    background: #faf5ff;
    border-left-color: #8b5cf6;
  }

  .reason-rank {
    width: 22px;
    height: 22px;
    font-size: 12px;
    font-weight: 600;
    line-height: 22px;
    color: #fff;
    text-align: center;
    background: #3b82f6;
    border-radius: 50%;
    flex-shrink: 0;
  }

  .reason-body {
    flex: 1;
  }

  .reason-head {
    display: flex;
    align-items: center;
    gap: 6px;
    flex-wrap: wrap;
  }

  .reason-rel {
    font-family: 'Courier New', monospace;
    font-size: 12px;
    color: #64748b;
  }

  .reason-desc {
    margin-top: 6px;
    font-size: 13px;
    color: #475569;
  }
}

/* Protection 冲突 */
.conflict-list {
  display: flex;
  margin-top: 8px;
  flex-direction: column;
  gap: 6px;
}

.conflict-item {
  display: flex;
  padding: 8px 10px;
  font-size: 12px;
  color: #92400e;
  background: #fef3c7;
  border-radius: 4px;
  align-items: center;
  gap: 8px;
}

/* 表格列样式 */
.del {
  font-weight: 600;
  color: #ef4444;
}

/* 跨 Domain 链 */
.domain-chain {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
}

.chain-item {
  flex: 1;
  min-width: 180px;
  padding: 10px 14px;
  background: #f0fdf4;
  border: 1px solid #bbf7d0;
  border-radius: 6px;

  &.fail {
    background: #fef2f2;
    border-color: #fecaca;
  }

  .chain-head {
    display: flex;
    align-items: center;
    gap: 8px;

    .chain-time {
      font-size: 12px;
      color: #64748b;
    }

    .chain-arrow {
      font-size: 18px;
      color: #94a3b8;
    }
  }

  .chain-bn {
    margin-top: 6px;
    font-size: 12px;
    color: #475569;
  }

  .chain-fail {
    margin-top: 4px;
    font-size: 11px;
    color: #b91c1c;
  }
}

/* ESTIMATED 顶部徽章 */
.est-alert {
  margin-bottom: 12px;
}

.over-alert {
  margin-bottom: 12px;
}

/* 空态 */
.empty-card {
  background: transparent;
  border: none;
  border-radius: 8px;
  box-shadow: none;
}
</style>
