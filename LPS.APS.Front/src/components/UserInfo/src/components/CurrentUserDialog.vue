<script setup lang="ts">
/**
 * APS V1 4号位 — 当前用户上下文详情（Current User）
 *
 * B2（权限文档 §9.2 / 实施清单 L624-630）：4号位 需展示 Current User 状态 + 权限列表 + Scope 列表。
 * U40 已验 `GET /api/auth/me` 返回 permissions[] + 4 类 scope；本组件补「展示」缺口（layout 顶栏入口）。
 *
 * 数据源：useApsAuthStore（真实模式 = /me；mock 模式 = 角色预设）。只读展示，无写操作。
 * 范围语义：dataScope 各维度空数组 = 全局未限制（isGlobal=true）；非空 = 受限白名单。
 */
import { computed } from 'vue'
import { ElDialog, ElDescriptions, ElDescriptionsItem, ElTag, ElEmpty } from 'element-plus'
import { useApsAuthStore } from '@/store/modules/aps/auth'
import { ROLE_LABELS, type RoleKey } from '@/api/aps-v1/types'

const props = defineProps<{ modelValue: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: boolean): void }>()

const visible = computed({
  get: () => props.modelValue,
  set: (v) => emit('update:modelValue', v)
})

const apsAuth = useApsAuthStore()
const info = computed(() => apsAuth.userInfo)

/** 4 维数据范围（空 = 全局未限制） */
const scopeRows = computed(() => {
  const s = apsAuth.dataScope
  return [
    { label: 'Factory 工厂', values: s.factoryCodes as string[] },
    { label: 'ProductFamily 产品族', values: s.productFamilyCodes as string[] },
    { label: 'Department 部门', values: s.departmentCodes as string[] },
    { label: 'Domain 域', values: s.domainKeys as string[] }
  ]
})

function roleLabel(r: RoleKey): string {
  return ROLE_LABELS[r] ?? r
}
</script>

<template>
  <ElDialog v-model="visible" title="当前用户（Current User）" width="660px" append-to-body>
    <template v-if="info">
      <ElDescriptions :column="2" border size="small">
        <ElDescriptionsItem label="工号 UserCode">{{ info.userCode }}</ElDescriptionsItem>
        <ElDescriptionsItem label="姓名 UserName">{{ info.userName }}</ElDescriptionsItem>
        <ElDescriptionsItem label="用户 ID">{{ info.userId }}</ElDescriptionsItem>
        <ElDescriptionsItem label="范围模式">
          <ElTag :type="info.isGlobal ? 'success' : 'warning'" size="small">
            {{ info.isGlobal ? '全局放行（未限制）' : '受限白名单' }}
          </ElTag>
        </ElDescriptionsItem>
      </ElDescriptions>

      <!-- 角色列表 -->
      <div class="cu-section">
        <div class="cu-title">角色（{{ apsAuth.roles.length }}）</div>
        <div class="cu-tags">
          <ElTag v-for="r in apsAuth.roles" :key="r" type="primary" effect="plain" size="small">
            {{ roleLabel(r) }}（{{ r }}）
          </ElTag>
          <span v-if="apsAuth.roles.length === 0" class="cu-muted">无角色</span>
        </div>
      </div>

      <!-- 功能权限码列表 -->
      <div class="cu-section">
        <div class="cu-title">功能权限码（{{ apsAuth.permissions.length }}）</div>
        <div class="cu-tags cu-perms">
          <ElTag v-for="p in apsAuth.permissions" :key="p" type="info" effect="plain" size="small">
            {{ p }}
          </ElTag>
          <span v-if="apsAuth.permissions.length === 0" class="cu-muted">无权限码</span>
        </div>
      </div>

      <!-- 数据范围 4 维 -->
      <div class="cu-section">
        <div class="cu-title">数据范围（4 维 Scope）</div>
        <div v-for="row in scopeRows" :key="row.label" class="cu-scope-row">
          <span class="cu-scope-label">{{ row.label }}</span>
          <span class="cu-scope-vals">
            <template v-if="row.values.length === 0">
              <ElTag type="success" effect="plain" size="small">全局未限制</ElTag>
            </template>
            <template v-else>
              <ElTag v-for="v in row.values" :key="v" effect="plain" size="small">{{ v }}</ElTag>
            </template>
          </span>
        </div>
      </div>
    </template>
    <ElEmpty v-else description="未加载 APS 用户上下文（请先进入 APS 页面或登录）" />
  </ElDialog>
</template>

<style scoped lang="less">
.cu-section {
  margin-top: 16px;
}
.cu-title {
  font-weight: 600;
  margin-bottom: 8px;
  font-size: 13px;
}
.cu-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
.cu-perms {
  max-height: 200px;
  overflow-y: auto;
}
.cu-muted {
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.cu-scope-row {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  margin-bottom: 8px;
}
.cu-scope-label {
  flex: 0 0 150px;
  font-size: 12px;
  color: var(--el-text-color-regular);
  padding-top: 3px;
}
.cu-scope-vals {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  flex: 1;
}
</style>
