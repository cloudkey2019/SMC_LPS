/**
 * APS V1 4号位 — 认证 / 当前用户 Pinia store
 *
 * 与项目原 useUserStore 协作：
 *  - 这里只放 APS 业务的 userInfo / 角色 / 权限 / 业务范围
 *  - token 仍由 useUserStore 管理（axios 拦截器读那里）
 *
 * @see 审核报告 §十七.17 认证与权限（3号位 V1 正式职责）
 * @see 审核报告 §十七.17.2 业务范围权限（Factory / ProductFamily / Department / Domain）
 * @see 审核报告 §二十五 7 项权限场景
 */

import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { apsAuthApi, type UserInfo, type RoleKey, APS_USE_MOCK } from '@/api/aps-v1'
import { useUserStoreWithOut } from '@/store/modules/user'
import { MOCK_ROLE_SCOPE_PRESETS, normalizeScope, type BusinessScope } from './scope'

/** mock 模式可选角色列表（P2 角色门控验证用；生产模式不会暴露此列表）
 *  - 与 dev seed 一致：viewer → aps.viewer.management，pmc → aps.planner，
 *    rule_admin / rule_publisher → aps.admin.aps，admin → aps.admin.system
 *  - 余下 3 个 DDL 角色（aps.supervisor.workshop / aps.coordinator.material / aps.service.api）
 *    dev 未 seed，mock 模式按 §二十五 场景补 viewer 级演示卡
 */
const MOCK_AVAILABLE_ROLES: RoleKey[] = [
  'aps.viewer.management',
  'aps.planner',
  'aps.admin.aps',
  'aps.admin.system'
]

/** 把 BusinessScope（前端 4 维度）转 UserInfo 后端 5 维度 + IsGlobal */
function toBackendScope(
  s: BusinessScope
): Pick<
  UserInfo,
  'isGlobal' | 'factories' | 'productFamilies' | 'departments' | 'domains' | 'resourceOrgGroups'
> {
  // 任一维度非空且非全通（空数组表示未限制）→ IsGlobal=false
  const hasAny =
    s.factoryCodes.length > 0 ||
    s.productFamilyCodes.length > 0 ||
    s.departmentCodes.length > 0 ||
    s.domainKeys.length > 0
  return {
    isGlobal: !hasAny,
    factories: s.factoryCodes,
    productFamilies: s.productFamilyCodes,
    departments: s.departmentCodes,
    domains: s.domainKeys,
    resourceOrgGroups: []
  }
}

/** 把 UserInfo 后端扁平 scope 转 BusinessScope（前端 4 维度） */
function fromBackendScope(u: UserInfo): BusinessScope {
  if (u.isGlobal) {
    return normalizeScope({
      factoryCodes: [],
      productFamilyCodes: [],
      departmentCodes: [],
      domainKeys: []
    })
  }
  return normalizeScope({
    factoryCodes: u.factories,
    productFamilyCodes: u.productFamilies,
    departmentCodes: u.departments,
    domainKeys: u.domains
  })
}

/**
 * Mock 模式角色 → userInfo 预设
 *  - roles / permissions：与 dev seed 段 D 一致（v1.2 §23.1 层次码 aps.* 前缀，对齐后端 PermissionCodes.cs 34 码）
 *  - scope：经 toBackendScope() 转后端 5 维度扁平字段（对齐 UserInfoDto）
 *
 * 权限码分布（与 3号位 dev seed 段 D 一致）：
 *  - aps.viewer.management  → 10 个只读（*.view）
 *  - aps.planner            → 17 个（日常排程 + 手动 ETA + Candidate 确认 + 局部 reschedule）
 *  - aps.admin.aps          → 8 个（规则/参数/策略 edit + publish；RULE_ADMIN/RULE_PUBLISHER 共用此 DDL 角色）
 *  - aps.admin.system       → 34 个全部（含 aps.auth.* / aps.mes.dispatch / aps.demand_protection.release）
 *
 * 注：v1.2 DDL 把 RULE_ADMIN + RULE_PUBLISHER 合并为单一 aps.admin.aps 角色；
 *     原 §二十五.3「RULE_ADMIN 不能自动拥有发布权」不再由角色码表达，
 *     改由权限码 aps.rule.publish 区分（与后端二次校验同码）。
 */
const MOCK_ROLE_PRESETS: Record<
  RoleKey,
  Pick<
    UserInfo,
    | 'roles'
    | 'permissions'
    | 'isGlobal'
    | 'factories'
    | 'productFamilies'
    | 'departments'
    | 'domains'
    | 'resourceOrgGroups'
  >
> = {
  'aps.viewer.management': {
    roles: ['aps.viewer.management'],
    permissions: [
      'aps.plan.view',
      'aps.ctp.view',
      'aps.candidate.view',
      'aps.rule.view',
      'aps.parameter.view',
      'aps.strategy.view',
      'aps.setup.view',
      // ResourceCalendar / ManualCapacity：3号位 2026-09-24 裁决 viewer 仅 view（2 码）
      //  - 真实绑定见 3号位 Sql/APS_Auth_resource_calendar_role_binding_20260924.sql
      'aps.resource_calendar.view',
      'aps.manual_capacity.view',
      'aps.manual_eta.view',
      'aps.demand_protection.view',
      'aps.mes.view',
      'aps.audit.view'
    ],
    ...toBackendScope(MOCK_ROLE_SCOPE_PRESETS['aps.viewer.management'])
  },
  'aps.planner': {
    roles: ['aps.planner'],
    permissions: [
      'aps.plan.view',
      'aps.plan.run',
      'aps.plan.compare',
      'aps.plan.export',
      'aps.ctp.view',
      'aps.ctp.evaluate',
      'aps.insert.impact.evaluate',
      'aps.reschedule.local',
      'aps.reschedule.manual',
      'aps.candidate.view',
      'aps.candidate.confirm',
      'aps.candidate.activate',
      'aps.setup.view',
      // ResourceCalendar / ManualCapacity：3号位 2026-09-24 裁决 planner = view + edit（**不含 delete**）
      //  - delete 为破坏性删除，收在 admin.aps / admin.system；D2「改时段须删旧窗」由 admin 代办承接
      //  - 真实绑定见 3号位 Sql/APS_Auth_resource_calendar_role_binding_20260924.sql
      'aps.resource_calendar.view',
      'aps.resource_calendar.edit',
      'aps.manual_capacity.view',
      'aps.manual_capacity.edit',
      'aps.manual_eta.view',
      'aps.manual_eta.edit',
      'aps.manual_eta.cancel',
      'aps.demand_protection.view',
      'aps.mes.view',
      // rbac.md §1.2 audit 行：4 角色均持 aps.audit.view（2026-09-20 Audit 页落地时对齐补码）
      'aps.audit.view'
    ],
    ...toBackendScope(MOCK_ROLE_SCOPE_PRESETS['aps.planner'])
  },
  'aps.admin.aps': {
    roles: ['aps.admin.aps'],
    permissions: [
      'aps.plan.view',
      'aps.rule.view',
      'aps.rule.edit',
      'aps.rule.publish',
      'aps.parameter.view',
      'aps.parameter.edit',
      'aps.strategy.view',
      'aps.strategy.edit',
      'aps.setup.view',
      'aps.setup.edit',
      'aps.setup.publish',
      // ResourceCalendar / ManualCapacity：admin.aps 持全 6 码（同 Setup 先例）
      'aps.resource_calendar.view',
      'aps.resource_calendar.edit',
      'aps.resource_calendar.delete',
      'aps.manual_capacity.view',
      'aps.manual_capacity.edit',
      'aps.manual_capacity.delete',
      // rbac.md §1.2 audit 行：4 角色均持 aps.audit.view（2026-09-20 Audit 页落地时对齐补码）
      'aps.audit.view'
    ],
    ...toBackendScope(MOCK_ROLE_SCOPE_PRESETS['aps.admin.aps'])
  },
  'aps.admin.system': {
    roles: ['aps.admin.system'],
    permissions: [
      'aps.plan.view',
      'aps.plan.run',
      'aps.plan.compare',
      'aps.plan.export',
      'aps.ctp.view',
      'aps.ctp.evaluate',
      'aps.insert.impact.evaluate',
      'aps.reschedule.local',
      'aps.reschedule.manual',
      'aps.candidate.view',
      'aps.candidate.confirm',
      'aps.candidate.activate',
      'aps.rule.view',
      'aps.rule.edit',
      'aps.rule.publish',
      'aps.parameter.view',
      'aps.parameter.edit',
      'aps.strategy.view',
      'aps.strategy.edit',
      'aps.setup.view',
      'aps.setup.edit',
      'aps.setup.publish',
      // ResourceCalendar / ManualCapacity：admin.system 持全 6 码
      'aps.resource_calendar.view',
      'aps.resource_calendar.edit',
      'aps.resource_calendar.delete',
      'aps.manual_capacity.view',
      'aps.manual_capacity.edit',
      'aps.manual_capacity.delete',
      'aps.manual_eta.view',
      'aps.manual_eta.edit',
      'aps.manual_eta.cancel',
      'aps.demand_protection.view',
      'aps.demand_protection.release',
      'aps.mes.view',
      'aps.mes.dispatch',
      'aps.auth.user.view',
      'aps.auth.user.edit',
      'aps.auth.role.view',
      'aps.auth.role.edit',
      'aps.auth.permission.assign',
      'aps.auth.scope.assign',
      'aps.audit.view'
    ],
    ...toBackendScope(MOCK_ROLE_SCOPE_PRESETS['aps.admin.system'])
  },
  // 余下 3 个 DDL 角色 dev 未 seed，mock 模式按 viewer 级分配演示卡
  'aps.supervisor.workshop': {
    roles: ['aps.supervisor.workshop'],
    permissions: ['aps.plan.view', 'aps.audit.view'],
    ...toBackendScope(MOCK_ROLE_SCOPE_PRESETS['aps.supervisor.workshop'])
  },
  'aps.coordinator.material': {
    roles: ['aps.coordinator.material'],
    permissions: ['aps.plan.view', 'aps.manual_eta.view'],
    ...toBackendScope(MOCK_ROLE_SCOPE_PRESETS['aps.coordinator.material'])
  },
  'aps.service.api': {
    roles: ['aps.service.api'],
    permissions: ['aps.plan.view'],
    ...toBackendScope(MOCK_ROLE_SCOPE_PRESETS['aps.service.api'])
  }
}

export const useApsAuthStore = defineStore('aps.auth', () => {
  const userInfo = ref<UserInfo | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  /** mock 模式下当前生效的单一角色（用于角色切换器 / 顶部徽章显示）
   *  - 默认 aps.planner：维持原有"演示账号能看也能写"的体验
   *  - 切到 aps.viewer.management 时按钮全部隐藏，用于验证 §二十五.1/.2 场景
   *  - 切到 aps.admin.aps 演示规则管理员权限（v1.2 DDL 已合并 RULE_ADMIN + RULE_PUBLISHER；
   *    §二十五.3「不能自动拥有发布权」改由权限码 aps.rule.publish 表达，不依赖角色）
   */
  const mockActiveRole = ref<RoleKey>('aps.planner')

  const roles = computed<RoleKey[]>(() => userInfo.value?.roles ?? [])
  const permissions = computed<string[]>(() => userInfo.value?.permissions ?? [])
  const hasRole = (role: RoleKey) => roles.value.includes(role)
  /** 权限码校验（页面内写门控 / 按钮显隐专用）
   *  - v1.2 §23.1：与后端 PermissionCodes.cs 34 码比对（一致才放行）
   *  - 不归一化大小写：后端签发与前端比对都用小写 + 点号
   */
  const has = (perm: string) => permissions.value.includes(perm)

  /** DDL 系统角色便捷判断（语义化命名；与 isPmc 等历史别名一一对应） */
  const isAdmin = computed(() => hasRole('aps.admin.system'))
  const isRuleAdmin = computed(() => hasRole('aps.admin.aps'))
  const isPmc = computed(() => hasRole('aps.planner'))
  const isViewer = computed(() => hasRole('aps.viewer.management'))

  /** P1-15 业务范围（4 维度；空数组 = 未限制）
   *  - 来源：userInfo 后端扁平字段（isGlobal + factories + productFamilies + departments + domains）
   *  - IsGlobal=true → 全空（视为"未限制"）；各维度非空 → 受限白名单
   */
  const dataScope = computed<BusinessScope>(() => {
    const u = userInfo.value
    if (!u) return normalizeScope(null)
    return fromBackendScope(u)
  })

  /** mock 可切角色列表（生产模式返回空数组；UI 不显示切换器） */
  const mockAvailableRoles = computed<RoleKey[]>(() => (APS_USE_MOCK ? MOCK_AVAILABLE_ROLES : []))

  async function loadUserInfo(): Promise<void> {
    if (APS_USE_MOCK) {
      const preset = MOCK_ROLE_PRESETS[mockActiveRole.value]
      userInfo.value = {
        userId: 1,
        userCode: 'pmc',
        userName: `PMC 演示账号（${mockActiveRole.value}）`,
        roles: preset.roles,
        permissions: preset.permissions,
        isGlobal: preset.isGlobal,
        factories: preset.factories,
        productFamilies: preset.productFamilies,
        departments: preset.departments,
        domains: preset.domains,
        resourceOrgGroups: preset.resourceOrgGroups
      }
      return
    }
    // 真实模式：必须已登录（useUserStore.token 非空）才能调 /me
    const userStore = useUserStoreWithOut()
    if (!userStore.getToken) {
      error.value = '未登录'
      return
    }
    loading.value = true
    try {
      const me = await apsAuthApi.getUserInfo()
      // 角色用后端 roles 覆盖（mock 角色名 VIEWER/PMC 与 DDL 角色码 aps.planner 不同）
      userInfo.value = me
    } catch (err) {
      error.value = (err as Error)?.message ?? '加载用户信息失败'
    } finally {
      loading.value = false
    }
  }

  /** mock 模式角色切换（P2）：只把 roles + 业务范围改成预设
   *  - 仅 APS_USE_MOCK=true 时生效，生产模式调用即 no-op
   *  - 不重置页面 store（Gantt/Candidate/Run 等），切换后停留在原页面就能看到按钮显隐变化
   */
  function mockSwitchRole(role: RoleKey): void {
    if (!APS_USE_MOCK) return
    if (!MOCK_AVAILABLE_ROLES.includes(role)) return
    mockActiveRole.value = role
    const preset = MOCK_ROLE_PRESETS[role]
    if (userInfo.value) {
      userInfo.value = {
        ...userInfo.value,
        roles: preset.roles,
        permissions: preset.permissions,
        isGlobal: preset.isGlobal,
        factories: preset.factories,
        productFamilies: preset.productFamilies,
        departments: preset.departments,
        domains: preset.domains,
        resourceOrgGroups: preset.resourceOrgGroups,
        userName: `PMC 演示账号（${role}）`
      }
    }
  }

  function reset(): void {
    userInfo.value = null
    error.value = null
  }

  return {
    userInfo,
    loading,
    error,
    roles,
    permissions,
    hasRole,
    has,
    isAdmin,
    isRuleAdmin,
    isPmc,
    isViewer,
    dataScope,
    mockActiveRole,
    mockAvailableRoles,
    mockSwitchRole,
    loadUserInfo,
    reset
  }
})
