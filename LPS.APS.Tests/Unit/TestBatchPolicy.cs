using System.Collections.Generic;
using System.Linq;
using LPS.APS.Core.Dto;

namespace LPS.APS.Tests.Unit;

/// <summary>
/// 单元测试用**宽松** Batch Policy 构造器。
///
/// 背景（0号位 2026-10-08《未命名的Markdown文件 (1)(1).md》§四 P0-01）：
///   C 桶需求在 Solver 输入中**找不到有效 Batch Policy** 时必须 `BATCH_POLICY_MISSING` **Fail Closed**，
///   不得再被静默降级成「不拆、恒 1 批」的隐藏业务默认。
///   ⇒ 既有夹具（只为验证 Direction/Routing/Setup/压实/连续性等**非批决策**能力而写）
///     必须**显式**给出一条有效策略，否则一律 fail-closed。
///
/// 本构造器给出的策略刻意**不改变任何既有行为**：
///   · `MinExecutionBatchQty = MaxExecutionBatchQty = null` ⇒ 无硬批域约束（Min=0 / Max=∞）。
///   · `AllowSplit = false` ⇒ 唯一候选 = 单批且恒合法 ⇒ **恒 1 批**，与 P0-01 之前的行为逐字一致。
///   · `AllowMerge` 由调用方按夹具原有 `Parameters.AllowMerge` 传入 ⇒ Merge 类断言不被本改动误杀
///     （策略是 Merge 的**正式控制源**，见 §八 P1-01）。
///
/// ⚠ 这不是「隐藏默认」：策略是夹具**显式提供**的输入，正是 P0-01 要求的「上游必须给出有效 Policy」。
///   验证 Fail Closed 本身的用例**不得**使用本构造器（见 `ExecutionBatchDraftTests` 缺策略用例）。
///
/// ── 2026-10-10 AUD-1-004 修复后的**夹具口径变更**（必须与真实装载一致）──
///   裁词（0号位《APS_V1_2_20261010.md》§3 AUD-1-004）：冻结 B-001/B-006 —— 生效规则按
///     `MaterialId + **明确** ProductionDepartmentId` **精确匹配**；历史 `NULL` 部门只作**兼容**，
///     **不得**在 Solver 里重新变成「Material 级默认」。
///   真实装载侧同口径：3号位 `TaskSplitRuleConfigProjector.Project` 已按 v5.1.10 收口④
///     **排除** `ProductionDepartmentId is null` 的行 ⇒ 快照里**不存在** NULL 部门规则。
///   ⇒ 夹具过去靠 NULL 行「覆盖任意部门」的写法**在真实数据下不成立**（那正是被判 P0 的兜底），
///     故本构造器改为**显式部门**：调用方必须传本夹具真实使用的部门号，
///     且被排需求必须带 `StartStageCode`（真实由 2号位 `FillStartStageCodes` 回填），
///     否则 `ResolveExecutionBatchPolicy` 解析不出部门 ⇒ 如实 `BATCH_POLICY_MISSING`。
/// </summary>
internal static class TestBatchPolicy
{
    /// <summary>
    /// 为给定物料集构造宽松策略（每物料一条，`ProductionDepartmentId = 显式部门`）。
    /// `productionDepartmentId` **无默认值**（编译期强制每个夹具说清自己的部门），
    /// 避免再出现「靠 NULL 行覆盖任意部门」的隐性兜底。
    /// </summary>
    public static BatchPolicyRuleSnapshot[] Permissive(
        IEnumerable<int> materialIds,
        int productionDepartmentId,
        bool allowMerge = false,
        bool allowSplit = false)
        => materialIds
            .Distinct()
            .Select(materialId => new BatchPolicyRuleSnapshot
            {
                MaterialId = materialId,
                ProductionDepartmentId = productionDepartmentId,
                MinExecutionBatchQty = null,
                MaxExecutionBatchQty = null,
                AllowSplit = allowSplit,
                AllowMerge = allowMerge
            })
            .ToArray();

    /// <summary>
    /// 单物料便捷重载。
    /// </summary>
    public static BatchPolicyRuleSnapshot[] Permissive(
        int materialId,
        int productionDepartmentId,
        bool allowMerge = false,
        bool allowSplit = false)
        => Permissive(new[] { materialId }, productionDepartmentId, allowMerge, allowSplit);
}
