<!--
  APS V1 4号位 — 资源日历维护页（单页 3 Tab）

  依据：
    - 冻结文档 v1.3《APS_V1_Resource_Calendar资源日历能力补充冻结方案_v1.3_人工能力槽模型与职责边界修订版》
        §八 4号位 = 资源能力维护页面 Owner，维护 3 类对象：
          ① 设备资源日历（Resource + ResourceCalendarSlot）
          ② 人工能力槽（ManualCapacitySlot）
          ③ 人工能力槽日历（ManualCapacitySlotCalendar）
        §九 缺省语义：无有效日历 = 不可用（禁止 7×24 缺省）
    - 5号位 2026-09-24《ResourceCalendar与ManualCapacity接口对接函》（9 端点 / 6 权限码）

  页面结构：
    Tab1 设备资源日历：资源选择（过渡数据源）→ 窗口列表 → 批量铺窗 / 删窗
    Tab2 人工能力槽  ：部门/工序过滤 + 含软删开关 → 主档列表 → 新增 / 查看窗口 / 铺窗 / 软删
    Tab3 人工槽日历  ：由 Tab2 选中行驱动 → 窗口列表 → 批量铺窗 / 删窗

  RBAC（v1.2 §23.1）：
    - 路由级 apsRequiredPermissions = [aps.resource_calendar.view, aps.manual_capacity.view]（OR 语义）
    - 按钮级：canEditDevice / canDeleteDevice / canEditManual / canDeleteManual

  ⚠️ 三个已知缺口（已发函 5号位 2026-09-24；过渡方案见代码注释，**不在页面向用户展示**）：
    G1 无资源主表列表端点   → 过渡用「计划版本甘特资源池」，可能遗漏未排资源
    G2 无生产部门列表端点   → 过渡从资源池去重
    G4 DTO 无 hasCalendar   → 状态由"窗口数组空"推导，且仅选中行才查证
-->
<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { storeToRefs } from 'pinia'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  ElAlert,
  ElButton,
  ElCard,
  ElDropdown,
  ElDropdownItem,
  ElDropdownMenu,
  ElEmpty,
  ElForm,
  ElFormItem,
  ElInput,
  ElInputNumber,
  ElOption,
  ElSelect,
  ElSwitch,
  ElTable,
  ElTableColumn,
  ElTabPane,
  ElTabs,
  ElTag,
  ElTooltip
} from 'element-plus'
import {
  CALENDAR_CONFIG_META,
  type BulkWindowDraft,
  type ManualCapacitySlotDto,
  type ManualSlotCalendarDto,
  type ManualSlotDraft,
  type ResourceCalendarEntryDto,
  type ResourceCandidate
} from '@/api/aps-v1'
import type { RoleKey } from '@/api/aps-v1/types'
import { useApsAuthStore } from '@/store/modules/aps/auth'
import { useResourceCalendarStore } from '@/store/modules/aps/resourceCalendar'
import CalendarBulkDialog from './components/CalendarBulkDialog.vue'
import ManualSlotCreateDialog from './components/ManualSlotCreateDialog.vue'

const apsAuth = useApsAuthStore()
const store = useResourceCalendarStore()
const {
  resourceCandidates,
  candidateNote,
  candidateIsFallback,
  currentResourceId,
  deviceWindows,
  deviceWindowsLoaded,
  deviceCalendarStatus,
  slots,
  departmentCandidates,
  currentSlotId,
  slotWindows,
  currentSlot,
  loading,
  saving,
  error,
  canViewDevice,
  canEditDevice,
  canDeleteDevice,
  canViewManual,
  canEditManual,
  canDeleteManual
} = storeToRefs(store)

/* ===== Tab 控制 ===== */
type TabName = 'device' | 'manual' | 'manual-calendar'
const activeTab = ref<TabName>('device')
const loadedTabs = ref<Set<TabName>>(new Set())

async function onTabChange(name: TabName): Promise<void> {
  if (loadedTabs.value.has(name)) return
  loadedTabs.value.add(name)
  if (name === 'device') {
    await store.loadResourceCandidates()
  } else if (name === 'manual') {
    await Promise.all([store.loadSlots(), store.loadDepartmentCandidates()])
  }
}

/* ===== Tab1：设备资源日历 ===== */
const manualResourceId = ref<number | undefined>(undefined)
const deviceDialogVisible = ref(false)

/** 资源下拉候选（过渡数据源；G1） */
const resourceOptions = computed<ResourceCandidate[]>(() => resourceCandidates.value)

async function pickResource(resourceId: number): Promise<void> {
  await store.loadDeviceWindows(resourceId)
}

async function loadManualResource(): Promise<void> {
  const id = Number(manualResourceId.value)
  if (!Number.isFinite(id) || id <= 0) {
    ElMessage.warning('资源 ID 必须为正整数')
    return
  }
  await store.loadDeviceWindows(id)
}

function openDeviceDialog(): void {
  if (currentResourceId.value == null) {
    ElMessage.warning('请先选择或手填资源并加载窗口')
    return
  }
  deviceDialogVisible.value = true
}

async function submitDeviceBulk(draft: BulkWindowDraft): Promise<void> {
  if (currentResourceId.value == null) return
  const inserted = await store.createDeviceWindows({
    resourceId: currentResourceId.value,
    startDate: `${draft.startDate}T00:00:00`,
    days: draft.days,
    startTime: draft.startTime,
    endTime: draft.endTime,
    availableFlag: draft.availableFlag,
    remark: draft.remark
  })
  if (inserted != null) {
    ElMessage.success(`已生成 ${inserted} 条设备窗口`)
    deviceDialogVisible.value = false
  }
}

async function removeDeviceWindow(row: ResourceCalendarEntryDto): Promise<void> {
  const ok = await ElMessageBox.confirm(
    `将物理删除设备窗口 #${row.id}（${fmt(row.startTime)} ~ ${fmt(row.endTime)}）。\n` +
      '物理删除后行消失、不可恢复，排产追溯将不再含该窗口。确认删除？',
    '删除设备窗口',
    { type: 'warning', confirmButtonText: '确认删除', cancelButtonText: '取消' }
  ).catch(() => false)
  if (!ok) return
  const done = await store.removeDeviceWindow(row.id, row.resourceId)
  if (done) ElMessage.success('设备窗口已删除')
}

/** 当前资源的展示名 */
const currentResourceLabel = computed<string>(() => {
  const id = currentResourceId.value
  if (id == null) return ''
  const hit = resourceOptions.value.find((r) => r.resourceId === id)
  if (!hit) return `资源 #${id}`
  return `${hit.resourceCode} ${hit.resourceName}`
})

/* ===== Tab2：人工能力槽 ===== */
const slotDialogVisible = ref(false)
const filterDepartmentId = ref<number | undefined>(undefined)
const filterOperationName = ref<string>('')
const filterIncludeInactive = ref<boolean>(false)

async function searchSlots(): Promise<void> {
  await store.loadSlots({
    departmentId: filterDepartmentId.value ?? undefined,
    operationName: filterOperationName.value.trim() || undefined,
    includeInactive: filterIncludeInactive.value
  })
}

function resetSlotFilter(): void {
  filterDepartmentId.value = undefined
  filterOperationName.value = ''
  filterIncludeInactive.value = false
  void store.loadSlots({})
}

async function submitCreateSlot(draft: ManualSlotDraft): Promise<void> {
  const created = await store.createSlot({
    manualSlotId: 0,
    productionDepartmentId: draft.productionDepartmentId,
    operationName: draft.operationName,
    slotCode: draft.slotCode,
    isActive: true
  })
  if (created) {
    ElMessage.success(`人工能力槽「${created.slotCode}」已新增`)
    slotDialogVisible.value = false
  }
}

/** 查看某人工槽窗口 → 载入并跳 Tab3 */
async function viewSlotWindows(row: ManualCapacitySlotDto): Promise<void> {
  await store.loadSlotWindows(row.manualSlotId)
  activeTab.value = 'manual-calendar'
  loadedTabs.value.add('manual-calendar')
}

async function disableSlot(row: ManualCapacitySlotDto): Promise<void> {
  const ok = await ElMessageBox.confirm(
    `将软删人工能力槽「${row.slotCode}」（${row.operationName}）：停用后行保留、查询默认不含。\n\n` +
      '影响：该能力槽不再参与排产；已有日历窗口不会自动清除，如需清理请到「人工槽日历」页签删除。',
    '软删人工能力槽',
    { type: 'warning', confirmButtonText: '确认停用', cancelButtonText: '取消' }
  ).catch(() => false)
  if (!ok) return
  const done = await store.disableSlot(row.manualSlotId)
  if (done) ElMessage.success(`人工能力槽 ${row.slotCode} 已停用`)
}

/** 从 Tab2 直接对该行发起铺窗（切换到 Tab3 后打开 Dialog） */
const pendingSlotBulkId = ref<number | null>(null)
async function bulkForSlot(row: ManualCapacitySlotDto): Promise<void> {
  if (!row.isActive) {
    ElMessage.warning('已停用的人工槽不可铺窗')
    return
  }
  await viewSlotWindows(row)
  pendingSlotBulkId.value = row.manualSlotId
  slotBulkDialogVisible.value = true
}

/* ===== Tab3：人工槽日历 ===== */
const slotBulkDialogVisible = ref(false)

function openSlotBulkDialog(): void {
  if (currentSlotId.value == null) return
  if (currentSlot.value && !currentSlot.value.isActive) {
    ElMessage.warning('已停用的人工槽不可铺窗')
    return
  }
  slotBulkDialogVisible.value = true
}

async function submitSlotBulk(draft: BulkWindowDraft): Promise<void> {
  const id = currentSlotId.value
  if (id == null) return
  const inserted = await store.createSlotWindows({
    manualSlotId: id,
    startDate: `${draft.startDate}T00:00:00`,
    days: draft.days,
    startTime: draft.startTime,
    endTime: draft.endTime,
    availableFlag: draft.availableFlag,
    remark: draft.remark
  })
  if (inserted != null) {
    ElMessage.success(`已生成 ${inserted} 条人工槽窗口`)
    slotBulkDialogVisible.value = false
  }
}

async function removeSlotWindow(row: ManualSlotCalendarDto): Promise<void> {
  const ok = await ElMessageBox.confirm(
    `将物理删除人工槽窗口 #${row.id}（${fmt(row.startTime)} ~ ${fmt(row.endTime)}）。确认删除？`,
    '删除人工槽窗口',
    { type: 'warning', confirmButtonText: '确认删除', cancelButtonText: '取消' }
  ).catch(() => false)
  if (!ok) return
  const done = await store.removeSlotWindow(row.id, row.manualSlotId)
  if (done) ElMessage.success('人工槽窗口已删除')
}

const currentSlotLabel = computed<string>(() => {
  const s = currentSlot.value
  if (!s) return ''
  return `${s.slotCode}（${s.operationName}｜${s.departmentName ?? '未知部门'}）`
})

const currentSlotWindowStatus = computed(() =>
  currentSlotId.value == null ? 'UNKNOWN' : store.slotCalendarStatus(currentSlotId.value)
)

/* ===== 公共 ===== */
/** ISO 时间 → "YYYY-MM-DD HH:mm" */
function fmt(iso?: string): string {
  if (!iso) return '—'
  return iso.replace('T', ' ').slice(0, 16)
}

function windowCountLabel(rows: Array<unknown>): string {
  return `${rows.length} 条`
}

/* ===== mock 角色切换器（仅 mock 模式显示；与甘特图页同一范式） =====
 * 真实绑定已落地（3号位 脚本 APS_Auth_resource_calendar_role_binding_20260924.sql）：
 *   viewer = view；planner = view + edit（不含 delete）；admin.aps / admin.system = 全 6 码。
 * mock 下用本切换器验证按钮级门控（planner 无可删按钮 / admin 可删）。
 */
const ROLE_TAG: Record<RoleKey, 'success' | 'warning' | 'info' | 'primary' | 'danger'> = {
  'aps.viewer.management': 'info',
  'aps.planner': 'success',
  'aps.admin.aps': 'warning',
  'aps.supervisor.workshop': 'primary',
  'aps.coordinator.material': 'primary',
  'aps.service.api': 'primary',
  'aps.admin.system': 'danger'
}

function switchMockRole(role: RoleKey): void {
  apsAuth.mockSwitchRole(role)
  ElMessage.success(`已切换角色：${role}（仅演示模式）`)
}

onMounted(async () => {
  if (!canViewDevice.value && !canViewManual.value) {
    ElMessage.warning('当前操作者无资源日历查看权限，请联系系统管理员授予')
  }
  loadedTabs.value.add('device')
  await store.loadResourceCandidates()
})

onBeforeUnmount(() => {
  store.reset()
})
</script>

<template>
  <div class="rc-page">
    <!-- 页面说明 -->
    <ElCard class="rc-intro" shadow="never">
      <template #header>
        <div class="intro-header">
          <span class="intro-title">资源日历维护</span>
          <span class="intro-sub">设备资源日历 / 人工能力槽 / 人工槽日历</span>
          <ElTooltip placement="bottom-start" effect="light" :show-after="150">
            <span class="intro-help" tabindex="0">?</span>
            <template #content>
              <div class="help-pop">
                <p class="help-pop-title">无有效日历窗口 = 不可用</p>
                <p>设备与人工能力槽同一口径：系统不得按 7×24 小时缺省兜底。</p>
                <p>窗口列表为空，即该资源当前<strong>不可排</strong>。</p>
              </div>
            </template>
          </ElTooltip>
          <!-- mock 角色切换器（仅 mock 模式显示；生产模式自动隐藏） -->
          <ElDropdown
            v-if="apsAuth.mockAvailableRoles.length > 0"
            trigger="click"
            class="role-switcher"
            @command="switchMockRole"
          >
            <ElTag
              :type="ROLE_TAG[apsAuth.mockActiveRole]"
              effect="dark"
              class="role-badge"
              title="点击切换角色（仅演示模式可用）"
            >
              {{ apsAuth.mockActiveRole }}
            </ElTag>
            <template #dropdown>
              <ElDropdownMenu>
                <ElDropdownItem
                  v-for="r in apsAuth.mockAvailableRoles"
                  :key="r"
                  :command="r"
                  :disabled="r === apsAuth.mockActiveRole"
                >
                  <ElTag :type="ROLE_TAG[r]" size="small" effect="plain">{{ r }}</ElTag>
                  <span class="role-hint">
                    {{
                      r === 'aps.viewer.management'
                        ? '仅 view（可看不可改）'
                        : r === 'aps.planner'
                          ? 'view + edit（不含 delete）'
                          : r === 'aps.admin.aps' || r === 'aps.admin.system'
                            ? '全 6 码（含 delete）'
                            : ''
                    }}
                  </span>
                </ElDropdownItem>
              </ElDropdownMenu>
            </template>
          </ElDropdown>
        </div>
      </template>
    </ElCard>

    <!-- 错误条 -->
    <ElAlert
      v-if="error"
      type="error"
      :closable="true"
      show-icon
      class="rc-alert rc-error"
      title="操作失败"
      @close="store.clearError()"
    >
      {{ error }}
    </ElAlert>

    <ElCard shadow="never">
      <ElTabs v-model="activeTab" @tab-change="(n) => onTabChange(n as TabName)">
        <!-- ============ Tab1 设备资源日历 ============ -->
        <ElTabPane label="设备资源日历" name="device">
          <ElForm inline label-position="right" label-width="80px" class="rc-toolbar">
            <ElFormItem v-if="resourceOptions.length > 0" label="资源">
              <ElSelect
                :model-value="currentResourceId ?? undefined"
                filterable
                clearable
                placeholder="从资源池选择"
                style="width: 300px"
                @change="(v: number) => v && pickResource(v)"
              >
                <ElOption
                  v-for="r in resourceOptions"
                  :key="r.resourceId"
                  :label="`${r.resourceCode} ${r.resourceName}`"
                  :value="r.resourceId"
                />
              </ElSelect>
            </ElFormItem>
            <ElFormItem label="手填资源 ID">
              <ElInputNumber v-model="manualResourceId" :min="1" :step="1" style="width: 140px" />
              <ElButton class="rc-btn-gap" :loading="loading" @click="loadManualResource">
                加载窗口
              </ElButton>
            </ElFormItem>
            <ElFormItem>
              <ElButton
                type="primary"
                :disabled="currentResourceId == null || !canEditDevice"
                @click="openDeviceDialog"
              >
                批量铺窗
              </ElButton>
            </ElFormItem>
          </ElForm>

          <div v-if="candidateIsFallback && candidateNote" class="rc-hint rc-source-hint">
            {{ candidateNote }}
          </div>

          <div v-if="currentResourceId != null" class="rc-status-row">
            <span class="rc-status-label">当前资源：</span>
            <strong>{{ currentResourceLabel }}</strong>
            <ElTag
              :type="CALENDAR_CONFIG_META[deviceCalendarStatus].tagType"
              class="rc-btn-gap"
              effect="dark"
            >
              日历 {{ CALENDAR_CONFIG_META[deviceCalendarStatus].label }}
            </ElTag>
            <span class="rc-hint">{{
              CALENDAR_CONFIG_META[deviceCalendarStatus].description
            }}</span>
            <span class="rc-hint">窗口：{{ windowCountLabel(deviceWindows) }}</span>
          </div>

          <ElEmpty
            v-if="currentResourceId == null"
            description="请先在上方选择资源或手填资源 ID 后点击「加载窗口」"
          />
          <template v-else>
            <ElAlert
              v-if="deviceWindowsLoaded && deviceWindows.length === 0"
              type="error"
              :closable="false"
              show-icon
              class="rc-alert"
              title="该设备未配日历 = 不可排"
            >
              无有效窗口，排产判定该设备不可用。请点击「批量铺窗」补窗口后，设备才可参与排程。
            </ElAlert>

            <ElTable
              v-if="deviceWindows.length > 0"
              :data="deviceWindows"
              :loading="loading"
              border
              stripe
              row-key="id"
              size="small"
              style="width: 100%"
            >
              <ElTableColumn prop="id" label="窗口编号" width="120" fixed="left" />
              <ElTableColumn prop="resourceCode" label="资源编码" width="120" />
              <ElTableColumn prop="resourceName" label="资源名称" width="150" />
              <ElTableColumn label="开始时间" min-width="170">
                <template #default="{ row }">{{ fmt(row.startTime) }}</template>
              </ElTableColumn>
              <ElTableColumn label="结束时间" min-width="170">
                <template #default="{ row }">{{ fmt(row.endTime) }}</template>
              </ElTableColumn>
              <ElTableColumn label="可用" width="90" align="center">
                <template #default="{ row }">
                  <ElTag :type="row.availableFlag ? 'success' : 'danger'" size="small">
                    {{ row.availableFlag ? '可用' : '禁用' }}
                  </ElTag>
                </template>
              </ElTableColumn>
              <ElTableColumn prop="remark" label="备注" min-width="160" show-overflow-tooltip />
              <ElTableColumn label="操作" width="110" fixed="right" v-if="canDeleteDevice">
                <template #default="{ row }">
                  <ElButton
                    type="danger"
                    size="small"
                    :loading="saving"
                    @click="removeDeviceWindow(row)"
                  >
                    删除
                  </ElButton>
                </template>
              </ElTableColumn>
            </ElTable>
          </template>
        </ElTabPane>

        <!-- ============ Tab2 人工能力槽 ============ -->
        <ElTabPane label="人工能力槽" name="manual">
          <ElForm inline label-position="right" label-width="80px" class="rc-toolbar">
            <ElFormItem label="部门">
              <ElSelect
                v-if="departmentCandidates.length > 0"
                v-model="filterDepartmentId"
                filterable
                clearable
                placeholder="全部部门"
                style="width: 220px"
              >
                <ElOption
                  v-for="d in departmentCandidates"
                  :key="d.productionDepartmentId"
                  :label="d.productionDepartmentName ?? '未知部门'"
                  :value="d.productionDepartmentId"
                />
              </ElSelect>
              <ElInputNumber
                v-else
                v-model="filterDepartmentId"
                :min="1"
                :step="1"
                style="width: 140px"
              />
            </ElFormItem>
            <ElFormItem label="小工序">
              <ElInput
                v-model="filterOperationName"
                placeholder="如 精修（自由文本）"
                clearable
                style="width: 180px"
                @keyup.enter="searchSlots"
              />
            </ElFormItem>
            <ElFormItem label="含已停用">
              <ElSwitch v-model="filterIncludeInactive" />
            </ElFormItem>
            <ElFormItem>
              <ElButton type="primary" :loading="loading" @click="searchSlots">查询</ElButton>
              <ElButton :disabled="loading" @click="resetSlotFilter">重置</ElButton>
            </ElFormItem>
            <ElFormItem>
              <ElButton type="primary" :disabled="!canEditManual" @click="slotDialogVisible = true">
                新增人工能力槽
              </ElButton>
            </ElFormItem>
          </ElForm>

          <ElEmpty
            v-if="slots.length === 0 && !loading"
            description="无人工能力槽；可点击「新增人工能力槽」创建"
          />

          <ElTable
            v-else
            :data="slots"
            :loading="loading"
            border
            stripe
            row-key="manualSlotId"
            size="small"
            :row-class-name="({ row }) => (!row.isActive ? 'rc-row-disabled' : '')"
            style="width: 100%"
          >
            <ElTableColumn prop="manualSlotId" label="人工槽编号" width="130" fixed="left" />
            <ElTableColumn prop="departmentName" label="生产部门" width="150">
              <template #default="{ row }">
                {{ row.departmentName ?? '未知部门' }}
              </template>
            </ElTableColumn>
            <ElTableColumn prop="operationName" label="小工序" width="140" />
            <ElTableColumn prop="slotCode" label="能力槽编码" width="150" />
            <ElTableColumn label="状态" width="100" align="center">
              <template #default="{ row }">
                <ElTag :type="row.isActive ? 'success' : 'info'" size="small">
                  {{ row.isActive ? '启用' : '已停用' }}
                </ElTag>
              </template>
            </ElTableColumn>
            <ElTableColumn label="日历" width="120" align="center">
              <template #default="{ row }">
                <ElTag
                  :type="CALENDAR_CONFIG_META[store.slotCalendarStatus(row.manualSlotId)].tagType"
                  size="small"
                  effect="plain"
                >
                  {{ CALENDAR_CONFIG_META[store.slotCalendarStatus(row.manualSlotId)].label }}
                </ElTag>
              </template>
            </ElTableColumn>
            <ElTableColumn label="操作" width="260" fixed="right">
              <template #default="{ row }">
                <ElButton size="small" @click="viewSlotWindows(row)">查看窗口</ElButton>
                <ElButton
                  size="small"
                  type="primary"
                  :disabled="!canEditManual || !row.isActive"
                  @click="bulkForSlot(row)"
                >
                  铺窗
                </ElButton>
                <ElButton
                  size="small"
                  type="danger"
                  :disabled="!canDeleteManual || !row.isActive"
                  :loading="saving"
                  @click="disableSlot(row)"
                >
                  软删
                </ElButton>
              </template>
            </ElTableColumn>
          </ElTable>
        </ElTabPane>

        <!-- ============ Tab3 人工槽日历 ============ -->
        <ElTabPane label="人工槽日历" name="manual-calendar">
          <ElEmpty
            v-if="currentSlotId == null"
            description="请先在「人工能力槽」页签中点击某行的「查看窗口」或「铺窗」"
          />
          <template v-else>
            <div class="rc-status-row">
              <span class="rc-status-label">当前人工槽：</span>
              <strong>{{ currentSlotLabel }}</strong>
              <ElTag
                :type="CALENDAR_CONFIG_META[currentSlotWindowStatus].tagType"
                class="rc-btn-gap"
                effect="dark"
              >
                日历 {{ CALENDAR_CONFIG_META[currentSlotWindowStatus].label }}
              </ElTag>
              <span class="rc-hint">{{
                CALENDAR_CONFIG_META[currentSlotWindowStatus].description
              }}</span>
              <span class="rc-hint">窗口：{{ windowCountLabel(slotWindows) }}</span>
              <ElButton
                class="rc-btn-gap"
                size="small"
                :loading="loading"
                @click="store.loadSlotWindows(currentSlotId)"
              >
                刷新
              </ElButton>
              <ElButton
                type="primary"
                size="small"
                :disabled="!canEditManual || (currentSlot ? !currentSlot.isActive : true)"
                @click="openSlotBulkDialog"
              >
                批量铺窗
              </ElButton>
            </div>

            <ElAlert
              v-if="currentSlot && !currentSlot.isActive"
              type="warning"
              :closable="false"
              show-icon
              class="rc-alert"
              title="该人工槽已停用"
            >
              停用后不再参与排产；窗口保留，不会被自动清理。
            </ElAlert>

            <ElAlert
              v-if="slotWindows.length === 0"
              type="error"
              :closable="false"
              show-icon
              class="rc-alert"
              title="该人工槽未配日历 = 不可排"
            >
              无有效窗口，排产判定该人工槽不可用。请点击「批量铺窗」补窗口。
            </ElAlert>

            <ElTable
              v-if="slotWindows.length > 0"
              :data="slotWindows"
              :loading="loading"
              border
              stripe
              row-key="id"
              size="small"
              style="width: 100%"
            >
              <ElTableColumn prop="id" label="窗口编号" width="130" fixed="left" />
              <ElTableColumn prop="manualSlotId" label="人工槽编号" width="130" />
              <ElTableColumn label="开始时间" min-width="170">
                <template #default="{ row }">{{ fmt(row.startTime) }}</template>
              </ElTableColumn>
              <ElTableColumn label="结束时间" min-width="170">
                <template #default="{ row }">{{ fmt(row.endTime) }}</template>
              </ElTableColumn>
              <ElTableColumn label="可用" width="90" align="center">
                <template #default="{ row }">
                  <ElTag :type="row.availableFlag ? 'success' : 'danger'" size="small">
                    {{ row.availableFlag ? '可用' : '禁用' }}
                  </ElTag>
                </template>
              </ElTableColumn>
              <ElTableColumn prop="remark" label="备注" min-width="160" show-overflow-tooltip />
              <ElTableColumn label="操作" width="110" fixed="right" v-if="canDeleteManual">
                <template #default="{ row }">
                  <ElButton
                    type="danger"
                    size="small"
                    :loading="saving"
                    @click="removeSlotWindow(row)"
                  >
                    删除
                  </ElButton>
                </template>
              </ElTableColumn>
            </ElTable>
          </template>
        </ElTabPane>
      </ElTabs>
    </ElCard>

    <!-- ===== Dialog：设备铺窗 ===== -->
    <CalendarBulkDialog
      :visible="deviceDialogVisible"
      :submitting="saving"
      mode="DEVICE"
      :target-id="currentResourceId"
      :target-label="currentResourceLabel"
      :existing-windows="deviceWindows"
      :existing-windows-loaded="deviceWindowsLoaded"
      @update:visible="(v) => (deviceDialogVisible = v)"
      @submit="submitDeviceBulk"
    />

    <!-- ===== Dialog：新增人工能力槽 ===== -->
    <ManualSlotCreateDialog
      :visible="slotDialogVisible"
      :submitting="saving"
      :department-candidates="departmentCandidates"
      :is-fallback-source="true"
      @update:visible="(v) => (slotDialogVisible = v)"
      @submit="submitCreateSlot"
    />

    <!-- ===== Dialog：人工槽铺窗 ===== -->
    <CalendarBulkDialog
      :visible="slotBulkDialogVisible"
      :submitting="saving"
      mode="MANUAL_SLOT"
      :target-id="pendingSlotBulkId ?? currentSlotId"
      :target-label="currentSlotLabel"
      :existing-windows="slotWindows"
      :existing-windows-loaded="currentSlotId != null"
      @update:visible="(v) => (slotBulkDialogVisible = v)"
      @submit="submitSlotBulk"
    />
  </div>
</template>

<style scoped>
.rc-page {
  padding: 16px;
}
.rc-intro {
  margin-bottom: 12px;
}
.intro-header {
  display: flex;
  align-items: center;
  gap: 16px;
}
.intro-title {
  font-weight: 600;
  font-size: 15px;
}
.intro-sub {
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.intro-help {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 16px;
  height: 16px;
  border-radius: 50%;
  border: 1px solid var(--el-border-color);
  color: var(--el-text-color-secondary);
  font-size: 11px;
  cursor: help;
  user-select: none;
}
.intro-help:hover {
  border-color: var(--el-color-primary);
  color: var(--el-color-primary);
}
.help-pop {
  max-width: 300px;
  font-size: 12px;
  line-height: 1.7;
}
.help-pop p {
  margin: 0 0 4px;
}
.help-pop-title {
  font-weight: 600;
}
.role-switcher {
  margin-left: auto;
}
.role-badge {
  cursor: pointer;
  font-size: 12px;
  letter-spacing: 0.5px;
  transition: transform 0.15s ease;
}
.role-badge:hover {
  transform: translateY(-1px);
}
.role-hint {
  margin-left: 8px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
.rc-alert {
  margin-top: 10px;
}
.rc-error {
  margin-bottom: 12px;
}
.rc-alert :deep(code) {
  background: rgba(0, 0, 0, 0.05);
  padding: 1px 6px;
  border-radius: 3px;
  font-size: 12px;
}
.rc-toolbar {
  margin-bottom: 6px;
}
.rc-btn-gap {
  margin-left: 10px;
}
.rc-status-row {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 6px;
  margin: 10px 0 12px;
  font-size: 13px;
}
.rc-status-label {
  color: var(--el-text-color-secondary);
}
.rc-hint {
  color: var(--el-text-color-secondary);
  font-size: 12px;
  margin-left: 6px;
}
.rc-source-hint {
  margin-left: 0;
  margin-bottom: 8px;
}
:deep(.rc-row-disabled) {
  opacity: 0.6;
}
</style>
