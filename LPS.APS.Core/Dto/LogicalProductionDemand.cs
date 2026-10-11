namespace LPS.APS.Core.Dto;

/// <summary>
/// 逻辑生产需求（V1.2）
/// Pegging阶段形成，交给1号位Solver决定FinalTask
/// 不持久化到数据库，仅运行时内存DTO
/// </summary>
public sealed class LogicalProductionDemand
{
    /// <summary>
    /// 逻辑需求唯一键
    /// </summary>
    public string LogicalDemandKey { get; init; } = string.Empty;

    /// <summary>
    /// 计划版本ID
    /// </summary>
    public long PlanVersionId { get; init; }

    /// <summary>
    /// Domain键
    /// </summary>
    public string DomainKey { get; init; } = string.Empty;

    /// <summary>
    /// 与Pegging Allocation建立追溯
    /// </summary>
    public long AllocationSequence { get; init; }

    /// <summary>
    /// 需求键
    /// </summary>
    public string DemandKey { get; init; } = string.Empty;

    /// <summary>
    /// 订单ID（可选）
    /// </summary>
    public long? OrderId { get; init; }

    /// <summary>
    /// 物料ID
    /// </summary>
    public int MaterialId { get; init; }

    /// <summary>
    /// 工厂ID
    /// </summary>
    public int FactoryId { get; init; }

    /// <summary>
    /// 从哪里开始继续生产（大工艺阶段码，机加工/氧化级，对应 RoutingOperation.StageCode）。
    /// 2号位在 PeggingLoop 后据 Routing 有向图「无入边源结点」回填（原 init 改为 set 以支持回填，见 PeggingOrchestrator.FillStartStageCodes）。
    /// </summary>
    public string StartStageCode { get; set; } = string.Empty;

    /// <summary>
    /// 要做到哪个大工艺阶段即算可供给父件（供给阈值 Stage）——对应 BOM 边的 `ChildRequiredStageCode`
    /// / `APS_BOM_STAGE_PATH_RAW.IsSupplyThreshold = 1` 的那一段。
    ///
    /// 【语义（PM《BOM取用_Pegging_Stage_Routing完整链路说明》§八，2026-09-28）】
    ///   「子件做到这个 Stage 以后，才成为当前父件可使用的 Supply。」
    ///   例：B 的 Stage 链为 CN_MACH → CN_SURF(IsSupplyThreshold=1)，则 B 完成 CN_MACH 尚不可供给 A，
    ///   须完成 CN_SURF。⇒ 本需求**不必走完该物料全部 Stage，做到本阶段即为终点**。
    ///
    /// null / 空 = 无阈值信息（源数据缺 `ChildRequiredStageCode`）⇒ 按既有保守口径「全工艺完成才可供给」
    /// （与冻结 DDL 的设计决策一致：`ChildRequiredStageCode=NULL 时按保守策略：子件必须全工艺完成后才可供给父件`）。
    /// </summary>
    public string? RequiredStageCode { get; set; }

    /// <summary>
    /// 续排起点工序码（工序级，比 StartStageCode 更细，对应 5号位 的 NextOperation/StartOperation）
    /// 2号位 Pegging 不扩展 Operation，此字段由 5号位 按执行进度交付；null = 尚无工序级起点（新单从第一道工序起 / 未接 5号位交付）。
    /// </summary>
    public string? StartOperationCode { get; set; }

    /// <summary>
    /// 净产出数量
    /// </summary>
    public decimal NetOutputQty { get; init; }

    /// <summary>
    /// 计划加工数量
    /// </summary>
    public decimal PlannedProcessQty { get; init; }

    /// <summary>
    /// 数量单位（P1-08 方案a）：2号位 装载时透传；1号位 FinalTaskDraft.UOM 据此原样回填，
    /// 2号位落盘不再反查订单补 UOM。null = 无单位来源（物料缺行 / Material.UOM 为空）。
    ///
    /// 【2026-10-09 修正取值源】原注释写「来自需求侧订单 <c>Order.UOM</c>」，但此处的 <c>order</c> 是**根订单**
    /// （成品），经 <c>TraverseBomNode</c> 逐层原样下传 ⇒ 与 PI 身份同源的串味：子件需求会拿到**成品的单位**。
    /// 实测 PV2：<c>[Order].UOM</c> 22,144 行**恒为 'PS'**；而 <c>Material.UOM</c> 1,022,985 行**零空值**
    /// 且按物料各不相同（EA/…）。⇒ 取值改为**该需求自身物料**的 <c>Material.UOM</c>，由
    /// <c>PeggingOrchestrator.FillDemandUomAsync</c> 在 Pegging 后按需求物料集统一回填（与
    /// <see cref="StartStageCode"/> / <see cref="RequiredStageCode"/> 同款「Pegging 后解析」模式）。
    /// 1号位 <c>PhaseFiveCompression.cs</c> 的注释「如需子件 UOM 请 2号位 指明」即此口径。
    /// </summary>
    public string? UOM { get; set; }

    /// <summary>
    /// 下游要求的可用时间
    /// </summary>
    public DateTime RequiredAvailableTime { get; init; }

    /// <summary>
    /// 已按冻结规则排好的业务顺序
    /// 不是全局PriorityScore，而是计算层→Priority Segment→段内排序的结果
    /// </summary>
    public int DemandSequence { get; init; }

    /// <summary>
    /// 生产指示号（PI类需求使用）
    /// </summary>
    public string? ProductionInstructionNo { get; init; }

    /// <summary>
    /// 是否未定位（PI Position为UNLOCATED）
    /// </summary>
    public bool IsUnlocated { get; init; }

    /// <summary>
    /// 软偏好资源（P1-11）：上一 ACTIVE Task 的 ResourceId。非硬锁——1号位在合法资源集内优先尝试，
    /// 不满足则回落其它合法资源；硬锁走 ExecutionConstraint。null = 无上一 ACTIVE 资源偏好（自由/新单）。
    /// </summary>
    public int? PreferredResourceId { get; init; }

    /// <summary>
    /// 备选软偏好资源（P1-11）：PreferredResourceId 不可用时的次优偏好。null = 无。
    /// </summary>
    public int? FallbackResourceId { get; init; }

    /// <summary>
    /// 是否连续份额（跨版本连续性的输入标记）。
    ///
    /// 【A/B/C 模型（《APS_V1_1号位有限产能排程开发实施包 v1.6》§本轮统一执行红线 Q2/Q3，0号位 2026-10-07 裁决 §四）】
    ///   · **Continuation Slice（A桶 / B桶）** = true —— 已有执行连续份额：带 ContinuationKey、
    ///     固定真实 RouteCode/PathId、StartOperation，按 NoSplitMerge 处理，**不得切换 Routing**。
    ///     A 与 B 对 Solver 语义**完全一致**（差别仅在 FinalTask 后 TaskNo 归属，属 2号位 身份处理，
    ///     1号位 不需要区分）。
    ///   · **Free Slice（C桶）** = false/缺省 —— 自由需求：Routing 由 1号位 在候选内联合择优选择。
    ///
    /// 1号位 消费语义：
    ///   · true  → 走「不拆合(P0-07) + 连续先行/单活跃资源(P0-08)」语义，且**固定路径**（不选路）；
    ///   · false → 若物料有 &gt;1 条候选 Path，走 C桶候选内择优（见 Phase2 需求级选路）。
    ///
    /// ⚠ **禁止用「候选 Path 条数」反推 A/B/C 身份**（0号位 2026-10-07 裁决 §四）：
    ///   A/B 所在物料本来就可能有多条合法 Routing，C 也可能当前只有 1 条合法 Path。
    ///   Path 数量 ≠ 桶身份，桶身份只能由本字段承载。
    /// </summary>
    public bool IsContinuation { get; init; }

    /// <summary>
    /// 连续份额身份键（《APS_V1_1号位有限产能排程开发实施包 v1.6》Q3，0号位 2026-10-07 裁决 §六 授权补载体）。
    ///
    /// 【口径（Q3 逐字）】一个 ScheduleRun 内，一个 MESWorkOrderNo 一个且仅一个 ContinuationKey；
    ///   同一 MES 工单多个 Slice 共享同一 Key；**不得把 LogicalDemandKey 拼入 Key**（否则同一 MES 工单裂分）。
    ///   由 2号位 按 ScheduleRun + MESWorkOrderNo 生成真实值；1号位 只做**透明消费 + FinalTask 原样回传**，
    ///   **不生成、不解析、不拼接**（生成权属 2号位）。
    ///   null = 非连续份额（Free Slice）或无 MES 工单身份。
    /// </summary>
    public string? ContinuationKey { get; init; }

    /// <summary>
    /// 固定工艺路径编码（《APS_V1_1号位有限产能排程开发实施包 v1.6》§1号位新增/替换实施要求）：
    /// 「A/B输入必须带真实固定 `RouteCode / PathId / StartOperation` 与完整指定 Path DAG」。
    /// 非空 ⇒ 该需求走**固定路径**，1号位 不选路（Continuation Slice）。
    /// null ⇒ 无固定路径，由 1号位 在候选内择优（Free Slice / C桶）。
    /// 与 <see cref="PathId"/> 成对使用；**A/B 缺值应 Fail Closed，不得回退猜唯一 Path**（0号位 2026-10-07 裁决 §三）。
    ///
    /// ⚠ 2026-10-09：由 <c>init</c> 放开为 <c>set</c> —— 分桶（<c>ApplyContinuityBucketing</c>）发生在
    ///   <c>LoadRoutingContextAsync</c> **之前**，切片构造时无法拿到路由上下文，只能在路由装载后由
    ///   <c>FillContinuationRouteIdentities</c> 统一回填（与 <see cref="StartStageCode"/> 同款理由）。
    ///
    /// 🔴 **桥接期取值 ≠ 业务真值**：该回填的取值源是上游刚被 <c>NormalizeToSingleRoute</c> 就地改写的路由载荷
    ///   ⇒ 当前实际回填的是 **`DEFAULT / 1`**（1号位 技术适配值）。冻结 <c>11_2号位v2.1</c> 执行红线 <b>Q1</b>
    ///   明令「`DEFAULT / 1` **不得**作为归一化后的业务真值」「A/B 向 1号位 发送固定**真实** Route/Path，
    ///   **禁止预先压成 DEFAULT/1**」⇒ 本字段在桥接期**仅供 1号位 查图，不得当作持久化/FinalTask/Task/MES
    ///   执行身份的真实路径来源**。终态 = ① 1号位 去 <c>StageTimingNodeBuilder.GetRoutelessStages</c> 的
    ///   <c>RouteCode != "DEFAULT"</c> 判断（我方归一化删除补丁已备好、压住不发）+ ② 5号位 按 slice 交付
    ///   真实 <c>RouteCode / PathId</c>（Q-1008-1）。两者齐备后 <c>FillContinuationRouteIdentities</c> 因
    ///   「已带真值即跳过」自动失效，本字段即为真值。桥接期每次 Run 都有 WARNING 留痕，不静默。
    /// </summary>
    public string? RouteCode { get; set; }

    /// <summary>
    /// 固定路径序号（与 <see cref="RouteCode"/> 成对，语义见该字段）。
    /// 类型与 <c>RoutingOperation.PathId</c> 一致（int），可直接用于 <c>RoutePathKey</c> 图查找。
    /// </summary>
    public int? PathId { get; set; }

    /// <summary>
    /// 不拆不合硬标记（《APS_V1_1号位有限产能排程开发实施包 v1.6》：「A/B输入…并按 `NoSplitMerge` 处理」）。
    /// true ⇒ 本份额不得拆分、不得与他份额合批（A/B Continuation Slice 恒为 true，由 2号位 按桶置位）。
    /// false/缺省 ⇒ 按既有 AllowSplit/AllowMerge 策略处理。
    /// 与 <see cref="IsContinuation"/> 的 P0-07 语义同向，本字段是**显式载体**（0号位 2026-10-07 裁决 §四：
    /// 「`NoSplitMerge` 及固定 Route/Path 应显式落实」）。
    /// </summary>
    public bool NoSplitMerge { get; init; }

    /// <summary>
    /// 首选资源编码（软偏好，《APS_V1_1号位有限产能排程开发实施包 v1.6》§1号位新增/替换实施要求 + 红线 Q4）。
    ///
    /// 【口径】5号位 保留 Operation 级 `LastReportResourceCode` 真实事实；2号位 关联当前 Continuation Slice
    ///   并校验当前 Operation Eligibility，**合法可靠时**形成本值，否则为 NULL。
    ///   1号位 对非空值必须作为**资源连续性软偏好**进入目标函数/排序，
    ///   **不得升级为 Hard Lock**，不得突破 Eligibility / Calendar / Firm·Frozen / Material / Routing 等硬约束。
    ///
    /// 与旧字段 <see cref="PreferredResourceId"/> 的关系：旧字段是「上一 ACTIVE Task 的 ResourceId」的 ID 形态；
    ///   本字段是 v1.6 冻结的 **Code 形态正式输入**。两者并存，1号位 优先消费本字段（Code→Resource 适配），
    ///   本字段为空时回落旧字段（行为不变）。
    /// </summary>
    public string? PreferredResourceCode { get; init; }
}
