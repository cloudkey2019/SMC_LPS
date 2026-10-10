/**
 * APS V1 4号位 — Domain 定义 Pinia store（v1.2 Domain专项 Pkg-2）
 *
 * 状态：
 *  - definitions  DomainDefinitionDto[]（来自 3号位治理接口 / mock 字典）
 *  - loadedAt    最近一次刷新时间
 *  - loading/error
 *
 * 用途：
 *  - 顶部 Domain 切换器 / 过滤器下拉选项
 *  - Gantt / CTP / Candidate / Run / PI / Rules 页面 domainName 显示
 *  - Domain 维护页（v1.2 Pkg-3）CRUD 入口
 *  - 与 Scope（dataScope.domainKeys）联动：仅展示 active + 在白名单内的 Domain
 *
 * 设计：
 *  - 4号位不维护 DomainDefinition；只读为主，CRUD 仅给 Domain 维护页
 *  - 应用启动时 lazy 加载（首次访问任何 Domain 相关页面时拉取）
 *  - 切换角色后不需要重新拉（scope 过滤在 getter 里）
 */

import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import {
  domainApi,
  EndpointUnavailableError,
  type DomainDefinitionDto,
  type DomainListQuery,
  type DomainKey,
  APS_USE_MOCK
} from '@/api/aps-v1'
import { useApsAuthStore } from './auth'

/** Domain 字典加载状态（驱动 UI banner + 调试可见性）
 *  - 'ok'           加载成功（含 mock 模式）
 *  - 'unavailable'  后端 404（DomainController 未实现）→ 红色 banner
 *  - 'error'        其它错误（网络/5xx）→ 黄色 banner
 */
export type DomainFetchStatus = 'ok' | 'unavailable' | 'error'

export const useDomainStore = defineStore('aps.domain', () => {
  // ===== state =====
  const definitions = ref<DomainDefinitionDto[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)
  const loadedAt = ref<string | null>(null)
  const fetchStatus = ref<DomainFetchStatus>('ok')

  // ===== getters =====
  /**
   * 当前业务范围（scope.domainKeys）内且启用的 Domain 列表
   *  - 默认按 sortOrder 升序
   *  - 空 scope.domainKeys = 未限制，返回全部 active
   */
  const activeDomains = computed<DomainDefinitionDto[]>(() => {
    const apsAuth = useApsAuthStore()
    const allowed = apsAuth.dataScope.domainKeys
    return definitions.value
      .filter((d) => d.isActive)
      .filter((d) => allowed.length === 0 || allowed.includes(d.domainKey))
      .slice()
      .sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0))
  })

  /** domainKey → DomainDefinitionDto 查找 */
  const byKey = computed(() => {
    const map = new Map<DomainKey, DomainDefinitionDto>()
    for (const d of definitions.value) {
      map.set(d.domainKey, d)
    }
    return map
  })

  /** 仅 active + 不分 scope（维护页 / 测试用） */
  const allActive = computed<DomainDefinitionDto[]>(() =>
    definitions.value
      .filter((d) => d.isActive)
      .slice()
      .sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0))
  )

  /** 给 UI 显示用的 label：domainKey → domainName（找不到时回退 key） */
  function label(domainKey: DomainKey | undefined | null): string {
    if (!domainKey) return '—'
    return byKey.value.get(domainKey)?.domainName ?? domainKey
  }

  /** domainKey → 数据库 Id（后端 detail/update/enable/disable 都按 id 寻址）
   *  - view 层始终用 domainKey（业务唯一标识），由 store 内部映射到 id
   *  - 找不到时返回 undefined（调用方应 abort 操作，避免打到错误端点）
   */
  function getId(domainKey: DomainKey): number | undefined {
    return byKey.value.get(domainKey)?.id
  }

  // ===== actions =====
  /**
   * 加载 Domain 字典（应用启动后任意页面调用一次即可）
   *  - 默认 activeOnly=true
   */
  async function load(query: DomainListQuery = {}): Promise<void> {
    if (loading.value) return
    loading.value = true
    error.value = null
    try {
      definitions.value = await domainApi.list(query)
      loadedAt.value = new Date().toISOString()
      fetchStatus.value = 'ok'
    } catch (err) {
      if (err instanceof EndpointUnavailableError) {
        // 接口未实现 → banner 提示用户，不静默回退 mock（避免误导）
        fetchStatus.value = 'unavailable'
        error.value = err.message
      } else {
        fetchStatus.value = 'error'
        error.value = (err as Error)?.message ?? '加载 Domain 字典失败'
      }
    } finally {
      loading.value = false
    }
  }

  /** 强制刷新（v1.2 Domain 维护页保存后调用） */
  async function refresh(): Promise<void> {
    return load({ activeOnly: false })
  }

  /** 单个 Domain 详情（按 domainKey 查；内部转 id 调 api.detail）
   *  - 若 domainKey 未在已加载 definitions 中（含刚创建尚未刷新），抛错让调用方先 refresh
   */
  async function fetchDetail(domainKey: DomainKey): Promise<DomainDefinitionDto | null> {
    const id = getId(domainKey)
    if (id === undefined) {
      throw new Error(`Domain 不在已加载字典中：${domainKey}（请先调用 load/refresh）`)
    }
    return domainApi.detail(id)
  }

  /** 维护页：创建 / 更新 / 启停用 */
  async function create(
    input: Omit<DomainDefinitionDto, 'id' | 'updatedBy' | 'updatedAt'>
  ): Promise<void> {
    await domainApi.create(input)
    await refresh()
  }

  async function update(
    domainKey: DomainKey,
    patch: Partial<Omit<DomainDefinitionDto, 'id' | 'domainKey' | 'updatedBy' | 'updatedAt'>>
  ): Promise<void> {
    const id = getId(domainKey)
    if (id === undefined) {
      throw new Error(`Domain 不在已加载字典中：${domainKey}（请先调用 load/refresh）`)
    }
    await domainApi.update(id, patch)
    await refresh()
  }

  async function enable(domainKey: DomainKey): Promise<void> {
    const id = getId(domainKey)
    if (id === undefined) {
      throw new Error(`Domain 不在已加载字典中：${domainKey}（请先调用 load/refresh）`)
    }
    await domainApi.enable(id)
    await refresh()
  }

  async function disable(domainKey: DomainKey): Promise<void> {
    const id = getId(domainKey)
    if (id === undefined) {
      throw new Error(`Domain 不在已加载字典中：${domainKey}（请先调用 load/refresh）`)
    }
    await domainApi.disable(id)
    await refresh()
  }

  /** 应用启动后立即预加载（mock 同步，生产异步）
   *  - 调用方：main.ts 或 layout onMounted
   *  - 失败不阻塞首屏（domain 选择器可降级为空）
   */
  function bootstrap(): void {
    if (definitions.value.length > 0) return
    if (APS_USE_MOCK) {
      // mock 同步预填，避免首屏空白
      load().catch(() => {
        /* error 已在 load 内记录 */
      })
    } else {
      load().catch(() => {
        /* 同上 */
      })
    }
  }

  return {
    // state
    definitions,
    loading,
    error,
    loadedAt,
    fetchStatus,
    // getters
    activeDomains,
    allActive,
    byKey,
    label,
    getId,
    // actions
    load,
    refresh,
    fetchDetail,
    create,
    update,
    enable,
    disable,
    bootstrap
  }
})
