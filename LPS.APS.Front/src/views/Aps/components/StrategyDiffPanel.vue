<!--
  APS V1 4号位 — 策略版本 Diff 展示子组件

  @see lps/LPS.APS.Core/DTOs/Governance/VersionDiffResult.cs（fieldDiffs[]）
  @see lps/LPS.APS.Application/Services/GovernanceVersionService.cs（CompareStrategyProfileVersionsAsync）

  复用 Rules.vue diff 表格样式（5xx 行附近），改为独立组件便于策略页调用
  - 三列：字段名 / 源版本值 / 目标版本值
  - 变化字段高亮（橙色背景 + 文字色）
  - 顶部展示源→目标版本号 + 对比时间 + 变化字段数
-->
<script setup lang="ts">
import { computed } from 'vue'
import { ElEmpty, ElTable, ElTableColumn, ElTag } from 'element-plus'
import type { StrategyVersionDiffDto } from '@/api/aps-v1'
import { formatUtcDateTimeSec } from '@/utils/datetime'

const props = defineProps<{
  diff: StrategyVersionDiffDto | null
}>()

/** 变化字段计数（红角标） */
const changedCount = computed<number>(
  () => props.diff?.fieldDiffs.filter((f) => f.isChanged).length ?? 0
)
</script>

<template>
  <div class="strat-diff-panel">
    <div v-if="!diff" class="strat-diff-empty">
      <ElEmpty description="未选择版本或源 = 目标" :image-size="60" />
    </div>
    <template v-else>
      <div class="strat-diff-head">
        <span>
          <ElTag size="small" type="info">源</ElTag>
          {{ diff.sourceVersionCode }} (#{{ diff.sourceVersionId }})
        </span>
        <span class="strat-diff-arrow">→</span>
        <span>
          <ElTag size="small" type="primary">目标</ElTag>
          {{ diff.targetVersionCode }} (#{{ diff.targetVersionId }})
        </span>
        <ElTag v-if="changedCount > 0" size="small" type="warning" effect="dark">
          {{ changedCount }} 处变化
        </ElTag>
        <ElTag v-else size="small" type="success">无变化</ElTag>
        <span class="strat-diff-time">对比于 {{ formatUtcDateTimeSec(diff.comparedAt) }}</span>
      </div>
      <ElTable :data="diff.fieldDiffs" size="small" border>
        <ElTableColumn label="字段" min-width="140">
          <template #default="{ row }: { row: StrategyVersionDiffDto['fieldDiffs'][number] }">
            <span class="strat-diff-name">{{ row.fieldDisplayName }}</span>
            <code class="code-tag">{{ row.fieldName }}</code>
          </template>
        </ElTableColumn>
        <ElTableColumn label="源版本值" min-width="180">
          <template #default="{ row }: { row: StrategyVersionDiffDto['fieldDiffs'][number] }">
            <span :class="{ 'strat-diff-changed': row.isChanged }">
              {{ row.sourceValue || '—' }}
            </span>
          </template>
        </ElTableColumn>
        <ElTableColumn label="目标版本值" min-width="180">
          <template #default="{ row }: { row: StrategyVersionDiffDto['fieldDiffs'][number] }">
            <span :class="{ 'strat-diff-changed': row.isChanged }">
              {{ row.targetValue || '—' }}
            </span>
          </template>
        </ElTableColumn>
        <ElTableColumn label="变化" width="80" align="center">
          <template #default="{ row }: { row: StrategyVersionDiffDto['fieldDiffs'][number] }">
            <ElTag v-if="row.isChanged" size="small" type="warning" effect="plain">变更</ElTag>
            <ElTag v-else size="small" type="info" effect="plain">—</ElTag>
          </template>
        </ElTableColumn>
      </ElTable>
    </template>
  </div>
</template>

<style scoped>
.strat-diff-panel {
  padding: 4px 0;
}
.strat-diff-empty {
  padding: 20px 0;
}
.strat-diff-head {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 12px;
  flex-wrap: wrap;
}
.strat-diff-arrow {
  font-size: 18px;
  color: var(--el-color-primary);
}
.strat-diff-time {
  margin-left: auto;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
.strat-diff-name {
  font-weight: 600;
  margin-right: 6px;
}
.strat-diff-changed {
  color: var(--el-color-warning);
  font-weight: 600;
  background: var(--el-color-warning-light-9);
  padding: 1px 4px;
  border-radius: 3px;
}
.code-tag {
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 11px;
  color: var(--el-text-color-secondary);
}
</style>
