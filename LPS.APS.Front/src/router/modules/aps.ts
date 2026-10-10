/**
 * APS V1 4号位 — 路由模块
 *
 * 11 类页面（依据 4号位文档第二十一节 + v1.2 Domain专项扩展）：
 *  阶段 A（只读结果页）  Phase A：overview / order / gantt / explanation / run
 *  阶段 B（CTP/Candidate）Phase B：ctp / candidate
 *  阶段 C（规则参数）    Phase C：rules / domain
 *  阶段 D（异常/PI/MES）Phase D：pi / manual-eta / demand-protection
 *
 * 菜单生成：通过 router meta 自动构建（项目原有 generateRoutes 机制）
 *
 * RBAC（v1.2 §23.1 重新启用）：
 *  - 路由级守卫函数已就绪（src/router/guard.ts setupApsRouteGuard）
 *  - 按三波挂 meta：第一波（admin/draft 风险低）→ 第二波（PMC 写操作）→ 第三波（只读页）
 *  - 第一波当前已挂：domain（仅 SYSTEM_ADMIN）+ rules（持 rule 权限码）
 *  - 第二/三波待办：ctp/candidate/manual-eta/demand-protection（第二波）+ overview/order/gantt/explanation/run/pi（第三波）
 */

const Layout = () => import('@/layout/Layout.vue')

/**
 * 阶段标识：影响菜单"实现状态"角标
 *  - 'A' 已实现
 *  - B 阶段 B 待开发
 *  - C 阶段 C 待开发
 *  - D 阶段 D 待开发
 */
export const apsV1Routes: AppRouteRecordRaw[] = [
  {
    path: '/aps',
    component: Layout,
    redirect: '/aps/overview',
    name: 'ApsV1',
    meta: {
      title: 'APS 排产',
      icon: 'vi-mdi:chart-gantt',
      alwaysShow: true
    },
    children: [
      /* ===== 阶段 A：只读结果页 ===== */
      {
        path: 'overview',
        component: () => import('@/views/Aps/Overview.vue'),
        name: 'ApsOverview',
        meta: {
          title: '排产总览',
          icon: 'vi-mdi:view-dashboard-outline',
          noCache: true,
          stage: 'A',
          acceptance: 'U01 / U02 / U07',
          // 第三波 RBAC：只读页 - 需持有 aps.plan.view 权限码（5 角色均持有，全员放行）
          apsRequiredPermissions: ['aps.plan.view']
        }
      },
      {
        path: 'order',
        component: () => import('@/views/Aps/Order.vue'),
        name: 'ApsOrder',
        meta: {
          title: '订单/需求计划',
          icon: 'vi-mdi:clipboard-list-outline',
          stage: 'A',
          acceptance: 'U03 / U04 / U05',
          // 第三波 RBAC：只读页 - 需持有 aps.plan.view
          apsRequiredPermissions: ['aps.plan.view']
        }
      },
      {
        path: 'gantt',
        component: () => import('@/views/Aps/Gantt.vue'),
        name: 'ApsGantt',
        meta: {
          title: '甘特图/资源计划',
          icon: 'vi-mdi:chart-timeline',
          noTagsView: true,
          stage: 'A',
          acceptance: 'U01 / U06 / U14',
          // 第三波 RBAC：只读页 - 需持有 aps.plan.view
          apsRequiredPermissions: ['aps.plan.view']
        }
      },
      {
        path: 'explanation',
        component: () => import('@/views/Aps/Explanation.vue'),
        name: 'ApsExplanation',
        meta: {
          title: '异常与原因解释',
          icon: 'vi-mdi:alert-circle-outline',
          stage: 'A',
          acceptance: 'U09 / U20',
          // 第三波 RBAC：只读页 - 需持有 aps.plan.view
          apsRequiredPermissions: ['aps.plan.view']
        }
      },
      {
        path: 'run',
        component: () => import('@/views/Aps/Run.vue'),
        name: 'ApsRun',
        meta: {
          title: '运行/版本/MES',
          icon: 'vi-mdi:server-network',
          noCache: true,
          stage: 'A',
          acceptance: 'U01 / U02 / U19 / U16-U18',
          // 第三波 RBAC：只读页 - 需持有 aps.plan.view
          apsRequiredPermissions: ['aps.plan.view']
        }
      },

      /* ===== 阶段 B：CTP / Candidate ===== */
      {
        path: 'ctp',
        component: () => import('@/views/Aps/Ctp.vue'),
        name: 'ApsCtp',
        meta: {
          title: 'CTP/插单评估',
          icon: 'vi-mdi:target',
          stage: 'B',
          acceptance: 'U08 / U09 / U11 / U15',
          // 第二波 RBAC：需持有 aps.ctp.evaluate 权限码（PMC / SYSTEM_ADMIN 持有）
          //  - CTP 试算是写操作；VIEWER 只持 aps.ctp.view 不持 evaluate → 被守卫拒绝
          apsRequiredPermissions: ['aps.ctp.evaluate']
        }
      },
      {
        path: 'candidate',
        component: () => import('@/views/Aps/Candidate.vue'),
        name: 'ApsCandidate',
        meta: {
          title: 'Candidate 对比与确认',
          icon: 'vi-mdi:compare-horizontal',
          stage: 'B',
          acceptance: 'U10 / U11 / U12 / U13 / U14',
          // 第二波 RBAC：需持有 aps.candidate.confirm 权限码（PMC / SYSTEM_ADMIN 持有）
          //  - Candidate 激活是写操作；U11/U12 WHATIF 场景已由 canActivate 字段控制按钮，路由级仅做粗粒度门控
          apsRequiredPermissions: ['aps.candidate.confirm']
        }
      },

      /* ===== 阶段 C：规则参数维护 ===== */
      {
        path: 'rules',
        component: () => import('@/views/Aps/Rules.vue'),
        name: 'ApsRules',
        meta: {
          title: '规则与参数维护',
          icon: 'vi-mdi:cog-outline',
          stage: 'C',
          acceptance: 'U21 / U22',
          // 第一波 RBAC：需持有 rule.edit / rule.publish 权限码任一（OR 语义）
          //  - RULE_ADMIN（持 aps.rule.edit / publish）/ RULE_PUBLISHER（同）/ SYSTEM_ADMIN（持全部）均可访问
          //  - VIEWER / PMC 无 rule.* 权限码 → 被守卫拒绝，跳 /403
          apsRequiredPermissions: ['aps.rule.edit', 'aps.rule.publish']
        }
      },

      /* ===== 阶段 C：排程域定义维护（v1.2 Domain专项 Pkg-3）===== */
      {
        path: 'domain',
        component: () => import('@/views/Aps/Domain.vue'),
        name: 'ApsDomain',
        meta: {
          title: '排程域定义维护',
          icon: 'vi-mdi:sitemap-outline',
          stage: 'C',
          acceptance: 'Domain专项 §四 / Pkg-3',
          // 第一波 RBAC：仅系统管理员可进（与 Domain.vue 按钮级 canWrite 一致）
          //  v1.2 DDL：用后端系统角色码 aps.admin.system（不再用 SYSTEM_ADMIN 别名）
          apsRequiredRoles: ['aps.admin.system']
        }
      },

      /* ===== 阶段 D：PI / MES 资格 ===== */
      {
        path: 'pi',
        component: () => import('@/views/Aps/Pi.vue'),
        name: 'ApsPi',
        meta: {
          title: 'PI Position/供给追溯',
          icon: 'vi-mdi:package-variant-closed',
          stage: 'D',
          acceptance: 'U16 / U17 / U18',
          // 第三波 RBAC：只读页 - 需持有 aps.plan.view
          apsRequiredPermissions: ['aps.plan.view']
        }
      },

      /* ===== 阶段 D：Manual ETA（4 号位文档第 11 节 / 审核报告 P1-7） ===== */
      {
        path: 'manual-eta',
        component: () => import('@/views/Aps/ManualEta.vue'),
        name: 'ApsManualEta',
        meta: {
          title: '人工到货 ETA',
          icon: 'vi-mdi:truck-delivery-outline',
          stage: 'D',
          acceptance: 'P1-7 / 4号位文档第 11 节',
          // 第二波 RBAC：PMC 专属（人工到货 ETA 是计划员的领域操作）
          //  - 与 ManualEta.vue 内部权限码门控（has('aps.manual_eta.edit')）一致；路由级做粗粒度
          //  - v1.2 DDL：用后端系统角色码 aps.planner + aps.admin.system
          apsRequiredRoles: ['aps.planner', 'aps.admin.system']
        }
      },

      /* ===== 阶段 D：Demand Protection 释放（4 号位文档第 13 节 / 审核报告 P1-11） ===== */
      {
        path: 'demand-protection',
        component: () => import('@/views/Aps/DemandProtection.vue'),
        name: 'ApsDemandProtection',
        meta: {
          title: '需求保护释放',
          icon: 'vi-mdi:shield-lock-outline',
          stage: 'D',
          acceptance: 'P1-11 / 4号位文档第 13 节',
          // 第二波 RBAC：PMC 专属（需求保护释放是计划员的领域操作）
          //  - 与 DemandProtection.vue 内部权限码门控（has('aps.demand_protection.release')）一致；
          //    路由级做粗粒度（dev seed 下 aps.planner 不持有 release 权限码，仅 aps.admin.system 持有）
          //  - v1.2 DDL：用后端系统角色码 aps.planner + aps.admin.system
          apsRequiredRoles: ['aps.planner', 'aps.admin.system']
        }
      },

      /* ===== 阶段 C：策略配置（v1.4 §十八.10 长期欠账收口；独立路由不混入 Rules.vue）=====
       * 后端 12 端点全在 /api/governance/strategy-profile/* + /api/governance/strategy-profiles
       * 6 态状态机 + IsDefault 红线（UQ_StrategyProfileVersion_DefaultPublished）
       * 权限码：aps.strategy.{view, edit, publish}（不要用 aps.rule.* 错位）
       *   - VIEWER 仅持 view，可读
       *   - aps.admin.aps 持 view + edit，可维护 DRAFT
       *   - aps.admin.system 持全部，可发布
       */
      {
        path: 'strategy-profile',
        component: () => import('@/views/Aps/StrategyProfiles.vue'),
        name: 'ApsStrategyProfiles',
        meta: {
          title: '策略配置',
          icon: 'vi-mdi:tune-variant',
          noCache: true,
          stage: 'C',
          acceptance: 'v1.4 §十八.10',
          // 路由守卫 OR 语义：持任一权限码即可访问（VIEWER 持 view 可进）
          apsRequiredPermissions: ['aps.strategy.view', 'aps.strategy.edit', 'aps.strategy.publish']
        }
      },

      /* ===== 阶段 C：Setup 换型规则维护（v1.5 Setup 专项 §2，5 个页面）=====
       * 后端 13 端点全在 /api/governance/setup-rules/* + /api/governance/operation-resource-eligibility
       * 3号位 API 待落地（9/25 deadline），4号位 先做前端预备 + mock fallback
       * 权限码：aps.setup.{view, edit, publish}（不要用 aps.rule.* 错位）
       *   - VIEWER 仅持 view，可读
       *   - aps.admin.aps 持 view + edit + publish，可维护 DRAFT + 发布
       *   - aps.admin.system 持全部
       * 红线（§7.1 + §7.2）：
       *   - 仅 DRAFT 状态的 RuleSetVersion 可编辑
       *   - FromMaterialCode != ToMaterialCode（同产品连续 = 0 分钟）
       *   - SetupMinutes > 0
       *   - EXACT 必须填 FromMaterial/ToMaterial；DEFAULT 必须都空
       */
      {
        path: 'setup/exact',
        component: () => import('@/views/Aps/SetupExact.vue'),
        name: 'ApsSetupExact',
        meta: {
          title: '明确产品转换规则',
          icon: 'vi-mdi:swap-horizontal-bold',
          noCache: true,
          stage: 'C',
          acceptance: 'v1.5 §3 / S01 / S03',
          apsRequiredPermissions: ['aps.setup.view', 'aps.setup.edit', 'aps.setup.publish']
        }
      },
      {
        path: 'setup/default',
        component: () => import('@/views/Aps/SetupDefault.vue'),
        name: 'ApsSetupDefault',
        meta: {
          title: '默认 Setup 规则',
          icon: 'vi-mdi:cog-outline',
          noCache: true,
          stage: 'C',
          acceptance: 'v1.5 §4 / S05',
          apsRequiredPermissions: ['aps.setup.view', 'aps.setup.edit', 'aps.setup.publish']
        }
      },
      {
        path: 'setup/uncovered',
        component: () => import('@/views/Aps/SetupUncovered.vue'),
        name: 'ApsSetupUncovered',
        meta: {
          title: '未维护 Setup 查询',
          icon: 'vi-mdi:help-circle-outline',
          noCache: true,
          stage: 'C',
          acceptance: 'v1.5 §5 / S06',
          apsRequiredPermissions: ['aps.setup.view']
        }
      },
      {
        path: 'setup/diff',
        component: () => import('@/views/Aps/SetupDiff.vue'),
        name: 'ApsSetupDiff',
        meta: {
          title: 'Setup 版本 Diff',
          icon: 'vi-mdi:file-compare-outline',
          noCache: true,
          stage: 'C',
          acceptance: 'v1.5 §6',
          apsRequiredPermissions: ['aps.setup.view', 'aps.setup.publish']
        }
      },

      /* ===== Pkg-8：RBAC 管理 UI（4 号位 自管，仅 SYSTEM_ADMIN 经 aps.auth.user.edit 通行）=====
       *  - 用户管理 / 角色管理（第一版）/ 权限码只读 / 业务范围维护（第二版）
       *  - 后端类级 [Authorize(Policy = PermissionCodes.AuthManage = "aps.auth.user.edit")]，
       *    前端 meta 必挂同一码；用 'aps.auth.role.edit' 会前端放行 / 后端 403 错位
       *  - 第一版只建 users/roles 两页（permissions/scopes 第二版）
       */
      {
        path: 'rbac-users',
        component: () => import('@/views/Aps/RbacUsers.vue'),
        name: 'ApsRbacUsers',
        meta: {
          title: '用户管理',
          icon: 'vi-mdi:account-cog-outline',
          noCache: true,
          stage: 'E',
          acceptance: 'F-G5 / RBAC 管理 UI',
          apsRequiredPermissions: ['aps.auth.user.edit']
        }
      },
      {
        path: 'rbac-roles',
        component: () => import('@/views/Aps/RbacRoles.vue'),
        name: 'ApsRbacRoles',
        meta: {
          title: '角色管理',
          icon: 'vi-mdi:shield-account-outline',
          noCache: true,
          stage: 'E',
          acceptance: 'F-G5 / RBAC 管理 UI',
          apsRequiredPermissions: ['aps.auth.user.edit']
        }
      },
      {
        path: 'rbac-permissions',
        component: () => import('@/views/Aps/RbacPermissions.vue'),
        name: 'ApsRbacPermissions',
        meta: {
          title: '权限码',
          icon: 'vi-mdi:key-outline',
          noCache: true,
          stage: 'E',
          acceptance: 'F-G5 / RBAC 管理 UI',
          apsRequiredPermissions: ['aps.auth.user.edit']
        }
      },
      {
        path: 'rbac-scopes',
        component: () => import('@/views/Aps/RbacScopes.vue'),
        name: 'ApsRbacScopes',
        meta: {
          title: '业务范围维护',
          icon: 'vi-mdi:map-marker-radius-outline',
          noCache: true,
          stage: 'E',
          acceptance: 'F-G5 / RBAC 管理 UI',
          apsRequiredPermissions: ['aps.auth.user.edit']
        }
      },

      /* ===== 阶段 C：OPM 工艺规划属性治理（3 号位 2026-09-23 T1 交付件）=====
       *  - 后端 2 端点：GET /api/governance/routing-operations?materialId=
       *                 PUT /api/governance/routing-operations/{id}/planning-mode
       *  - 权限：view=aps.rule.view / edit=aps.rule.edit（**复用 Rule 码**，零权限种子变更）
       *  - 路由级 OR 语义：持任一权限码即可访问（VIEWER 持 view 可进）
       *  - 红线（0号位 2026-09-23 Q1 裁决）：
       *    #5 接口即契约：本路由不嵌入 Rules.vue，独立维护页（避免混入规则参数主表）
       *    #6 仅 UPDATE 值（DML），无 DDL；sp_SyncRoutingData 不 MERGE OPM
       *  - 依据：frontNew/docs/APS_V1_OPM治理API_S2S3_3号位致4号位_v1.0_20260923.md
       */
      {
        path: 'operation-planning-mode',
        component: () => import('@/views/Aps/Opm.vue'),
        name: 'ApsOperationPlanningMode',
        meta: {
          title: '工艺规划模式（OPM）',
          icon: 'vi-mdi:cog-transfer-outline',
          noCache: true,
          stage: 'C',
          acceptance: '3号位 T1 OPM 治理 / 字段三来源原则',
          // 路由守卫 OR 语义：持任一权限码即可访问
          //  - aps.rule.view（VIEWER / RULE_ADMIN / RULE_PUBLISHER / SYSTEM_ADMIN 全持）
          //  - aps.rule.edit（仅 RULE_ADMIN / RULE_PUBLISHER / SYSTEM_ADMIN 持）
          apsRequiredPermissions: ['aps.rule.view', 'aps.rule.edit']
        }
      },

      /* ===== 阶段 C：资源日历 / 人工能力槽维护（v1.3 冻结方案 + 5号位 2026-09-24 对接函）=====
       *  - 后端：/api/resource-calendar/*（设备 Resource + ResourceCalendarSlot）
       *          /api/manual-capacity/*（人工能力槽 ManualCapacitySlot + ManualCapacitySlotCalendar）
       *  - 单页 3 Tab：设备资源日历 / 人工能力槽 / 人工槽日历
       *  - 权限码：设备 aps.resource_calendar.{view,edit,delete}
       *            人工 aps.manual_capacity.{view,edit,delete}
       *            路由守卫 OR 语义：持 view 任一即可进（按钮级按 edit/delete 细分）
       *  - 红线（v1.3 §九）：无有效日历 = 不可用，禁止按 7×24 缺省兜底
       *  - ⚠️ 冻结文档同步中（C1）：v1.3 方案尚未回灌 7 份冻结文档，页面顶部已明示
       */
      {
        path: 'resource-calendar',
        component: () => import('@/views/Aps/ResourceCalendar.vue'),
        name: 'ApsResourceCalendar',
        meta: {
          title: '资源日历/人工能力槽',
          icon: 'vi-mdi:calendar-clock-outline',
          noCache: true,
          stage: 'C',
          acceptance: 'v1.3 §三/§五 / 5号位 2026-09-24 对接函',
          apsRequiredPermissions: ['aps.resource_calendar.view', 'aps.manual_capacity.view']
        }
      },

      /* ===== 审计日志页（页面 12 §22.6 Audit / §二十四 / U42）=====
       *  - 门控用专属码 aps.audit.view（rbac.md §1.2：4 角色均持，与其余 rbac 页的
       *    aps.auth.user.edit 仅 admin.system 不同——审计是监督能力，管理层可查看）
       *  - 后端 GET /api/rbac/audit-logs（Policy=AuditView）2026-09-20 curl 实测已就绪
       */
      {
        path: 'audit',
        component: () => import('@/views/Aps/Audit.vue'),
        name: 'ApsAudit',
        meta: {
          title: '审计日志',
          icon: 'vi-ant-design:audit-outlined',
          noCache: true,
          stage: 'E',
          acceptance: 'U42 / §22.6 Audit / §二十四',
          apsRequiredPermissions: ['aps.audit.view']
        }
      }
    ]
  }
]

export default apsV1Routes
