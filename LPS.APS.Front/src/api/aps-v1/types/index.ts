/**
 * APS V1 4 号位 DTO 索引
 *
 * 命名约定：
 *  - common.ts          公共类型与枚举（ApiResponse、TaskStatus 5 值、PlanVersionStatus 等）
 *  - schedule.ts        排程查询 DTO（已与后端对齐，GanttData/PlanVersionSummary/ScheduleSummary）
 *  - overview.ts        排产总览 DTO（占位，等 3 号位落地）
 *  - order.ts           订单详情 DTO（占位）
 *  - ctp.ts             CTP / 插单评估 DTO（占位）
 *  - candidate.ts       Candidate 对比与确认 DTO（占位）
 *  - explanation.ts     异常与原因解释 DTO（占位）
 *  - pi.ts              PI Position / 供给追溯 DTO（占位）
 *  - rule.ts            规则与参数维护 DTO（占位）
 *  - run.ts             运行 / 版本 / MES 下发状态 DTO（占位）
 *  - mes.ts             （暂未单独拆分 MES，与 run.ts 中的 MesEligibilityDto 合用）
 *  - auth.ts            认证 DTO（已与后端对齐）
 *  - manualEta.ts       人工到货 ETA DTO（已与 5 号位对齐）
 *  - demandProtection.ts 需求保护释放 DTO（P1-11）
 *  - domain.ts           排程域（Domain）定义 DTO（v1.2 Domain专项）
 *  - strategyProfile.ts  策略配置 DTO（v1.4 §十八.10）
 *  - setup.ts            Setup 换型规则 DTO（v1.5 Setup 专项）
 *  - audit.ts            审计日志 DTO（U42 / §22.6 Audit / §二十四）
 *  - opm.ts              OPM 工艺规划模式 DTO（3号位 2026-09-23 T1 交付）
 *  - resourceCalendar.ts 设备资源日历 + 人工能力槽 DTO（v1.3 冻结方案 + 5号位 2026-09-24 对接函）
 *  - backend-aligned.ts 后端 Core DTO 类型对齐（只读参考）
 */

export * from './common'
export * from './schedule'
export * from './overview'
export * from './order'
export * from './ctp'
export * from './candidate'
export * from './explanation'
export * from './pi'
export * from './rule'
export * from './run'
export * from './auth'
export * from './manualEta'
export * from './demandProtection'
export * from './domain'
export * from './rbac'
export * from './strategyProfile'
export * from './setup'
export * from './audit'
export * from './opm'
export * from './resourceCalendar'
export * from './backend-aligned'
