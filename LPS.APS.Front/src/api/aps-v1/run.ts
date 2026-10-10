/**
 * APS V1 4号位 — 运行/版本/MES 下发状态 API（占位）
 *
 * @owner 3号位（recoverFailedRun / triggerReschedule 运行治理） + 5号位（status / MES 资格查询）
 * @see 审核报告 §十六.4↔3 + §十八（G4/G7/G8/Schedule 迁移 5号位）+ §十九.MES展示/操作
 *
 * @see 4号位文档第 15-16 节（页面 9）
 * 验收：U01 / U02 / U19 / U16-U18（MES 资格）
 */

import { apsHttp, APS_USE_MOCK } from './http'
import { findMockActivatableCandidate } from './candidate'
import type {
  RecoverFailedRunInput,
  RecoverFailedRunResult,
  RunStatusDto,
  TriggerRescheduleInput,
  TriggerRescheduleResult,
  MesDispatchInput,
  MesDispatchResult,
  MesEligibilityDto
} from './types'

const isoNow = () => new Date().toISOString()
const isoAgo = (ms: number) => new Date(Date.now() - ms).toISOString()
const isoFromNow = (ms: number) => new Date(Date.now() + ms).toISOString()

const DOMAINS = ['FAMILY_INJECTION', 'FAMILY_ASSEMBLY', 'FAMILY_TEST'] as const

const mockRunStatus: RunStatusDto = {
  recentRuns: [
    {
      runId: 5003,
      runType: 'LOCAL_RESCHEDULE',
      status: 'SUCCESS',
      dataCutoffTime: isoAgo(3600_000),
      strategyProfileVersion: 'SP-DEFAULT v5',
      expectedDomainKeys: [...DOMAINS],
      startedAt: isoAgo(3600_000),
      completedAt: isoAgo(3540_000)
    },
    {
      runId: 5002,
      runType: 'INSERT_ORDER_WHATIF',
      status: 'SUCCESS',
      dataCutoffTime: isoAgo(5400_000),
      strategyProfileVersion: 'SP-DEFAULT v5',
      expectedDomainKeys: [...DOMAINS],
      startedAt: isoAgo(5400_000),
      completedAt: isoAgo(5340_000)
    },
    {
      runId: 5001,
      runType: 'FULL_SCHEDULE',
      status: 'PARTIAL_SUCCESS',
      dataCutoffTime: isoAgo(7200_000),
      strategyProfileVersion: 'SP-DEFAULT v5',
      expectedDomainKeys: [...DOMAINS],
      startedAt: isoAgo(7200_000),
      completedAt: isoAgo(7000_000)
    },
    {
      runId: 5000,
      runType: 'FULL_SCHEDULE',
      status: 'FAILED',
      dataCutoffTime: isoAgo(2 * 86400_000),
      strategyProfileVersion: 'SP-DEFAULT v5',
      expectedDomainKeys: [...DOMAINS],
      startedAt: isoAgo(2 * 86400_000),
      completedAt: new Date(Date.now() - 2 * 86400_000 + 3600_000).toISOString(),
      errorMessage: 'TEST Domain 求解器 OOM'
    },
    {
      runId: 4999,
      runType: 'MANUAL_RESCHEDULE',
      status: 'SUCCESS',
      dataCutoffTime: isoAgo(3 * 86400_000),
      strategyProfileVersion: 'SP-DEFAULT v4',
      expectedDomainKeys: ['FAMILY_INJECTION', 'FAMILY_ASSEMBLY'],
      startedAt: isoAgo(3 * 86400_000),
      completedAt: isoAgo(3 * 86400_000 + 1800_000)
    },
    {
      runId: 4998,
      runType: 'FULL_SCHEDULE',
      status: 'SUCCESS',
      dataCutoffTime: isoAgo(4 * 86400_000),
      strategyProfileVersion: 'SP-DEFAULT v4',
      expectedDomainKeys: [...DOMAINS],
      startedAt: isoAgo(4 * 86400_000),
      completedAt: isoAgo(4 * 86400_000 - 3600_000)
    }
  ],
  activeVersions: [
    {
      domainKey: 'FAMILY_INJECTION',
      planVersionId: 1001,
      versionCode: 'V-2026-001-INJECTION',
      status: 'ACTIVE',
      activatedAt: isoAgo(7200_000),
      planHorizonStart: isoAgo(7 * 86400_000),
      planHorizonEnd: isoFromNow(83 * 86400_000)
    },
    {
      domainKey: 'FAMILY_ASSEMBLY',
      planVersionId: 1002,
      versionCode: 'V-2026-001-ASSEMBLY',
      status: 'ACTIVE',
      activatedAt: isoAgo(7200_000),
      planHorizonStart: isoAgo(7 * 86400_000),
      planHorizonEnd: isoFromNow(83 * 86400_000)
    },
    {
      domainKey: 'FAMILY_TEST',
      planVersionId: 1003,
      versionCode: 'C-2026-003-TEST',
      status: 'CANDIDATE',
      activatedAt: isoAgo(7200_000),
      planHorizonStart: isoAgo(7 * 86400_000),
      planHorizonEnd: isoFromNow(83 * 86400_000),
      errorMessage: '等待上游 FAMILY_INJECTION 重排后激活'
    }
  ],
  currentPartialBreakdown: {
    scheduleRunId: 5001,
    successDomains: [
      { domainKey: 'FAMILY_INJECTION', status: 'SUCCESS', planVersionId: 1001 },
      { domainKey: 'FAMILY_ASSEMBLY', status: 'SUCCESS', planVersionId: 1002 }
    ],
    failedDomains: [
      { domainKey: 'FAMILY_TEST', status: 'FAILED', errorMessage: 'PI-2026-3320 Position 缺数据' }
    ],
    blockedDomains: [
      {
        domainKey: 'FAMILY_TEST',
        status: 'BLOCKED_BY_UPSTREAM',
        blockedByDomainKey: 'FAMILY_INJECTION'
      }
    ]
  },
  mesEligibilitySamples: [
    {
      taskId: 1001,
      taskNo: 'T1001',
      eligibility: 'INELIGIBLE',
      reasons: ['CANDIDATE'],
      domainKey: 'FAMILY_TEST'
    },
    {
      taskId: 1010,
      taskNo: 'T1010',
      eligibility: 'INELIGIBLE',
      reasons: ['UNLOCATED', 'PLANNING_PLACEHOLDER_DEPENDENCY'],
      dispatchWindow: { from: isoFromNow(3600_000), to: isoFromNow(8 * 3600_000) },
      domainKey: 'FAMILY_INJECTION'
    },
    {
      taskId: 1011,
      taskNo: 'T1011',
      eligibility: 'INELIGIBLE',
      reasons: ['OUT_OF_DISPATCH_WINDOW'],
      domainKey: 'FAMILY_INJECTION'
    },
    {
      taskId: 1012,
      taskNo: 'T1012',
      eligibility: 'INELIGIBLE',
      reasons: ['CANCELLED'],
      domainKey: 'FAMILY_ASSEMBLY'
    },
    {
      taskId: 1020,
      taskNo: 'T1020',
      eligibility: 'ELIGIBLE',
      dispatchWindow: { from: isoFromNow(3600_000), to: isoFromNow(8 * 3600_000) },
      domainKey: 'FAMILY_INJECTION'
    },
    {
      taskId: 1021,
      taskNo: 'T1021',
      eligibility: 'ELIGIBLE',
      dispatchWindow: { from: isoFromNow(3600_000), to: isoFromNow(8 * 3600_000) },
      domainKey: 'FAMILY_ASSEMBLY'
    },
    {
      taskId: 1022,
      taskNo: 'T1022',
      eligibility: 'UNKNOWN',
      domainKey: 'FAMILY_TEST'
    }
  ]
}

export const runApi = {
  /** Run 列表 + 状态聚合（页面 9 入口）
   *  v1.4 §十九：Run/PlanVersion 普通查询走 5号位（或 3号位 Governance）
   *  当前用 GovernanceController 的 /api/governance/runs（聚合所有 ScheduleRun）
   *  ⚠️ 后端无对应 PlanVersion 聚合端点；DTO 不一致时降级 mock
   */
  async getStatus(): Promise<RunStatusDto> {
    if (APS_USE_MOCK) return mockRunStatus
    return apsHttp.get<RunStatusDto>({ url: '/api/governance/runs' })
  },

  /**
   * 校验 ScheduleRun.ExpectedDomainKeysJson 冻结规则（v1.4 §十九.2）
   *  - P0-08：FULL_SCHEDULE ≥ 1 Domain；RESCHEDULE 恰 1 Domain
   *  - 后端：POST /api/governance/run/{scheduleRunId}/validate-domain-keys（空 body）
   *  - 返回 200 → 通过；400 → 冻结规则冲突，提示用户无法恢复
   *  - 前端在 recoverFailedRun 之前先调一次（预校验），避免空恢复直接 400
   */
  async validateDomainKeys(scheduleRunId: number): Promise<{ valid: boolean; message: string }> {
    if (APS_USE_MOCK) {
      // mock：mockRunStatus 里所有 FAILED Run 都通过校验（演示用）
      return { valid: true, message: 'mock 校验通过（演示）' }
    }
    try {
      await apsHttp.post<unknown>({
        url: `/api/governance/run/${scheduleRunId}/validate-domain-keys`,
        data: {}
      })
      return { valid: true, message: 'DomainKey 冻结规则校验通过' }
    } catch (err) {
      // 400 → 校验失败；其它 → 视为无效（兜底）
      const apiErr = err as { code?: number; message?: string }
      return {
        valid: false,
        message: apiErr?.message ?? `校验失败 (code=${apiErr?.code ?? '?'})`
      }
    }
  },

  /** FAILED Run 恢复（§十九.2 / U19）：必须新建 ScheduleRun，不得改回 RUNNING
   *  后端：POST /api/governance/run/{failedScheduleRunId}/recover
   */
  async recoverFailedRun(input: RecoverFailedRunInput): Promise<RecoverFailedRunResult> {
    if (APS_USE_MOCK) {
      return {
        newRunId: input.originalRunId + 1000,
        originalRunId: input.originalRunId,
        originalRunStatus: 'FAILED',
        newRunStatus: 'RUNNING',
        startedAt: isoNow()
      }
    }
    return apsHttp.post<RecoverFailedRunResult>({
      url: `/api/governance/run/${input.originalRunId}/recover`,
      data: input
    })
  },

  /**
   * 候选重排触发（§十九.2 / 文档第 6 节 / 12 节 / 17 节）
   *  - 从 Gantt Drawer / Resource Drawer / "批量人工重排" 入口发起，针对选中 Task/Resource 的 Domain
   *  - P0-03：根据 input.runType 创建 LOCAL_RESCHEDULE（小范围）或 MANUAL_RESCHEDULE（较大范围）
   *  - 成功后前端跳到 Candidate 对比页（页面5）确认采用
   *  2026-09-17 3号位 回执后确认（@see 3号位回执_致4号位_治理端点对接确认_v1.0_20260917.md §四）：
   *   - 端点：POST /api/governance/run/candidate（GovernanceController.cs L627）
   *   - 权限码：aps.plan.run（aps.run.reschedule / aps.run.candidate 均不存在）
   *   - 入参：CreateCandidateRunRequest（字段 RunType / Purpose / DomainKey / BasePlanVersionId? / DataCutoffTime? / Remark?）
   *   - objectRefIds 砍掉（后端无此字段）；reason → Remark
   */
  async triggerReschedule(input: TriggerRescheduleInput): Promise<TriggerRescheduleResult> {
    if (APS_USE_MOCK) {
      // mock：模拟 1.5s 异步，生成 RunId + CandidatePlanVersionId
      await new Promise((resolve) => setTimeout(resolve, 1500))
      // 归一到一个 CANDIDATES 里已存在的候选，保证跳转 Candidate 页能加载对比详情
      // （原 `2000 + domainKey.length` 是孤立 ID，如 FAMILY_INJECTION → 2016，list 查不到 + CANDIDATES[2016] 不存在）
      const mockCand = findMockActivatableCandidate(input.domainKey)
      const domainHash = input.domainKey.length
      return {
        newRunId: 6000 + domainHash,
        candidatePlanVersionId: mockCand?.header.candidatePlanVersionId ?? 2001,
        basePlanVersionId: input.basePlanVersionId ?? mockCand?.header.basePlanVersionId ?? 1001,
        startedAt: isoNow(),
        estimatedDurationSeconds: 30
      }
    }
    // 真实模式：字段映射为后端 CreateCandidateRunRequest（camelCase）
    //  - remark **停传**（2026-09-21 3号位 回执 §一.3：CandidateRunCreateSpec 无 Remark 属性，后端维持丢弃）
    //  - scope 条件透传（§10A；缺省不发 → 后端静默放行，旧调用兼容）
    const data: Record<string, unknown> = {
      runType: input.runType,
      purpose: input.purpose,
      domainKey: input.domainKey,
      basePlanVersionId: input.basePlanVersionId,
      dataCutoffTime: input.dataCutoffTime
    }
    if (input.scope) {
      data.scope = input.scope
    }
    return apsHttp.post<TriggerRescheduleResult>({
      url: '/api/governance/run/candidate',
      data
    })
  },

  /**
   * MES 下发（§十九.MES展示/操作）
   *  - 仅 ELIGIBLE Task 可下发；INELIGIBLE / UNKNOWN 由 UI 按钮 disabled 强制
   *  - 4号位 → 5号位中转 → MES
   *  - P1-15：store 层 assertScope(domainKey) 前端防御；3号位后端二次校验
   *  - V1 不建设 PAUSE/RESUME 闭环（P1-13）；下发后由 MES 接管 Task 状态
   *
   * 入参 MesDispatchInput 由 caller 拼装（含 actor + taskId + remark）
   */
  async dispatch(input: MesDispatchInput): Promise<MesDispatchResult> {
    if (APS_USE_MOCK) {
      // mock：模拟 800ms 异步，返回 MES 工单号
      await new Promise((resolve) => setTimeout(resolve, 800))
      // 从 mock 样本里反查 taskNo（生产模式 taskNo 由 5号位返回）
      const sample = mockRunStatus.mesEligibilitySamples.find(
        (s: MesEligibilityDto) => s.taskId === input.taskId
      )
      const taskNo = sample?.taskNo ?? `T${input.taskId}`
      return {
        taskId: input.taskId,
        taskNo,
        mesDispatchId: `MES-${input.taskId}-${Date.now()}`,
        dispatchedAt: isoNow(),
        status: 'DISPATCHED',
        actor: input.actor
      }
    }
    return apsHttp.post<MesDispatchResult>({
      url: '/api/mes/dispatch',
      data: input
    })
  }
}
