/**
 * APS V1 4号位 — Setup 换型规则 API（13 端点，v1.5 Setup 专项）
 *
 * @owner 3号位（后端 API 待落地，4号位 先做前端预备 + mock fallback）
 * @see 冻结文档/APS_V1_4号位页面与业务操作开发实施包_v1.5_20260916_Setup换型规则专项冻结对齐版.md §11.1
 * @see frontNew/docs/3号位催办单-2026-09-17-Setup维护API-12端点.md
 * @see frontNew/src/api/aps-v1/types/setup.ts
 *
 * 13 端点清单（按 v1.5 §11.1 表，前缀 /api/governance）：
 *   1.  GET    /setup-rules/exact?ruleSetVersionId=...              EXACT 规则列表
 *   2.  POST   /setup-rules/exact                                    新增 EXACT
 *   3.  PUT    /setup-rules/exact/{id}                               修改 EXACT
 *   4.  DELETE /setup-rules/exact/{id}                               删除 EXACT
 *   5.  GET    /setup-rules/default?ruleSetVersionId=...             DEFAULT 规则列表
 *   6.  POST   /setup-rules/default                                  新增 DEFAULT
 *   7.  PUT    /setup-rules/default/{id}                             修改 DEFAULT
 *   8.  DELETE /setup-rules/default/{id}                             删除 DEFAULT
 *   9.  GET    /operation-resource-eligibility?operationCode=...&materialId=...  共同合法设备推荐
 *   10. GET    /setup-rules/uncovered-stats?runId=...&...filters     0 分钟兜底统计
 *   11. GET    /rule-set-versions                                    RuleSetVersion 列表（已有，复用）
 *   12. GET    /rule-set-versions/{id}/diff?otherVersionId=...       版本 Diff（需补 Setup 维度）
 *   13. POST   /rule-set-versions/{id}/publish                       发布（已有，复用）
 *
 * 设计要点：
 *  - mock 模式：读端点返回极小 fixture；写端点统一抛 503 防伪成功（与 strategyProfile.ts 同范式）
 *  - 前端不维护 status/createdAt/createdBy 等后端权威字段
 *  - 上层组件调 setupApi.* 即可，store + page 不必关心 unwrap / mock fallback
 *  - 3号位 API 落地后只需把 mock fallback 切到真实端点（APS_USE_MOCK=false）
 */

import { apsHttp, APS_USE_MOCK, ApiError } from './http'
import type {
  SetupRuleDto,
  SetupRuleExactInput,
  SetupRuleDefaultInput,
  SetupUncoveredQuery,
  SetupUncoveredStatDto,
  OperationResourceEligibilityQuery,
  OperationResourceEligibilityDto,
  SetupDiffDto,
  SetupRuleSetVersionDto,
  PublishSetupRuleSetVersionRequest
} from './types/setup'

/* ==================== Mock Fixtures（仅离线 UI 演示） ==================== */

const now = (): string => new Date().toISOString()
const fixedDate = (offset: number): string =>
  new Date(Date.now() - offset * 86400_000).toISOString()

/** mock RuleSetVersion 下拉源（DRAFT 状态可编辑 Setup 规则；status = 治理六态原文，09-20 lps 核实） */
const MOCK_RULE_SET_VERSIONS: SetupRuleSetVersionDto[] = [
  {
    id: 2001,
    ruleSetId: 101,
    versionCode: 'RS-v1.0.0',
    status: 'PUBLISHED',
    effectiveFrom: fixedDate(30),
    publishedAt: fixedDate(30),
    publishedBy: 'admin',
    createdAt: fixedDate(35)
  },
  {
    id: 2002,
    ruleSetId: 101,
    versionCode: 'RS-v1.1.0-draft',
    status: 'DRAFT',
    createdAt: now()
  },
  {
    id: 2003,
    ruleSetId: 102,
    versionCode: 'RS-v2.0.0',
    status: 'PUBLISHED',
    effectiveFrom: fixedDate(20),
    publishedAt: fixedDate(20),
    publishedBy: 'admin',
    createdAt: fixedDate(25)
  }
]

/** mock EXACT 规则（7 元组） */
const MOCK_EXACT_RULES: SetupRuleDto[] = [
  {
    id: 1,
    ruleSetVersionId: 2002,
    ruleType: 'EXACT',
    productionDepartmentId: 1,
    departmentCode: 'DEPT_A',
    stageCode: 'STAGE_1',
    operationCode: 'OP20',
    resourceId: 1,
    resourceCode: 'MC001',
    fromMaterialCode: 'MAT_A',
    toMaterialCode: 'MAT_B',
    fromMaterialId: 1,
    toMaterialId: 2,
    setupMinutes: 45,
    status: 'DRAFT',
    createdAt: fixedDate(5),
    createdBy: 'admin'
  },
  {
    id: 2,
    ruleSetVersionId: 2002,
    ruleType: 'EXACT',
    productionDepartmentId: 1,
    departmentCode: 'DEPT_A',
    stageCode: 'STAGE_1',
    operationCode: 'OP20',
    resourceId: 2,
    resourceCode: 'MC002',
    fromMaterialCode: 'MAT_B',
    toMaterialCode: 'MAT_C',
    fromMaterialId: 2,
    toMaterialId: 3,
    setupMinutes: 30,
    status: 'DRAFT',
    createdAt: fixedDate(4),
    createdBy: 'admin'
  },
  {
    id: 3,
    ruleSetVersionId: 2002,
    ruleType: 'EXACT',
    productionDepartmentId: 2,
    departmentCode: 'DEPT_B',
    stageCode: 'STAGE_2',
    operationCode: 'OP30',
    resourceId: 3,
    resourceCode: 'MC003',
    fromMaterialCode: 'MAT_X',
    toMaterialCode: 'MAT_Y',
    fromMaterialId: 4,
    toMaterialId: 5,
    setupMinutes: 60,
    status: 'DRAFT',
    createdAt: fixedDate(3),
    createdBy: 'admin'
  }
]

/** mock DEFAULT 规则（5 元组） */
const MOCK_DEFAULT_RULES: SetupRuleDto[] = [
  {
    id: 101,
    ruleSetVersionId: 2002,
    ruleType: 'DEFAULT',
    productionDepartmentId: 1,
    departmentCode: 'DEPT_A',
    stageCode: 'STAGE_1',
    operationCode: 'OP20',
    resourceId: 1,
    resourceCode: 'MC001',
    setupMinutes: 15,
    status: 'DRAFT',
    createdAt: fixedDate(5),
    createdBy: 'admin'
  },
  {
    id: 102,
    ruleSetVersionId: 2002,
    ruleType: 'DEFAULT',
    productionDepartmentId: 1,
    departmentCode: 'DEPT_A',
    stageCode: 'STAGE_1',
    operationCode: 'OP20',
    resourceId: 2,
    resourceCode: 'MC002',
    setupMinutes: 20,
    status: 'DRAFT',
    createdAt: fixedDate(4),
    createdBy: 'admin'
  }
]

/** mock 0 分钟兜底统计（§5.3） */
const MOCK_UNCOVERED_STATS: SetupUncoveredStatDto[] = [
  {
    departmentCode: 'DEPT_A',
    stageCode: 'STAGE_1',
    operationCode: 'OP20',
    resourceCode: 'MC001',
    fromMaterialCode: 'MAT_C',
    toMaterialCode: 'MAT_D',
    count: 12,
    lastOccurrence: fixedDate(1)
  },
  {
    departmentCode: 'DEPT_A',
    stageCode: 'STAGE_1',
    operationCode: 'OP20',
    resourceCode: 'MC002',
    fromMaterialCode: 'MAT_D',
    toMaterialCode: 'MAT_E',
    count: 8,
    lastOccurrence: fixedDate(2)
  },
  {
    departmentCode: 'DEPT_B',
    stageCode: 'STAGE_2',
    operationCode: 'OP30',
    resourceCode: 'MC003',
    fromMaterialCode: 'MAT_Y',
    toMaterialCode: 'MAT_Z',
    count: 5,
    lastOccurrence: fixedDate(3)
  }
]

/** mock 共同合法设备推荐（§3.3；key = operationCode_fromMaterialId_toMaterialId，偏差 #5 后传 Id） */
const MOCK_ELIGIBILITY: Record<string, OperationResourceEligibilityDto> = {
  OP20_1_2: {
    operationCode: 'OP20',
    resourceCodes: ['MC001', 'MC002'],
    resources: [
      {
        resourceCode: 'MC001',
        resourceName: '设备 001',
        departmentCode: 'DEPT_A',
        stageCode: 'STAGE_1'
      },
      {
        resourceCode: 'MC002',
        resourceName: '设备 002',
        departmentCode: 'DEPT_A',
        stageCode: 'STAGE_1'
      }
    ]
  },
  OP20_2_3: {
    operationCode: 'OP20',
    resourceCodes: ['MC002', 'MC003'],
    resources: [
      {
        resourceCode: 'MC002',
        resourceName: '设备 002',
        departmentCode: 'DEPT_A',
        stageCode: 'STAGE_1'
      },
      {
        resourceCode: 'MC003',
        resourceName: '设备 003',
        departmentCode: 'DEPT_B',
        stageCode: 'STAGE_2'
      }
    ]
  },
  OP30_4_5: {
    operationCode: 'OP30',
    resourceCodes: ['MC003'],
    resources: [
      {
        resourceCode: 'MC003',
        resourceName: '设备 003',
        departmentCode: 'DEPT_B',
        stageCode: 'STAGE_2'
      }
    ]
  }
}

/** mock 版本 Diff（§6.3） */
const MOCK_DIFF: SetupDiffDto = {
  ruleSetVersionId: 2002,
  otherVersionId: 2001,
  addedCount: 2,
  modifiedCount: 1,
  removedCount: 0,
  publishStatus: 'DRAFT',
  setupRuleChanges: {
    added: [
      {
        ruleType: 'EXACT',
        operationCode: 'OP20',
        departmentCode: 'DEPT_A',
        stageCode: 'STAGE_1',
        resourceCode: 'MC001',
        fromMaterialCode: 'MAT_A',
        toMaterialCode: 'MAT_B',
        setupMinutes: 45
      },
      {
        ruleType: 'DEFAULT',
        operationCode: 'OP20',
        departmentCode: 'DEPT_A',
        stageCode: 'STAGE_1',
        resourceCode: 'MC002',
        setupMinutes: 20
      }
    ],
    modified: [
      {
        ruleType: 'EXACT',
        id: 2,
        operationCode: 'OP20',
        before: { setupMinutes: 25 },
        after: { setupMinutes: 30 }
      }
    ],
    removed: []
  },
  comparedAt: now()
}

/** mock 模式 write 端点统一抛 503（防伪成功误导；dev APS_USE_MOCK=false 不走 mock） */
const rejectMockWrite = (op: string): Promise<never> =>
  Promise.reject(new ApiError(`mock 模式禁止写 Setup 规则（${op}）`, 503))

/* ==================== API ==================== */

export const setupApi = {
  /* ===== 1. EXACT 规则列表 ===== */
  async listExactRules(ruleSetVersionId: number): Promise<SetupRuleDto[]> {
    if (APS_USE_MOCK) {
      return MOCK_EXACT_RULES.filter((r) => r.ruleSetVersionId === ruleSetVersionId)
    }
    return apsHttp.get<SetupRuleDto[]>({
      url: '/api/governance/setup-rules/exact',
      params: { ruleSetVersionId }
    })
  },

  /* ===== 2. 新增 EXACT ===== */
  async createExactRule(input: SetupRuleExactInput): Promise<SetupRuleDto> {
    if (APS_USE_MOCK) return rejectMockWrite('createExactRule')
    return apsHttp.post<SetupRuleDto>({
      url: '/api/governance/setup-rules/exact',
      data: input
    })
  },

  /* ===== 3. 修改 EXACT ===== */
  async updateExactRule(id: number, input: SetupRuleExactInput): Promise<SetupRuleDto> {
    if (APS_USE_MOCK) return rejectMockWrite('updateExactRule')
    return apsHttp.put<SetupRuleDto>({
      url: `/api/governance/setup-rules/exact/${id}`,
      data: input
    })
  },

  /* ===== 4. 删除 EXACT ===== */
  async deleteExactRule(id: number): Promise<void> {
    if (APS_USE_MOCK) return rejectMockWrite('deleteExactRule')
    await apsHttp.delete<void>({
      url: `/api/governance/setup-rules/exact/${id}`
    })
  },

  /* ===== 5. DEFAULT 规则列表 ===== */
  async listDefaultRules(ruleSetVersionId: number): Promise<SetupRuleDto[]> {
    if (APS_USE_MOCK) {
      return MOCK_DEFAULT_RULES.filter((r) => r.ruleSetVersionId === ruleSetVersionId)
    }
    return apsHttp.get<SetupRuleDto[]>({
      url: '/api/governance/setup-rules/default',
      params: { ruleSetVersionId }
    })
  },

  /* ===== 6. 新增 DEFAULT ===== */
  async createDefaultRule(input: SetupRuleDefaultInput): Promise<SetupRuleDto> {
    if (APS_USE_MOCK) return rejectMockWrite('createDefaultRule')
    return apsHttp.post<SetupRuleDto>({
      url: '/api/governance/setup-rules/default',
      data: input
    })
  },

  /* ===== 7. 修改 DEFAULT ===== */
  async updateDefaultRule(id: number, input: SetupRuleDefaultInput): Promise<SetupRuleDto> {
    if (APS_USE_MOCK) return rejectMockWrite('updateDefaultRule')
    return apsHttp.put<SetupRuleDto>({
      url: `/api/governance/setup-rules/default/${id}`,
      data: input
    })
  },

  /* ===== 8. 删除 DEFAULT ===== */
  async deleteDefaultRule(id: number): Promise<void> {
    if (APS_USE_MOCK) return rejectMockWrite('deleteDefaultRule')
    await apsHttp.delete<void>({
      url: `/api/governance/setup-rules/default/${id}`
    })
  },

  /* ===== 9. 共同合法设备推荐（§3.3） ===== */
  async getOperationResourceEligibility(
    query: OperationResourceEligibilityQuery
  ): Promise<OperationResourceEligibilityDto> {
    if (APS_USE_MOCK) {
      const key = `${query.operationCode}_${query.materialId}_${query.toMaterialId ?? ''}`
      const mock = MOCK_ELIGIBILITY[key]
      if (mock) return mock
      // fallback: 返回空列表（无共同合法设备）
      return {
        operationCode: query.operationCode,
        resourceCodes: [],
        resources: []
      }
    }
    return apsHttp.get<OperationResourceEligibilityDto>({
      url: '/api/governance/operation-resource-eligibility',
      params: { ...query } as Record<string, unknown>
    })
  },

  /* ===== 10. 0 分钟兜底统计（§5） ===== */
  async getUncoveredStats(query: SetupUncoveredQuery): Promise<SetupUncoveredStatDto[]> {
    if (APS_USE_MOCK) {
      let arr = MOCK_UNCOVERED_STATS
      if (query.departmentCode) {
        arr = arr.filter((s) => s.departmentCode === query.departmentCode)
      }
      if (query.stageCode) {
        arr = arr.filter((s) => s.stageCode === query.stageCode)
      }
      if (query.resourceCode) {
        arr = arr.filter((s) => s.resourceCode === query.resourceCode)
      }
      return arr
    }
    return apsHttp.get<SetupUncoveredStatDto[]>({
      url: '/api/governance/setup-rules/uncovered-stats',
      params: { ...query } as Record<string, unknown>
    })
  },

  /* ===== 11. RuleSetVersion 列表（已有，复用） ===== */
  async listRuleSetVersions(params?: {
    ruleSetId?: number
    status?: string
  }): Promise<SetupRuleSetVersionDto[]> {
    if (APS_USE_MOCK) {
      let arr = MOCK_RULE_SET_VERSIONS
      if (params?.ruleSetId) {
        arr = arr.filter((v) => v.ruleSetId === params.ruleSetId)
      }
      if (params?.status) {
        arr = arr.filter((v) => v.status === params.status)
      }
      return arr
    }
    return apsHttp.get<SetupRuleSetVersionDto[]>({
      url: '/api/governance/rule-set-versions',
      params
    })
  },

  /* ===== 12. 版本 Diff（需补 Setup 维度） ===== */
  async diffRuleSetVersions(
    ruleSetVersionId: number,
    otherVersionId: number
  ): Promise<SetupDiffDto> {
    if (APS_USE_MOCK) {
      // mock 模式返回固定 Diff（演示用）
      return {
        ...MOCK_DIFF,
        ruleSetVersionId,
        otherVersionId
      }
    }
    return apsHttp.get<SetupDiffDto>({
      url: `/api/governance/rule-set-versions/${ruleSetVersionId}/diff`,
      params: { otherVersionId }
    })
  },

  /* ===== 13. 发布（已有，复用） ===== */
  async publishRuleSetVersion(
    ruleSetVersionId: number,
    request: PublishSetupRuleSetVersionRequest
  ): Promise<void> {
    if (APS_USE_MOCK) return rejectMockWrite('publishRuleSetVersion')
    await apsHttp.post<void>({
      url: `/api/governance/rule-set-versions/${ruleSetVersionId}/publish`,
      data: request
    })
  }
}

/* ==================== 导出 mock actor（与 rule.ts 同范式） ==================== */

export const mockActor = {
  userCode: 'admin',
  userName: '系统管理员',
  roles: ['aps.admin.system']
}
