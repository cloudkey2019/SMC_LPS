<script setup lang="ts">
/**
 * APS V1 4号位 — Domain 字典接口不可用徽标
 *
 * 用法：在 domainFilter 下拉旁放置
 *   <DomainUnavailableBadge />
 *
 * 行为：
 *  - domainStore.fetchStatus === 'unavailable' → 红点 chip + tooltip
 *  - 其它状态 → 不渲染（节省空间）
 *
 * 数据源：useDomainStore().fetchStatus
 * 配套：domain.ts API 层 EndpointUnavailableError（404 触发）
 * 适用：Gantt / Order / Pi 等依赖 activeDomains 的页面顶部下拉
 */
import { storeToRefs } from 'pinia'
import { useDomainStore } from '@/store/modules/aps/domain'
import { ElTooltip } from 'element-plus'

const domainStore = useDomainStore()
const { fetchStatus } = storeToRefs(domainStore)
</script>

<template>
  <ElTooltip
    v-if="fetchStatus === 'unavailable'"
    content="排程域数据暂不可用，当前下拉为空属正常表现。请稍后重试，或联系系统管理员。"
    placement="top"
    :show-after="100"
  >
    <span class="domain-unavailable-badge">
      <Icon icon="vi-mdi:alert-circle" />
      <span class="badge-text">排程域数据不可用</span>
    </span>
  </ElTooltip>
</template>

<style lang="less" scoped>
.domain-unavailable-badge {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 2px 10px;
  margin-left: 4px;
  font-size: 12px;
  color: #b91c1c;
  background: #fee2e2;
  border: 1px solid #fca5a5;
  border-radius: 12px;
  cursor: help;
  vertical-align: middle;

  .iconify {
    font-size: 14px;
  }
}
</style>
