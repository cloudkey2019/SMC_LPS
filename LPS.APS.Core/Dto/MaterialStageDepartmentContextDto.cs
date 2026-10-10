namespace LPS.APS.Core.Dto;

/// <summary>
/// 物料×阶段→默认生产部门 上下文（PM 裁定：最小 B）。
/// 2号位裁剪当前 Domain 涉及的 (MaterialId, StageCode) 传入；1号位按 (MaterialId, StageCode)
/// 锁 ProductionDepartmentId 后过滤 Routing 三件套，不得重新推导部门。
///
/// 本 DTO 同时承担「该物料本次需经过的 Stage 列表」的载体作用（1号位 由此知悉 Stage 集合）。
/// </summary>
public sealed class MaterialStageDepartmentContextDto
{
    public int MaterialId { get; init; }

    /// <summary>必须存在于 StageDict 的合法大工艺阶段码</summary>
    public string StageCode { get; init; } = string.Empty;

    /// <summary>该物料在该阶段下的默认生产部门 Id</summary>
    public int ProductionDepartmentId { get; init; }
}

/// <summary>
/// 某物料在**本次 BOM 上下文**中的**完整有序 Stage 链**（PM《BOM取用_Pegging_Stage_Routing完整链路说明》§七，2026-09-28：
/// 「Stage 顺序只能以 `StageDetail.StageSeq` / `APS_BOM_STAGE_PATH_RAW.StageSeq` 为权威，
///  禁止从 RoutingOperation 按 Stage 名/SortHint/字母序猜工艺先后」）。
///
/// 【为什么需要】1号位 消费「供给阈值 Stage（`LogicalProductionDemand.RequiredStageCode`）」时，
///   必须判断「做到该阶段为止」= 保留该阶段**及之前**的全部 Stage、丢弃之后的 —— 这需要 Stage 先后顺序。
///   同样，「无 Routing 阶段保留前后依赖」（`StageLeadTimes`）也需要顺序。
///   本链即为此提供**原始有序事实**，不预判消费侧的建图形态（StageDependency / 有序列表均可）。
///
/// 【形态（1号位 2026-09-28 回执 §二 撤回上报的硬前提）】
///   ① **每个物料一条完整有序链**（不是「一个集合 + 另一张映射表」）；
///   ② **每步带 `StageSeq` 数值**（不是只有数组下标隐含先后）。
///   `Stages` 已按 `StageSeq` 升序排列，`StageSeq` 亦随步携带，两条前提同时满足。
///
/// 【粒度】同一物料在本次 BOM 里 ROOT ∪ EDGE 的 Stage 取并集；同一 StageCode 取**最小 StageSeq**
///   （与 PE 侧 `MIN(StageSeq)` 口径一致）。
/// 【谁产出/谁消费】2号位 从本次 BOM 上下文装载 → 1号位 消费。
/// </summary>
public sealed class StageSequenceChain
{
    public int MaterialId { get; init; }

    /// <summary>已按 `StageSeq` 升序排列的完整 Stage 链</summary>
    public IReadOnlyList<StageSequenceStep> Stages { get; init; } = Array.Empty<StageSequenceStep>();
}

/// <summary>Stage 链上的一步：阶段码 + 顺序号 + 该阶段的生产部门</summary>
public sealed class StageSequenceStep
{
    /// <summary>大工艺阶段码（如 CN_MACH / CN_SURF）</summary>
    public string StageCode { get; init; } = string.Empty;

    /// <summary>顺序号（越小越前）。权威来源 = `APS_BOM_STAGE_PATH_RAW.StageSeq`。</summary>
    public int StageSeq { get; init; }

    /// <summary>
    /// 该阶段对应的生产部门 Id。
    /// 【来源】`MaterialStageDeptContext`（PM《Stage、生产部门、Routing、Dependency、StageLeadTimeParam 接口裁决回复》2026-09-28 §三/§四/§六：
    ///   「StageCode 不能直接决定生产部门，必须通过 MaterialStageDeptContext 确定」「Stage 不是部门，Stage 必须经过 Master 裁决」）。
    /// 【为什么必须随链带出】PM §三 明确 2号位 提供的业务事实是
    ///   `MaterialId + StageCode + StageSeq + ProductionDepartmentId` 四条一组（即 PM 命名的 `EffectiveStagePath`），
    ///   不是只有码与序号。
    /// 【null 语义】无 MSC 映射 ⇒ 本阶段部门未知（不得凭空推导）。1号位 按既有最小B 口径记
    ///   `MISSING_PRODUCTION_DEPARTMENT_CONTEXT` 并置 Unscheduled，不得由 1号位 自行猜部门。
    /// </summary>
    public int? ProductionDepartmentId { get; init; }
}

/// <summary>
/// 无 Routing 阶段的提前期（PM《无Routing Stage统一处理建议》§九/§十，2026-09-28）。
///
/// 【谁产出/谁消费】2号位 装载 → 1号位 消费。
/// 【为什么需要】StagePath 决定阶段是否存在及顺序；Routing 只回答「该 Stage 内有无小工序」。
///   `Routing 不存在 ≠ Stage 不存在` —— 如 BJ_FINAL（完工/出口）、TJ_OUTS（外协）天然无工序，
///   1号位 需为该阶段保留时间与前后依赖，其时长即取自本事实。
/// 【2号位 解析口径】**三级**，从细到粗降级（2026-09-29 用户明确口径，**逐字**）：
///   ① `DEPT_EXACT`            = ProductionDeptCode + FactoryCode + StageCode
///   ② `FACTORY_STAGE_DEFAULT` = FactoryCode + StageCode
///   ③ `GLOBAL_STAGE_DEFAULT`  = **StageCode**（**不带 `IsDefault` 条件** —— 只要该 Stage 有参数行即命中）
///   （旧口径的 MaterialCode 级 / ProductFamilyCode 级 §4.1 定「在V1不作为正常有效匹配级别」，**已移除**。）
///
/// 【走完三级仍未命中】PM 2026-09-29 裁决：「按照三级查找，如果三级均未命中，则记入 STAGE_LEADTIME_MISSING，
///   **并按 3 天兜底**」。⇒ 2号位 **产出一条 `LeadTimeHours = 72` 的 Fact**（不是省略、更不是 0 小时），
///   `MatchLevel` = **`STAGE_LEADTIME_MISSING`**（PM 原码；1号位 回执 §四 把它定为 V1 合法 4 值之一），
///   同时落日志计数。⇒ V1 `MatchLevel` 合法值域 = 下面 `MatchLevel` 属性注释里的 4 个，**多一个都算契约违例**。
///
/// 【兜底只看「算不算命中」，不按成因分治】——**只有一种情形算未命中：三级全不中**（甲/乙/丙 都走同一支）。
///   ⚠️ **2026-09-29 用户更正**：本类此前写有第二种「未命中」情形
///   「**命中到参数行，但折算值 ≤ 0**（实测 4 行 `*_FINAL` 的 `LeadTimeDays = 0.00`）」
///   —— **该口径已作废**。**命中就是命中**：折算值为 0（或负）**按命中产出**
///   （`LeadTimeHours = 0`、`MatchLevel` = 实际命中级，实测那 4 行为 `FACTORY_STAGE_DEFAULT`）。
///   0 是参数行写下的值，装载层不得替参数侧改判；仅单记 `zeroHitCount` 供参数 Owner 查看。
/// </summary>
public sealed class StageLeadTimeFact
{
    public int MaterialId { get; init; }

    /// <summary>大工艺阶段码（如 BJ_FINAL / TJ_OUTS）</summary>
    public string StageCode { get; init; } = string.Empty;

    /// <summary>
    /// 提前期（小时）。换算口径：LeadTimeHours &gt; 0 时取之，否则按 LeadTimeDays × 24 折算。
    /// </summary>
    public decimal LeadTimeHours { get; init; }

    /// <summary>
    /// 命中层级（审计用）。**V1 合法值域 = 恰好 4 个**，多一个都算契约违例：
    /// `DEPT_EXACT` / `FACTORY_STAGE_DEFAULT` / `GLOBAL_STAGE_DEFAULT` / `STAGE_LEADTIME_MISSING`。
    /// 便于回溯「该提前期由哪一级参数命中」，避免同值不同源无法区分。
    ///
    /// ⚠️ 值域是**与 1号位 对齐过的契约**（1号位 回执《无RoutingStage时间出口字段_字段载体落码提请_v1.0》
    ///    §四 明文：「`MatchLevel` 为上述 4 个之外的任何值（含旧名 `MATERIAL` / `FAMILY`）⇒ **契约违例**，
    ///    不用该值，按第 3 条兜底」）。⇒ 装载层**不得**再自起名字，改值域须先与 1号位 对齐。
    ///
    /// ⚠️ 0号位 2026-09-29 §4.3 收敛了前三个值；旧的 `MATERIAL_EXACT` / `PRODUCT_FAMILY_EXACT`
    ///    （本字段历史值 `MATERIAL` / `FAMILY`）**V1 不作为正常命中**，装载层已不再产出——
    ///    遇旧数据由装载层登记兼容告警，不让 1号位 自行解释。
    ///
    /// ⚠️ 第四值 `STAGE_LEADTIME_MISSING`（PM 2026-09-29 裁决「三级均未命中 ⇒ 记入 … 并按 3 天兜底」）。
    ///    **注意它带值**（`LeadTimeHours = 72`）——名字里的 MISSING 指「**参数未命中**」，不是「本条无提前期」。
    ///    它不复用 `GLOBAL_STAGE_DEFAULT`：复用会让审计把兜底误认成「真有全局默认参数」。
    ///    **覆盖面（2026-09-29 用户更正后）**：本值**只覆盖「三级全不中」这一种情形**。
    ///    此前写的第二种情形「命中但折算值 ≤ 0」（实测 4 行 `*_FINAL` 的 `LeadTimeDays = 0.00`）
    ///    **已不归本值** —— 命中就是命中，那 4 行按命中级（`FACTORY_STAGE_DEFAULT`）+ 值 0 产出。
    /// </summary>
    public string MatchLevel { get; init; } = string.Empty;
}