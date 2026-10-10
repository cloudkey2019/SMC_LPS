/**
 * APS V1 4号位 — 审计日志 API（U42 / §22.6 Audit / §二十四 人工操作审计）
 *
 * @owner 3号位（认证 / 权限 / 审计能力）
 * @see lps/LPS.APS.Web/Controllers/RbacController.cs L170-181（GET /api/rbac/audit-logs）
 * @see frontNew/src/api/aps-v1/types/audit.ts（字段级契约源）
 *
 * 设计要点：
 *  - 只读页面无写端点 → 不需要 rejectMockWrite
 *  - mock fixture 模拟真实服务端行为：userId/action/from/to 过滤 + OccurredAt 倒序 + page/size 切片
 *    （保证 mock 走查与真实模式交互一致，含 hasMore 分页语义）
 *  - mock fixture 中 oldValue/newValue 一律不含密码/Token（U42 卫生要求；
 *    真实数据由后端写入点保证——已确认现有写入点结构上不涉密）
 *  - 响应无 total（后端 QueryPagedAsync 不返回总数）→ 调用方用 `rows.length === size` 判断 hasMore
 */

import { apsHttp, APS_USE_MOCK } from './http'
import type { AuditLogDto, AuditLogQuery } from './types'

/* ==================== Mock Fixtures（仅离线 UI） ==================== */

/** 相对当前时间 N 分钟前的 ISO 字符串（fixture 时间递减，模拟真实倒序数据） */
const minutesAgo = (m: number): string => new Date(Date.now() - m * 60_000).toISOString()

/**
 * mock 审计流水（29 条）：
 *  - 覆盖全部 15 个 actionCode × Success/Failed（含 09-20 落地的 Login/Logout）
 *  - userId/userCode 与 rbac.ts MOCK_USERS 对齐（1=admin / 2=pmc / 3=viewer），
 *    保证 Actor 下拉筛选在 mock 模式下可演示
 *  - entityType 覆盖 RBAC 四实体 + 治理五实体（§二十四 关键动作清单）
 *  - Login/Logout 行对齐后端 WriteAuthAuditAsync 写入形态：
 *    Module="Auth" / EntityType="User" / EntityId=userCode / errorMessage 仅原因类别（U42 脱敏）
 */
const MOCK_AUDIT_LOGS: AuditLogDto[] = [
  {
    id: 9029,
    userId: 1,
    userCode: 'admin',
    actionCode: 'Login',
    module: 'Auth',
    entityType: 'User',
    entityId: 'admin',
    result: 'Success',
    occurredAt: minutesAgo(3),
    clientIp: '10.10.21.33',
    userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)'
  },
  {
    id: 9028,
    userId: 3,
    userCode: 'viewer',
    actionCode: 'Login',
    module: 'Auth',
    entityType: 'User',
    entityId: 'viewer',
    result: 'Failed',
    occurredAt: minutesAgo(8),
    clientIp: '10.10.21.77',
    errorMessage: '密码错误'
  },
  {
    id: 9027,
    userId: 2,
    userCode: 'pmc',
    actionCode: 'Logout',
    module: 'Auth',
    entityType: 'User',
    entityId: 'pmc',
    result: 'Success',
    occurredAt: minutesAgo(20),
    clientIp: '10.10.21.58'
  },
  {
    id: 9026,
    userId: 1,
    userCode: 'admin',
    actionCode: 'Publish',
    module: 'governance',
    entityType: 'RuleSetVersion',
    entityId: '1042',
    versionCode: 'RSV-2026-009',
    oldValue: '{"status":"DRAFT"}',
    newValue: '{"status":"ACTIVE"}',
    result: 'Success',
    occurredAt: minutesAgo(12),
    clientIp: '10.10.21.33',
    userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)',
    remark: '发布原因：Setup 换型规则 v1.5 专项对齐'
  },
  {
    id: 9025,
    userId: 2,
    userCode: 'pmc',
    actionCode: 'ConfirmCandidate',
    module: 'run',
    entityType: 'PlanVersion',
    entityId: '2087',
    planVersionId: 2087,
    result: 'Success',
    occurredAt: minutesAgo(35),
    clientIp: '10.10.21.58',
    remark: 'Candidate 采用（U13：Actor/Time 可追溯）'
  },
  {
    id: 9024,
    userId: 1,
    userCode: 'admin',
    actionCode: 'AssignPermissions',
    module: 'auth',
    entityType: 'Role',
    entityId: '3',
    oldValue: '["aps.plan.view","aps.ctp.view"]',
    newValue: '["aps.plan.view","aps.ctp.view","aps.candidate.confirm"]',
    result: 'Success',
    occurredAt: minutesAgo(52),
    clientIp: '10.10.21.33',
    remark: '角色 aps.planner 增补 candidate.confirm'
  },
  {
    id: 9023,
    userId: 2,
    userCode: 'pmc',
    actionCode: 'ActivateCandidate',
    module: 'run',
    entityType: 'PlanVersion',
    entityId: '2087',
    planVersionId: 2087,
    result: 'Failed',
    occurredAt: minutesAgo(66),
    clientIp: '10.10.21.58',
    errorMessage: 'Purpose=CTP 的 Run 不允许激活（U46）',
    remark: '激活被后端拒绝'
  },
  {
    id: 9022,
    userId: 1,
    userCode: 'admin',
    actionCode: 'RecoverFailedRun',
    module: 'run',
    entityType: 'ScheduleRun',
    entityId: '311',
    oldValue: '{"status":"FAILED"}',
    newValue: '{"status":"RECOVERED","newRunId":318}',
    result: 'Success',
    occurredAt: minutesAgo(90),
    clientIp: '10.10.21.33',
    remark: 'U19：新 Run 产生，旧 Run 仍 FAILED'
  },
  {
    id: 9021,
    userId: 1,
    userCode: 'admin',
    actionCode: 'Create',
    module: 'auth',
    entityType: 'User',
    entityId: '17',
    newValue: '{"userCode":"planner02","status":"Active"}',
    result: 'Success',
    occurredAt: minutesAgo(120),
    clientIp: '10.10.21.33',
    remark: '新建用户（U42：newValue 不含密码字段）'
  },
  {
    id: 9020,
    userId: 1,
    userCode: 'admin',
    actionCode: 'Update',
    module: 'auth',
    entityType: 'User',
    entityId: '17',
    oldValue: '{"status":"Active"}',
    newValue: '{"status":"Deleted"}',
    result: 'Success',
    occurredAt: minutesAgo(150),
    clientIp: '10.10.21.33',
    remark: '停用用户 planner02'
  },
  {
    id: 9019,
    userId: 1,
    userCode: 'admin',
    actionCode: 'AssignRoles',
    module: 'auth',
    entityType: 'User',
    entityId: '2',
    oldValue: '[]',
    newValue: '["aps.planner"]',
    result: 'Success',
    occurredAt: minutesAgo(185),
    clientIp: '10.10.21.33'
  },
  {
    id: 9018,
    userId: 1,
    userCode: 'admin',
    actionCode: 'AssignScopes',
    module: 'auth',
    entityType: 'Role',
    entityId: '3',
    oldValue: '[]',
    newValue: '[{"scopeType":"Factory","scopeValue":"F-SUZ-01"}]',
    result: 'Success',
    occurredAt: minutesAgo(210),
    clientIp: '10.10.21.33',
    remark: 'planner 角色绑定苏州工厂范围'
  },
  {
    id: 9017,
    userId: 1,
    userCode: 'admin',
    actionCode: 'Delete',
    module: 'auth',
    entityType: 'DataScopePolicy',
    entityId: '9',
    oldValue: '{"scopeType":"Factory","scopeValue":"BJ-OLD"}',
    result: 'Success',
    occurredAt: minutesAgo(260),
    clientIp: '10.10.21.33'
  },
  {
    id: 9016,
    userId: 1,
    userCode: 'admin',
    actionCode: 'Publish',
    module: 'governance',
    entityType: 'StrategyProfileVersion',
    entityId: '58',
    versionCode: 'SPV-2026-014',
    oldValue: '{"status":"DRAFT"}',
    newValue: '{"status":"ACTIVE"}',
    result: 'Success',
    occurredAt: minutesAgo(300),
    clientIp: '10.10.21.33',
    remark: '发布原因：注塑族换型策略调整'
  },
  {
    id: 9015,
    userId: 1,
    userCode: 'admin',
    actionCode: 'Disable',
    module: 'governance',
    entityType: 'RuleSetVersion',
    entityId: '1030',
    versionCode: 'RSV-2026-007',
    oldValue: '{"status":"ACTIVE"}',
    newValue: '{"status":"DEPRECATED"}',
    result: 'Success',
    occurredAt: minutesAgo(340),
    clientIp: '10.10.21.33',
    remark: '退役原因：被 RSV-2026-009 取代'
  },
  {
    id: 9014,
    userId: 2,
    userCode: 'pmc',
    actionCode: 'CreateCandidateRun',
    module: 'run',
    entityType: 'ScheduleRun',
    entityId: '317',
    newValue: '{"purpose":"CANDIDATE","domainKeys":["FAMILY_INJECTION"]}',
    result: 'Success',
    occurredAt: minutesAgo(400),
    clientIp: '10.10.21.58',
    remark: '白天试算（不激活）'
  },
  {
    id: 9013,
    userId: 1,
    userCode: 'admin',
    actionCode: 'Create',
    module: 'domain',
    entityType: 'DomainDefinition',
    entityId: '6',
    newValue: '{"domainKey":"FAMILY_ASSEMBLY","isActive":true}',
    result: 'Success',
    occurredAt: minutesAgo(460),
    clientIp: '10.10.21.33'
  },
  {
    id: 9012,
    userId: 1,
    userCode: 'admin',
    actionCode: 'Update',
    module: 'domain',
    entityType: 'DomainDefinition',
    entityId: '6',
    oldValue: '{"description":"装配域（旧）"}',
    newValue: '{"description":"装配域"}',
    result: 'Success',
    occurredAt: minutesAgo(500),
    clientIp: '10.10.21.33'
  },
  {
    id: 9011,
    userId: 1,
    userCode: 'admin',
    actionCode: 'Enable',
    module: 'domain',
    entityType: 'DomainDefinition',
    entityId: '5',
    oldValue: '{"isActive":false}',
    newValue: '{"isActive":true}',
    result: 'Success',
    occurredAt: minutesAgo(560),
    clientIp: '10.10.21.33'
  },
  {
    id: 9010,
    userId: 1,
    userCode: 'admin',
    actionCode: 'Disable',
    module: 'domain',
    entityType: 'DomainDefinition',
    entityId: '4',
    oldValue: '{"isActive":true}',
    newValue: '{"isActive":false}',
    result: 'Success',
    occurredAt: minutesAgo(620),
    clientIp: '10.10.21.33',
    remark: '临时停用待工艺确认'
  },
  {
    id: 9009,
    userId: 1,
    userCode: 'admin',
    actionCode: 'Create',
    module: 'setup',
    entityType: 'SetupTransitionRule',
    entityId: '88',
    newValue:
      '{"ruleType":"EXACT","operationCode":"OP-INJ-01","fromMaterialCode":"MAT_A","toMaterialCode":"MAT_B","setupMinutes":30}',
    result: 'Success',
    occurredAt: minutesAgo(700),
    clientIp: '10.10.21.33',
    remark: 'Setup 换型规则（v1.5 专项）'
  },
  {
    id: 9008,
    userId: 1,
    userCode: 'admin',
    actionCode: 'Update',
    module: 'setup',
    entityType: 'SetupTransitionRule',
    entityId: '88',
    oldValue: '{"setupMinutes":30}',
    newValue: '{"setupMinutes":25}',
    result: 'Success',
    occurredAt: minutesAgo(760),
    clientIp: '10.10.21.33'
  },
  {
    id: 9007,
    userId: 1,
    userCode: 'admin',
    actionCode: 'Delete',
    module: 'setup',
    entityType: 'SetupTransitionRule',
    entityId: '91',
    oldValue: '{"ruleType":"DEFAULT","operationCode":"OP-INJ-02","setupMinutes":15}',
    result: 'Failed',
    occurredAt: minutesAgo(820),
    clientIp: '10.10.21.33',
    errorMessage: '目标 RuleSetVersion 非 DRAFT，禁止删除（400）'
  },
  {
    id: 9006,
    userId: 2,
    userCode: 'pmc',
    actionCode: 'ConfirmCandidate',
    module: 'run',
    entityType: 'PlanVersion',
    entityId: '2081',
    planVersionId: 2081,
    result: 'Success',
    occurredAt: minutesAgo(900),
    clientIp: '10.10.21.58',
    batchNo: 'BATCH-20260919-01'
  },
  {
    id: 9005,
    userId: 1,
    userCode: 'admin',
    actionCode: 'Publish',
    module: 'governance',
    entityType: 'ParameterSetVersion',
    entityId: '77',
    versionCode: 'PSV-2026-003',
    oldValue: '{"status":"DRAFT"}',
    newValue: '{"status":"ACTIVE"}',
    result: 'Success',
    occurredAt: minutesAgo(1000),
    clientIp: '10.10.21.33',
    remark: '发布原因：Default Purchase LT 调整'
  },
  {
    id: 9004,
    userId: 1,
    userCode: 'admin',
    actionCode: 'AssignPermissions',
    module: 'auth',
    entityType: 'Role',
    entityId: '2',
    oldValue: '["aps.rule.view"]',
    newValue: '["aps.rule.view","aps.rule.edit","aps.rule.publish"]',
    result: 'Failed',
    occurredAt: minutesAgo(1100),
    clientIp: '10.10.21.33',
    errorMessage: '权限码 aps.rule.publishx 不存在（422）'
  },
  {
    id: 9003,
    userId: 3,
    userCode: 'viewer',
    actionCode: 'Update',
    module: 'auth',
    entityType: 'User',
    entityId: '3',
    result: 'Failed',
    occurredAt: minutesAgo(1250),
    clientIp: '10.10.24.9',
    errorMessage: '403：缺少 aps.auth.user.edit（U38 后端拒绝越权）'
  },
  {
    id: 9002,
    userId: 1,
    userCode: 'admin',
    actionCode: 'AssignScopes',
    module: 'auth',
    entityType: 'User',
    entityId: '2',
    newValue: '[{"scopeType":"Domain","scopeValue":"FAMILY_INJECTION"}]',
    result: 'Success',
    occurredAt: minutesAgo(1400),
    clientIp: '10.10.21.33'
  },
  {
    id: 9001,
    userId: 1,
    userCode: 'admin',
    actionCode: 'Create',
    module: 'auth',
    entityType: 'Role',
    entityId: '4',
    newValue: '{"roleCode":"aps.viewer.management","roleName":"管理查看员"}',
    result: 'Success',
    occurredAt: minutesAgo(1600),
    clientIp: '10.10.21.33'
  }
]

/* ==================== Mock 查询实现（模拟服务端过滤 + 分页） ==================== */

function mockListAuditLogs(query: AuditLogQuery): AuditLogDto[] {
  const { userId, action, from, to, page = 1, size = 20 } = query
  let rows = [...MOCK_AUDIT_LOGS]
  if (userId != null) rows = rows.filter((r) => r.userId === userId)
  // 与后端一致：action 精确等值匹配（AuditLogRepository L120 Contains 判空后 ==）
  if (action && action.trim()) rows = rows.filter((r) => r.actionCode === action.trim())
  if (from) {
    const fromIso = new Date(from).toISOString()
    rows = rows.filter((r) => r.occurredAt >= fromIso)
  }
  if (to) {
    const toIso = new Date(to).toISOString()
    rows = rows.filter((r) => r.occurredAt <= toIso)
  }
  // 与后端一致：OccurredAt 倒序
  rows.sort((a, b) => b.occurredAt.localeCompare(a.occurredAt))
  const safePage = Math.max(page, 1)
  const safeSize = Math.min(Math.max(size, 1), 200)
  return rows.slice((safePage - 1) * safeSize, safePage * safeSize)
}

/* ==================== API ==================== */

export const auditApi = {
  /**
   * 审计日志分页查询（GET /api/rbac/audit-logs，Policy=aps.audit.view）
   *
   * 返回**本页数组**（无 total——后端 QueryPagedAsync 不返回总数）；
   * 调用方以 `rows.length === size` 推断是否可能有下一页（hasMore）。
   */
  async listAuditLogs(query: AuditLogQuery = {}): Promise<AuditLogDto[]> {
    if (APS_USE_MOCK) return mockListAuditLogs(query)
    const params: Record<string, string | number> = {
      page: query.page ?? 1,
      size: query.size ?? 20
    }
    if (query.userId != null) params.userId = query.userId
    if (query.action) params.action = query.action
    if (query.from) params.from = query.from
    if (query.to) params.to = query.to
    return apsHttp.get<AuditLogDto[]>({ url: '/api/rbac/audit-logs', params })
  }
}
