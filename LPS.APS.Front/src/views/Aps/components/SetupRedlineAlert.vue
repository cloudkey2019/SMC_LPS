<!--
  APS V1 4号位 — Setup 维护红线 Alert（v1.5 Setup 专项 §7，页面 2.5）

  用途：贯穿所有 Setup 维护页面顶部，展示数据结构红线 + 业务红线
  复用：SetupExact.vue / SetupDefault.vue / SetupUncovered.vue / SetupDiff.vue

  红线清单（§7.1 数据结构红线 6 项 + §7.2 业务红线 4 项）：
   - 不得出现 FromOperationCode/ToOperationCode（v1.2 收口版废止"前小工序 + 后小工序"）
   - 不得出现 SetupAttribute（§1.1 已废止）
   - 同产品连续 = 0 分钟（FromMaterial == ToMaterial 不存规则）
   - SetupMinutes 必须 > 0（EXACT/DEFAULT 规则）
   - EXACT 必须填 FromMaterial/ToMaterial；DEFAULT 必须都空
   - 仅 DRAFT 状态的 RuleSetVersion 可编辑（PUBLISHED/ACTIVE 不可改）
   - 共同合法设备外不允许保存（§3.3）
   - 同 5 元组 EXACT + DEFAULT 重复 → 后端 422
   - 同 7 元组 EXACT 重复 → 后端 422

  展示形态（2026-09-20 走查收口）：默认收起为一行标题 + 展开按钮，展开态存 localStorage
  （v1.5 §2 页面 2.5 要求红线贯穿页面顶部，收起不删除＝契约仍满足；全文一字不少）
-->

<script setup lang="ts">
import { ref } from 'vue'
import { ElAlert, ElButton } from 'element-plus'

defineOptions({ name: 'SetupRedlineAlert' })

interface Props {
  /** 页面类型（用于定制红线文案） */
  pageType?: 'exact' | 'default' | 'uncovered' | 'diff'
}

withDefaults(defineProps<Props>(), {
  pageType: 'exact'
})

const STORAGE_KEY = 'aps.setup.redline.expanded'
const expanded = ref(localStorage.getItem(STORAGE_KEY) === '1')

function toggleExpanded() {
  expanded.value = !expanded.value
  localStorage.setItem(STORAGE_KEY, expanded.value ? '1' : '0')
}
</script>

<template>
  <ElAlert type="warning" :closable="false" show-icon class="setup-redline-alert">
    <template #title>
      <div class="redline-title-row">
        <span> <strong>换型（Setup）维护规则</strong>（违规操作会被页面拦截或被服务端拒绝） </span>
        <ElButton link type="primary" size="small" @click="toggleExpanded">
          {{ expanded ? '收起 ▲' : '展开全文 ▼' }}
        </ElButton>
      </div>
    </template>

    <div v-show="expanded" class="redline-content">
      <div class="redline-section">
        <h4>数据结构规则</h4>
        <ul>
          <li> <strong>只按工序维度维护</strong>：规则中不含"前小工序 / 后小工序"字段 </li>
          <li><strong>换型属性字段已废止</strong>：不再使用该字段</li>
          <li> <strong>同产品连续 = 0 分钟</strong>：前后产品相同时不存规则（属兜底自动命中） </li>
          <li>
            <strong>换型分钟数 &gt; 0</strong>：明确转换规则 / 默认换型规则的换型分钟数必须大于 0
          </li>
          <li v-if="pageType === 'exact'">
            <strong>明确转换规则必须填前后产品</strong>：前产品与后产品均为必填
          </li>
          <li v-if="pageType === 'default'">
            <strong>默认换型规则必须留空前后产品</strong
            >：前产品与后产品都必须为空（与明确转换规则互斥）
          </li>
        </ul>
      </div>

      <div class="redline-section">
        <h4>业务规则</h4>
        <ul>
          <li>
            <strong>仅草稿（DRAFT）可编辑</strong>：版本已发布或已生效时，编辑 / 新增按钮不可用，
            服务端也会拒绝
          </li>
          <li v-if="pageType === 'exact'">
            <strong>共同合法设备约束</strong>：选中非共同合法设备时无法提交
          </li>
          <li>
            <strong>唯一性约束</strong>：同一条明确转换规则 / 默认换型规则
            重复时会被服务端拒绝，并返回业务提示
          </li>
        </ul>
      </div>

      <div v-if="pageType === 'exact' || pageType === 'default'" class="redline-section">
        <h4>本页不做的事</h4>
        <ul>
          <li>❌ 不做物料需求追溯（Pegging）计算</li>
          <li>❌ 不做排程序列优化</li>
          <li>❌ 不做跨产品全量枚举</li>
          <li>❌ 不做换型属性字段维护</li>
          <li>❌ 不做换型算法参数推导</li>
        </ul>
      </div>
    </div>
  </ElAlert>
</template>

<style scoped>
.setup-redline-alert {
  margin-bottom: 16px;
}

.redline-title-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
}

.redline-content {
  margin-top: 8px;
  font-size: 13px;
  line-height: 1.6;
}

.redline-section {
  margin-bottom: 12px;
}

.redline-section:last-child {
  margin-bottom: 0;
}

.redline-section h4 {
  margin: 0 0 6px 0;
  font-size: 13px;
  font-weight: 600;
  color: var(--el-text-color-primary);
}

.redline-section ul {
  margin: 0;
  padding-left: 20px;
}

.redline-section li {
  margin-bottom: 4px;
  color: var(--el-text-color-regular);
}

.redline-section li:last-child {
  margin-bottom: 0;
}

.redline-section code {
  background-color: var(--el-fill-color-light);
  padding: 2px 4px;
  border-radius: 3px;
  font-family: var(--el-font-family-mono);
  font-size: 12px;
  color: var(--el-color-danger);
}

.redline-section strong {
  color: var(--el-text-color-primary);
  font-weight: 600;
}
</style>
