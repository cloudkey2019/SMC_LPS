/**
 * APS V1 4号位 — 规则与参数维护 Pinia store（页面 8）
 *
 * 状态（v1.4 §二十 + B 设计稿 §1.2 双轨）：
 *  - ruleSets：列表（页面 8 入口）
 *  - currentDetail：当前选中 RuleSet 的完整详情（向后兼容；UI 重设计后逐步废弃）
 *  - ruleSetBuffer：双轨 RuleSet 缓冲（三态：governance/originalBlocks/workingBlocks + dirty 标志）
 *  - parameterSetBuffer：双轨 ParameterSet 缓冲（与 RuleSet 1:1 关联）
 *  - lastPublish / lastRetire：上一次写入结果（U22 可追溯）
 *  - statusFilter：列表过滤
 *  - loading / error：UI 态
 *
 * 写接口（v1.4 §二十 写维护 + B 设计稿 Step 1 7 函数）：
 *  - 旧：validateDraft / publish / retire（向后兼容；走 publishRuleSet）
 *  - 新：createRuleSetDraft / updateRuleSetDraft / createParameterSetDraft / updateParameterSetDraft
 *  - 新：forkDraft（入口 B — 建草稿）/ onCellEdit / onSaveDraft / onCancelDirty
 *
 * 权限（前端按钮显隐）：
 *  - ROLE_ADMIN：可加载详情、查看 Diff、提交校验、创建/编辑 DRAFT
 *  - ROLE_PUBLISHER：可发布 / 退役
 *  - v1.2 §23.1 后改为权限码门控：aps.rule.edit / aps.rule.publish
 */

import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import {
  ruleApi,
  mockActor,
  ApiError,
  canCreateDraftForStatus,
  type RuleSetSummaryDto,
  type RuleSetDetailDto,
  type RuleDiffDto,
  type PublishRuleResult,
  type RetireRuleResult,
  type RuleStatus,
  type RoleKey,
  type RuleSetVersion,
  type ParameterSetVersion,
  type PublishGovernanceInput,
  type ForkDraftResult,
  type ParameterSetBlocks,
  type BlockKey,
  BLOCK_KEYS
} from '@/api/aps-v1'
import {
  flattenBlocks,
  applyEdit,
  buildPutBody,
  parseParameterSetBlocks,
  type ParameterRow,
  type JsonValue
} from '@/api/aps-v1/draftBuffer'
import { useApsAuthStore } from './auth'
import { assertScope, ScopeViolationError, filterByScope } from './scope'

/* ===== 双轨 Buffer 类型（B 设计稿 §1.2） ===== */

/** 脏标记：5 块各自独立标记 */
type DirtyFlags = Record<BlockKey, boolean>

/** 双轨 Buffer 三态：governance / originalBlocks / workingBlocks / dirty */
export interface BufferState {
  governance: RuleSetVersion | ParameterSetVersion | null
  originalBlocks: ParameterSetBlocks | null
  workingBlocks: ParameterSetBlocks | null
  dirty: DirtyFlags
  loading: boolean
  saving: boolean
}

const emptyDirtyFlags = (): DirtyFlags => ({
  lock: false,
  supply: false,
  procurement: false,
  solverStrategy: false,
  candidateGuardrail: false
})

const emptyBlocks = (): ParameterSetBlocks => ({
  lock: {},
  supply: {},
  procurement: {},
  solverStrategy: {},
  candidateGuardrail: {}
})

const emptyBufferState = (): BufferState => ({
  governance: null,
  originalBlocks: null,
  workingBlocks: null,
  dirty: emptyDirtyFlags(),
  loading: false,
  saving: false
})

/** Buffer 选择（用于 onCellEdit/onCancelDirty/onSaveDraft 等参数） */
export type BufferChoice = 'ruleSetBuffer' | 'parameterSetBuffer'

export const useRulesStore = defineStore('aps.rules', () => {
  // ===== state =====
  const ruleSets = ref<RuleSetSummaryDto[]>([])
  const currentDetail = ref<RuleSetDetailDto | null>(null)
  const currentDiff = ref<RuleDiffDto | null>(null)
  const lastPublish = ref<PublishRuleResult | null>(null)
  const lastRetire = ref<RetireRuleResult | null>(null)
  const statusFilter = ref<RuleStatus | 'ALL'>('ALL')
  const loading = ref(false)
  const listLoading = ref(false)
  const actionRunning = ref(false)
  const validating = ref(false)
  const error = ref<string | null>(null)

  // ===== 双轨 Buffer state（B 设计稿 §1.2） =====
  /** RuleSet 轨三态（governance / originalBlocks / workingBlocks / dirty） */
  const ruleSetBuffer = ref<BufferState>(emptyBufferState())
  /** ParameterSet 轨三态（与 RuleSet 1:1 关联） */
  const parameterSetBuffer = ref<BufferState>(emptyBufferState())
  /** 当前 RuleSet VersionId（用于细粒度操作） */
  const currentRuleSetVersionId = ref<number | null>(null)
  /** 当前 ParameterSet VersionId（用于细粒度操作） */
  const currentParameterSetVersionId = ref<number | null>(null)

  /** 当前操作者（mock 模式固定；真实模式由 JWT 注入） */
  const actor = ref(mockActor.name)
  /** mock 模式下与 apsAuth.roles 保持同步（P2 角色切换器）；生产模式继续走 mockActor.roles 占位 */
  const apsAuth = useApsAuthStore()
  const actorRoles = computed<RoleKey[]>(() =>
    apsAuth.roles.length > 0 ? apsAuth.roles : mockActor.roles
  )

  // ===== getters =====
  /**
   * 过滤后的 RuleSet 列表
   *  - 状态过滤 + 业务范围（domainKey）过滤；domainKey='ALL' 表示跨域，始终放行
   *  - P1-15：按 dataScope.domainKeys 预过滤（前端防御；3号位后端会二次校验）
   */
  const filteredRuleSets = computed(() => {
    let arr = ruleSets.value
    if (statusFilter.value !== 'ALL') {
      arr = arr.filter((r) => r.status === statusFilter.value)
    }
    return filterByScope(arr, apsAuth.dataScope, (r) =>
      r.domainKey === 'ALL' ? {} : { domainKey: r.domainKey }
    )
  })

  /** 状态分布（顶部 KPI 用）—— v1.4 §二十 6 态 */
  const statusCounts = computed(() => {
    const c: Record<RuleStatus, number> = {
      DRAFT: 0,
      SUBMITTED: 0,
      APPROVED: 0,
      PUBLISHED: 0,
      DISABLED: 0,
      ARCHIVED: 0
    }
    ruleSets.value.forEach((r) => {
      c[r.status] = (c[r.status] ?? 0) + 1
    })
    return c
  })

  /** 当前选中 RuleSet 的 status */
  const currentStatus = computed(() => currentDetail.value?.summary.status ?? null)

  /** 是否有 DRAFT 可发布（用于"发布"按钮显示） */
  const canPublish = computed(() => {
    const s = currentDetail.value?.summary
    return !!s && s.status === 'DRAFT' && s.draftVersion !== undefined
  })

  /** 是否有 PUBLISHED 可退役 */
  const canRetire = computed(() => currentStatus.value === 'PUBLISHED')

  /** 是否有写权限（任意） — v1.2 §23.1 权限码门控
   *  - aps.rule.edit：aps.admin.aps + aps.admin.system 持有
   *  - 不用角色判断的原因：v1.2 DDL 把 RULE_ADMIN/RULE_PUBLISHER 合并为 aps.admin.aps，
   *    该角色持 edit 但不一定持 publish；用权限码区分两按钮
   */
  const canWrite = computed(() => apsAuth.has('aps.rule.edit'))

  /** 是否有发布权限 — aps.rule.publish
   *  - aps.admin.aps + aps.admin.system 都持有（v1.2 DDL 已合并 RULE_ADMIN + RULE_PUBLISHER；
   *    §二十五.3 不能自动拥有发布权由 DDL role 结构保证：aps.admin.aps 内含 publish，
   *    如需进一步拆分只能在 3 号位新建独立角色码）
   */
  const canPublishAction = computed(() => apsAuth.has('aps.rule.publish'))

  /* ===== 双轨 + 6 态 按钮可见性 getters（B 设计稿 §3.2 + §3.4） ===== */

  /** 综合按钮可用性（B 设计稿 §3.2 矩阵） */
  const actions = computed(() => {
    const s = currentStatus.value
    const edit = apsAuth.has('aps.rule.edit')
    const pub = apsAuth.has('aps.rule.publish')
    return {
      /** [+ 新建草稿]：任意状态可建（实际能否 fork 由 forkPermission 决定） */
      canCreateDraft: edit,
      /** [编辑 / 保存草稿]：仅 DRAFT */
      canEdit: s === 'DRAFT' && edit,
      /** [校验]：仅 DRAFT + 有发布权限 */
      canValidate: s === 'DRAFT' && pub,
      /** [发布]：仅 DRAFT + 有发布权限 */
      canPublish: s === 'DRAFT' && pub,
      /** [退役 / disable]：仅 PUBLISHED + 有发布权限 */
      canRetire: s === 'PUBLISHED' && pub
    }
  })

  /** 当前选中 RuleSet 是否允许 fork 新 DRAFT（B 设计稿 §3.4） */
  const forkPermission = computed(() => {
    const s = currentStatus.value
    if (!s) return { allowed: false, reason: '未选中 RuleSet' }
    return canCreateDraftForStatus(s)
  })

  /** 当前 RuleSet Buffer 是否脏（任一块 dirty） */
  const ruleSetDirty = computed(() => {
    const flags = ruleSetBuffer.value.dirty
    return BLOCK_KEYS.some((b) => flags[b])
  })

  /** 当前 ParameterSet Buffer 是否脏 */
  const parameterSetDirty = computed(() => {
    const flags = parameterSetBuffer.value.dirty
    return BLOCK_KEYS.some((b) => flags[b])
  })

  /** 任一 Buffer 脏（用于路由切换 + beforeunload 拦截） */
  const anyBufferDirty = computed(() => ruleSetDirty.value || parameterSetDirty.value)

  /** RuleSet 扁平化参数行（用于 UI 表格渲染） */
  const ruleSetFlattenedRows = computed<ParameterRow[]>(() => {
    const wb = ruleSetBuffer.value.workingBlocks
    if (!wb) return []
    return flattenBlocks(wb)
  })

  /** ParameterSet 扁平化参数行 */
  const parameterSetFlattenedRows = computed<ParameterRow[]>(() => {
    const wb = parameterSetBuffer.value.workingBlocks
    if (!wb) return []
    return flattenBlocks(wb)
  })

  /** 当前 Buffer 选择器（用于 onCellEdit 等参数） */
  function getBuffer(choice: BufferChoice): BufferState {
    return choice === 'ruleSetBuffer' ? ruleSetBuffer.value : parameterSetBuffer.value
  }

  // ===== actions =====
  async function loadList(): Promise<void> {
    listLoading.value = true
    error.value = null
    try {
      ruleSets.value = await ruleApi.listRuleSets()
    } catch (err) {
      handleError(err)
    } finally {
      listLoading.value = false
    }
  }

  async function loadDetail(ruleSetId: number): Promise<void> {
    loading.value = true
    error.value = null
    try {
      currentDetail.value = await ruleApi.getRuleSet(ruleSetId)
      // 详情加载完 → 回填列表行（B 设计稿 §3 落地后实测：主表缺 domainKey/version/status，
      // 在此把 listRuleSets() 占位字段校正为详情真实值，让卡片/状态分布即时刷新）
      const idx = ruleSets.value.findIndex((r) => r.ruleSetId === ruleSetId)
      if (idx >= 0 && currentDetail.value) {
        const sum = currentDetail.value.summary
        const existing = ruleSets.value[idx]
        ruleSets.value[idx] = {
          ...existing,
          domainKey: sum.domainKey ?? existing.domainKey,
          currentVersion: sum.currentVersion,
          draftVersion: sum.draftVersion,
          status: sum.status,
          publishedAt: sum.publishedAt
        }
      }
      // 选中后默认载入 base/target diff
      await loadDiff(
        ruleSetId,
        currentDetail.value.summary.currentVersion,
        currentDetail.value.summary.draftVersion ?? currentDetail.value.summary.currentVersion
      )
      // mock 模式：从 currentDetail.parameterSet.parameters 派生 buffer（B 设计稿 §1.2）
      // 真实模式：loadDualTrackBuffers 由 forkDraft 触发（或后续 Step D 校准后改为自动）
      if (import.meta.env.VITE_USE_MOCK === 'true' && currentDetail.value) {
        populateBufferFromDetail(currentDetail.value)
      }
    } catch (err) {
      handleError(err)
      currentDetail.value = null
    } finally {
      loading.value = false
    }
  }

  /** 从 currentDetail.parameterSet.parameters 派生 buffer（mock 模式专用）
   *  - 5 mock 参数全部映射到 procurement 块（其他块空对象）
   *  - heuristic 推断 type/sensitive/editable（§2.2）
   */
  function populateBufferFromDetail(detail: RuleSetDetailDto): void {
    const mockParameters = detail.parameterSet?.parameters ?? []
    // 构造 procurementJson 对象
    const procurementObj: Record<string, unknown> = {}
    for (const p of mockParameters) {
      procurementObj[p.parameterKey] = p.value
    }
    // 解析 5 块（mock 仅 procurement 有内容）
    const blocks: ParameterSetBlocks = {
      lock: {},
      supply: {},
      procurement: procurementObj,
      solverStrategy: {},
      candidateGuardrail: {}
    }
    // mock governance 派生
    const mockGovernance = {
      id:
        detail.summary.ruleSetId * 100 +
        (detail.summary.draftVersion ?? detail.summary.currentVersion),
      parameterSetId: detail.parameterSet?.parameterSetId ?? 0,
      versionCode: `v${detail.summary.draftVersion ?? detail.summary.currentVersion}`,
      status: 'DRAFT' as const,
      createdAt: detail.parameterSet?.createdAt,
      remarks: detail.parameterSet?.changeReason
    }
    const originalBlocks = JSON.parse(JSON.stringify(blocks))
    parameterSetBuffer.value = {
      governance: mockGovernance,
      originalBlocks,
      workingBlocks: blocks,
      dirty: emptyDirtyFlags(),
      loading: false,
      saving: false
    }
    ruleSetBuffer.value = {
      ...emptyBufferState(),
      governance: {
        id:
          detail.summary.ruleSetId * 100 +
          (detail.summary.draftVersion ?? detail.summary.currentVersion),
        ruleSetId: detail.summary.ruleSetId,
        parameterSetVersionId: detail.parameterSet?.parameterSetId ?? 0,
        versionCode: `v${detail.summary.draftVersion ?? detail.summary.currentVersion}`,
        status: 'DRAFT' as const
      }
    }
    currentParameterSetVersionId.value = mockGovernance.id
    currentRuleSetVersionId.value = mockGovernance.id
  }

  async function loadDiff(ruleSetId: number, base: number, target: number): Promise<void> {
    try {
      currentDiff.value = await ruleApi.diffVersions(ruleSetId, base, target)
    } catch (err) {
      handleError(err)
      currentDiff.value = null
    }
  }

  /** 校验草稿（U22：发布前必走）
   *  v1.4 §二十：后端走 versionId；store 内部将 ruleSetId + draftVersion 解析为 versionId
   */
  async function validateDraft(ruleSetId: number): Promise<boolean> {
    validating.value = true
    error.value = null
    try {
      const detail = currentDetail.value
      // 解析 versionId：优先 detail.history 中 status=DRAFT 且 ruleSetId 匹配的最后一个
      const versionId =
        (detail?.history ?? []).filter((h) => h.status === 'DRAFT').pop()?.versionId ??
        detail?.summary.draftVersion ??
        0
      const res = await ruleApi.validateDraft(versionId)
      if (currentDetail.value && currentDetail.value.summary.ruleSetId === ruleSetId) {
        currentDetail.value.summary.validationStatus = res.status
        currentDetail.value.summary.validationMessage = res.message
      }
      return res.status === 'PASSED'
    } catch (err) {
      handleError(err)
      return false
    } finally {
      validating.value = false
    }
  }

  /** 发布（需权限码 aps.rule.publish + P1-15 scope 断言）
   *  v1.4 §二十：后端走 versionId；store 内部解析
   */
  async function publish(ruleSetId: number, changeReason: string): Promise<void> {
    if (!canPublishAction.value) {
      error.value = '当前操作者无发布权限（需权限码 aps.rule.publish）'
      return
    }
    const detail = currentDetail.value
    if (!detail || detail.summary.ruleSetId !== ruleSetId || detail.summary.status !== 'DRAFT') {
      error.value = '当前 RuleSet 不是 DRAFT，无法发布'
      return
    }
    if (detail.summary.domainKey !== 'ALL') {
      try {
        assertScope(apsAuth.dataScope, { domainKey: detail.summary.domainKey })
      } catch (err) {
        if (err instanceof ScopeViolationError) {
          error.value = err.message
          return
        }
        throw err
      }
    }
    const draftVersion = detail.summary.draftVersion
    if (draftVersion === undefined) {
      error.value = '当前 DRAFT 没有可用的 draftVersion'
      return
    }
    const versionId =
      (detail.history ?? []).filter((h) => h.status === 'DRAFT').pop()?.versionId ?? draftVersion
    actionRunning.value = true
    error.value = null
    try {
      lastPublish.value = await ruleApi.publish({
        ruleSetId,
        draftVersion,
        changeReason,
        actor: actor.value,
        actorRoles: actorRoles.value,
        versionId
      } as any)
      await loadDetail(ruleSetId)
      await loadList()
    } catch (err) {
      handleError(err)
    } finally {
      actionRunning.value = false
    }
  }

  /** 退役 / 禁用（v1.4 后端语义改 disable；前端 UI 沿用"退役"文案）
   *  v1.4 §二十：后端走 versionId
   */
  async function retire(ruleSetId: number, reason: string): Promise<void> {
    if (!canPublishAction.value) {
      error.value = '当前操作者无退役权限（需权限码 aps.rule.publish）'
      return
    }
    const detail = currentDetail.value
    if (!detail || detail.summary.ruleSetId !== ruleSetId) {
      error.value = 'RuleSet 未选中'
      return
    }
    if (detail.summary.domainKey !== 'ALL') {
      try {
        assertScope(apsAuth.dataScope, { domainKey: detail.summary.domainKey })
      } catch (err) {
        if (err instanceof ScopeViolationError) {
          error.value = err.message
          return
        }
        throw err
      }
    }
    const versionId =
      (detail.history ?? []).filter((h) => h.status === 'PUBLISHED').pop()?.versionId ??
      detail.summary.currentVersion
    actionRunning.value = true
    error.value = null
    try {
      lastRetire.value = await ruleApi.retire({
        ruleSetId,
        version: detail.summary.currentVersion,
        reason,
        actor: actor.value,
        actorRoles: actorRoles.value,
        versionId
      } as any)
      await loadDetail(ruleSetId)
      await loadList()
    } catch (err) {
      handleError(err)
    } finally {
      actionRunning.value = false
    }
  }

  function setStatusFilter(filter: RuleStatus | 'ALL'): void {
    statusFilter.value = filter
  }

  function handleError(err: unknown): void {
    if (err instanceof ApiError) {
      error.value =
        `[${err.code}] ${err.message}` + (err.traceId ? ` (traceId=${err.traceId})` : '')
    } else {
      error.value = (err as Error)?.message ?? '未知错误'
    }
  }

  function reset(): void {
    currentDetail.value = null
    currentDiff.value = null
    lastPublish.value = null
    lastRetire.value = null
    error.value = null
    // 同时清空双轨 buffer（路由切换时使用）
    ruleSetBuffer.value = emptyBufferState()
    parameterSetBuffer.value = emptyBufferState()
    currentRuleSetVersionId.value = null
    currentParameterSetVersionId.value = null
  }

  /* ====================================================================== */
  /* v1.4 §二十 写维护：双轨 Buffer actions（B 设计稿 Step 2）                     */
  /* ====================================================================== */

  /**
   * 加载双轨 Buffer（入口 A：看 DRAFT 时调）
   *  - 调用场景：loadDetail 已载入 currentDetail → 调本方法拉 RS+PS 详情（5 JSON 字符串）
   *  - 真实模式：GET /api/governance/rule-set/version/{versionId} + /parameter-set/version/{versionId}
   *  - mock 模式：从 mockParametersFor 派生（仅 procurement 块有内容；其他块空对象）
   *
   * 注：本方法为 Step 2 骨架，UI 重设计时（Step 6）由 loadDetail 串联调用；
   *    联调 Step D 后用真实 GET 替换 mock 派生
   */
  async function loadDualTrackBuffers(
    ruleSetVersionId: number,
    parameterSetVersionId: number
  ): Promise<void> {
    ruleSetBuffer.value = { ...ruleSetBuffer.value, loading: true }
    parameterSetBuffer.value = { ...parameterSetBuffer.value, loading: true }
    error.value = null
    try {
      // 这里直接用 fetchVersion（按 versionId 拉 RS+PS）；如需联调可改 ruleApi 调用
      const [rsVer, psVer] = await Promise.all([
        fetchRuleSetVersion(ruleSetVersionId),
        fetchParameterSetVersion(parameterSetVersionId)
      ])
      currentRuleSetVersionId.value = ruleSetVersionId
      currentParameterSetVersionId.value = parameterSetVersionId

      const rsBlocks = parseParameterSetBlocks(rsVer)
      const psBlocks = parseParameterSetBlocks(psVer)
      const rsOriginal = JSON.parse(JSON.stringify(rsBlocks))
      const psOriginal = JSON.parse(JSON.stringify(psBlocks))

      ruleSetBuffer.value = {
        governance: rsVer,
        originalBlocks: rsOriginal,
        workingBlocks: rsBlocks,
        dirty: emptyDirtyFlags(),
        loading: false,
        saving: false
      }
      parameterSetBuffer.value = {
        governance: psVer,
        originalBlocks: psOriginal,
        workingBlocks: psBlocks,
        dirty: emptyDirtyFlags(),
        loading: false,
        saving: false
      }
    } catch (err) {
      handleError(err)
      ruleSetBuffer.value = { ...emptyBufferState(), loading: false }
      parameterSetBuffer.value = { ...emptyBufferState(), loading: false }
    }
  }

  /** 取 RuleSetVersion（mock 模式构造，真实模式调后端） */
  async function fetchRuleSetVersion(versionId: number): Promise<RuleSetVersion> {
    // 通过现有 getPublishedVersion（mock 支持）；如需 DRAFT 详情另开 GET 端点（联调 Step C）
    // 此处复用 mock 路径作为占位
    if ((ruleApi as any).getRuleSetVersion) {
      return await (ruleApi as any).getRuleSetVersion(versionId)
    }
    // 兜底：返回空 governance（避免 mock 模式崩溃）
    return {
      id: versionId,
      ruleSetId: 0,
      parameterSetVersionId: 0,
      versionCode: `v-${versionId}`,
      status: 'DRAFT'
    }
  }

  /** 取 ParameterSetVersion */
  async function fetchParameterSetVersion(versionId: number): Promise<ParameterSetVersion> {
    if ((ruleApi as any).getParameterSetVersion) {
      return await (ruleApi as any).getParameterSetVersion(versionId)
    }
    return {
      id: versionId,
      parameterSetId: 0,
      versionCode: `v-${versionId}`,
      status: 'DRAFT'
    }
  }

  /**
   * 入口 B：forkDraft（建草稿 — B 设计稿 §1.3 入口 B）
   *  - 真实模式：调 ruleApi.forkDraft（GET → POST 双发）
   *  - mock 模式：构造 RuleSetDraft + ParameterSetDraft 一对
   *  - 成功后自动载入双轨 Buffer
   *
   * @param sourceRuleSetVersionId 源 RuleSetVersionId（PUBLISHED 或 DISABLED）
   * @param newVersionCode 新版本号（如 'v13'）
   * @param changeReason 变更原因（写入 remarks）
   */
  async function forkDraft(
    sourceRuleSetVersionId: number,
    newVersionCode: string,
    changeReason?: string
  ): Promise<ForkDraftResult | null> {
    if (!canWrite.value) {
      error.value = '当前操作者无写权限（需权限码 aps.rule.edit）'
      return null
    }
    if (!forkPermission.value.allowed) {
      error.value = forkPermission.value.reason ?? '当前状态不允许 fork'
      return null
    }
    actionRunning.value = true
    error.value = null
    try {
      let result: ForkDraftResult
      if (import.meta.env.VITE_USE_MOCK === 'true') {
        // mock：直接构造双轨 DRAFT
        result = {
          ruleSetDraft: {
            id: sourceRuleSetVersionId * 10 + 1,
            ruleSetId: currentDetail.value?.summary.ruleSetId ?? 0,
            parameterSetVersionId: sourceRuleSetVersionId * 10 + 2,
            versionCode: newVersionCode,
            status: 'DRAFT',
            createdAt: new Date().toISOString(),
            remarks: changeReason
          },
          parameterSetDraft: {
            id: sourceRuleSetVersionId * 10 + 2,
            parameterSetId: (currentDetail.value?.summary.ruleSetId ?? 0) * 10,
            versionCode: newVersionCode,
            status: 'DRAFT',
            createdAt: new Date().toISOString(),
            remarks: changeReason
          }
        }
      } else {
        result = await ruleApi.forkDraft(sourceRuleSetVersionId, newVersionCode)
      }
      // 成功后自动载入双轨 Buffer
      if (result.ruleSetDraft.id && result.parameterSetDraft.id) {
        await loadDualTrackBuffers(result.ruleSetDraft.id, result.parameterSetDraft.id)
      }
      // 刷新列表
      await loadList()
      return result
    } catch (err) {
      handleError(err)
      return null
    } finally {
      actionRunning.value = false
    }
  }

  /**
   * onCellEdit — 包装 applyEdit + dirty.set（B 设计稿 §2.4）
   *  - 类型校验：纯前端 UI 控件已保证（ElInputNumber/ElSwitch）；不重复校验
   *  - 内部调 applyEdit（pure）+ 设置 dirty[row.block] = true
   */
  function onCellEdit(bufferChoice: BufferChoice, row: ParameterRow, newValue: JsonValue): void {
    const buf = getBuffer(bufferChoice)
    if (!buf.workingBlocks || !buf.governance) {
      error.value = `${bufferChoice} Buffer 未初始化`
      return
    }
    try {
      applyEdit(buf.workingBlocks, row, newValue)
      buf.dirty[row.block] = true
      // 触发响应式（直接赋值 ref 对象已自动触发；workingBlocks 是 ref 包裹的对象，Vue 3 Proxy 处理）
    } catch (err) {
      handleError(err)
    }
  }

  /**
   * onCancelDirty — 重置 working + 清 dirty
   */
  function onCancelDirty(bufferChoice: BufferChoice): void {
    const buf = getBuffer(bufferChoice)
    if (buf.originalBlocks) {
      buf.workingBlocks = JSON.parse(JSON.stringify(buf.originalBlocks))
    } else {
      buf.workingBlocks = emptyBlocks()
    }
    buf.dirty = emptyDirtyFlags()
  }

  /**
   * onSaveDraft — 包装 PUT + 清 dirty
   *  - full-object PUT（Q2 答复：5 JSON 必须全量回传）
   *  - 成功后刷新 originalBlocks + 清 dirty
   *  - 双轨守卫：先 PS 后 RS（如果 RS 也脏且未存则报错）
   *  - Issue 11 OCC 友好处理：后端 409 → "已被他人修改，请刷新"（不丢 dirty 状态）
   */
  async function onSaveDraft(bufferChoice: BufferChoice, changeReason: string): Promise<boolean> {
    const buf = getBuffer(bufferChoice)
    if (!buf.governance || !buf.workingBlocks) {
      error.value = `${bufferChoice} Buffer 未初始化`
      return false
    }
    if (!changeReason.trim()) {
      error.value = '请填写变更原因'
      return false
    }
    // 双轨守卫：保存 PS 前若 RS 脏且未存 → 报错
    if (bufferChoice === 'parameterSetBuffer' && ruleSetDirty.value) {
      error.value = '请先保存 RuleSet DRAFT（双轨顺序：先 RS 后 PS）'
      return false
    }
    const versionId = buf.governance.id
    if (!versionId) {
      error.value = 'versionId 缺失，无法保存草稿'
      return false
    }
    buf.saving = true
    error.value = null
    try {
      const putBody = buildPutBody(buf.governance as any, buf.workingBlocks)
      putBody.remarks = changeReason
      let result: RuleSetVersion | ParameterSetVersion
      if (bufferChoice === 'parameterSetBuffer') {
        result = await ruleApi.updateParameterSetDraft({
          versionId,
          body: putBody as unknown as ParameterSetVersion
        })
      } else {
        result = await ruleApi.updateRuleSetDraft({
          versionId,
          body: putBody as unknown as RuleSetVersion
        })
      }
      // 成功后刷新 governance + originalBlocks + 清 dirty
      buf.governance = result
      buf.originalBlocks = JSON.parse(JSON.stringify(buf.workingBlocks))
      buf.dirty = emptyDirtyFlags()
      return true
    } catch (err) {
      // Issue 11 OCC：后端 409 → 提示用户"已被他人修改"（不丢 dirty 状态供用户决定）
      if (err instanceof ApiError && err.code === 409) {
        error.value = `版本已被他人修改（OCC 冲突）：${err.message}。请刷新页面后再编辑。`
      } else {
        handleError(err)
      }
      return false
    } finally {
      buf.saving = false
    }
  }

  /**
   * 双轨 Publish（先 RS 后 PS；任一失败即停 + 回滚前端 dirty 状态）
   *  - 与现有 publish() 不同：双轨版同时发布 RS + PS
   *  - 调用方应确保两个 Buffer 都已保存（即 dirty=false）
   *  - Issue 11 OCC：catch 409 → 提示"已被他人发布/修改"，提示用户刷新
   */
  async function publishDualTrack(changeReason: string): Promise<boolean> {
    if (!canPublishAction.value) {
      error.value = '当前操作者无发布权限（需权限码 aps.rule.publish）'
      return false
    }
    if (ruleSetDirty.value || parameterSetDirty.value) {
      error.value = '存在未保存的草稿，请先保存再发布'
      return false
    }
    const rsVId = currentRuleSetVersionId.value
    const psVId = currentParameterSetVersionId.value
    if (!rsVId || !psVId) {
      error.value = '当前未选中 RuleSet/ParameterSet DRAFT'
      return false
    }
    actionRunning.value = true
    error.value = null
    try {
      const input: PublishGovernanceInput = {
        versionId: rsVId,
        changeReason,
        actor: actor.value
      }
      // 先 RS 后 PS
      try {
        await ruleApi.publishRuleSet(input)
      } catch (rsErr) {
        // Issue 11：RS 发布遇 409（被他人先发）→ 整体停
        if (rsErr instanceof ApiError && rsErr.code === 409) {
          error.value = `RuleSet 已被他人发布（OCC 冲突）：${rsErr.message}。请刷新页面。`
          await loadList()
          return false
        }
        throw rsErr
      }
      try {
        await ruleApi.publishParameterSet({ ...input, versionId: psVId })
      } catch (psErr) {
        // Issue 11：PS 发布遇 409 → 提示用户（RS 已发，PS 未发，需手动重试或取消 RS）
        if (psErr instanceof ApiError && psErr.code === 409) {
          error.value = `ParameterSet 已被他人发布（OCC 冲突）：${psErr.message}。注：RuleSet 已发布，请刷新页面后单独处理 PS。`
          await loadList()
          return false
        }
        throw psErr
      }
      // 刷新列表 + 重置 buffer
      await loadList()
      reset()
      return true
    } catch (err) {
      handleError(err)
      return false
    } finally {
      actionRunning.value = false
    }
  }

  return {
    // state
    ruleSets,
    currentDetail,
    currentDiff,
    lastPublish,
    lastRetire,
    statusFilter,
    loading,
    listLoading,
    actionRunning,
    validating,
    error,
    actor,
    actorRoles,
    // 双轨 Buffer state
    ruleSetBuffer,
    parameterSetBuffer,
    currentRuleSetVersionId,
    currentParameterSetVersionId,
    // getters
    filteredRuleSets,
    statusCounts,
    currentStatus,
    canPublish,
    canRetire,
    canWrite,
    canPublishAction,
    // 双轨 + 6 态 getters
    actions,
    forkPermission,
    ruleSetDirty,
    parameterSetDirty,
    anyBufferDirty,
    ruleSetFlattenedRows,
    parameterSetFlattenedRows,
    getBuffer,
    // actions
    loadList,
    loadDetail,
    loadDiff,
    validateDraft,
    publish,
    retire,
    setStatusFilter,
    reset,
    handleError,
    // 双轨 actions
    loadDualTrackBuffers,
    forkDraft,
    onCellEdit,
    onCancelDirty,
    onSaveDraft,
    publishDualTrack
  }
})
