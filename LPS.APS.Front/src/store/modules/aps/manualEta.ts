/**
 * APS V1 4号位 — 人工到货时间（Manual ETA）Pinia store（页面 8）
 *
 * @see 4号位文档第 11 节 / 审核报告 P1-7
 *
 * 状态：
 *  - list：ManualEtaDto[]
 *  - currentDetail：当前 Drawer 编辑对象（新增时为 null）
 *  - loading / saving / error：UI 态
 *  - lastFilter：最近一次查询的筛选条件
 *
 * 4号位不计算 Effective ETA / AvailableTime
 *
 * ❌ 2026-09-15 撤销 DepartmentCode 维度（@see 4号位-2026-09-13-裁定回退清单.md）：
 *  - 9月13日 `未命名的Markdown文件.md` 实际是 0号位 出的业务裁决（程序有效）
 *  - DepartmentCode 整条撤销：store getter distinctDepartmentCodes 删除
 *  - 当前状态：与 9月13日 撤销状态一致
 */

import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import {
  ApiError,
  manualEtaApi,
  type ManualEtaDto,
  type ManualEtaListFilter,
  type ManualEtaUpsertInput,
  type ManualEtaCancelInput
} from '@/api/aps-v1'

export const useManualEtaStore = defineStore('aps.manualEta', () => {
  // ===== state =====
  const list = ref<ManualEtaDto[]>([])
  const currentDetail = ref<ManualEtaDto | null>(null)
  const loading = ref(false)
  const saving = ref(false)
  const error = ref<string | null>(null)
  /** 最近一次查询的筛选条件（分页/筛选 UI 还原用） */
  const lastFilter = ref<ManualEtaListFilter>({})

  // ===== getters =====
  /** Active 行数 */
  const activeCount = computed(() => list.value.filter((x) => x.isActive).length)
  /** 已取消行数 */
  const cancelledCount = computed(() => list.value.filter((x) => !x.isActive).length)
  /** PO 号去重集合（供筛选下拉用） */
  const distinctPoNos = computed(() => Array.from(new Set(list.value.map((x) => x.poNo))).sort())
  /** 物料编码去重集合（供筛选下拉用） */
  const distinctMaterialCodes = computed(() =>
    Array.from(new Set(list.value.map((x) => x.materialCode))).sort()
  )

  // ===== actions =====
  async function load(filter?: ManualEtaListFilter): Promise<void> {
    loading.value = true
    error.value = null
    try {
      const next = filter ?? lastFilter.value
      list.value = await manualEtaApi.list(next)
      lastFilter.value = next
    } catch (err) {
      handleError(err)
    } finally {
      loading.value = false
    }
  }

  /**
   * Upsert：新增或更新
   *  - 成功后将 result 回写到 list 中相应位置（若已存在）或追加（新增）
   *  - currentDetail 同步刷新
   */
  async function upsert(input: ManualEtaUpsertInput): Promise<ManualEtaDto> {
    saving.value = true
    error.value = null
    try {
      const result = await manualEtaApi.upsert(input)
      const idx = list.value.findIndex((x) => x.poNo === result.poNo && x.lineNo === result.lineNo)
      if (idx >= 0) {
        list.value[idx] = result
      } else {
        list.value.unshift(result)
      }
      currentDetail.value = result
      return result
    } catch (err) {
      handleError(err)
      throw err
    } finally {
      saving.value = false
    }
  }

  /**
   * 取消（isActive=false）
   *  - 软删除，保留记录
   */
  async function cancel(input: ManualEtaCancelInput): Promise<ManualEtaDto> {
    saving.value = true
    error.value = null
    try {
      const result = await manualEtaApi.cancel(input)
      const idx = list.value.findIndex((x) => x.poNo === result.poNo && x.lineNo === result.lineNo)
      if (idx >= 0) {
        list.value[idx] = result
      }
      if (
        currentDetail.value?.poNo === result.poNo &&
        currentDetail.value?.lineNo === result.lineNo
      ) {
        currentDetail.value = result
      }
      return result
    } catch (err) {
      handleError(err)
      throw err
    } finally {
      saving.value = false
    }
  }

  function openDetail(dto: ManualEtaDto | null): void {
    currentDetail.value = dto
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
    list.value = []
    currentDetail.value = null
    error.value = null
    lastFilter.value = {}
  }

  return {
    list,
    currentDetail,
    loading,
    saving,
    error,
    lastFilter,
    activeCount,
    cancelledCount,
    distinctPoNos,
    distinctMaterialCodes,
    load,
    upsert,
    cancel,
    openDetail,
    reset
  }
})
