using LPS.APS.Shared.Models;

namespace LPS.APS.Core.Models.Scheduling;

/// <summary>
/// 排程沙盘上下文（ScheduleContext）
/// 由2号位在阶段1填充，1号位在阶段3只读消费（除了填充Task的StartTime/EndTime）
/// 
/// 数据来源：
/// - Tasks：由2号位在阶段2经过Pegging+拆批后生成的10万个物理Task
/// - Resources/ResourceCalendars：设备及其可用日历
/// - VirtualInventory：跨域场景中上游落盘后构建的虚拟库存（带AvailableTime硬约束）
/// - StrategyConfig：0号位配置的排产策略（Frozen天数、MinBatchSize、换型时间等）
/// </summary>
public class SchedulingContext
{
    /// <summary>
    /// 计划版本ID（如：20260227_020000）
    /// 【2号位填充】阶段0.1 创建 PlanVersion 时生成
    /// </summary>
    public string PlanVersionId { get; set; } = string.Empty;

    /// <summary>
    /// 排程运行ID（对应 ScheduleRun.Id，由 00:38 预创建，02:00 读取绑定）
    /// MES 快照三表均以此 ID 为分区键
    /// </summary>
    public int ScheduleRunId { get; set; }

    /// <summary>
    /// 本次运行绑定的策略包版本ID（对应 StrategyProfileVersion.Id）
    /// 【3号位填充】02:00 从 ScheduleRun.StrategyProfileVersionId 读取，供追溯
    /// </summary>
    public long? StrategyProfileVersionId { get; set; }

    /// <summary>
    /// 产品族域标识（分域计算时标识当前域）
    /// 【2号位填充】阶段0.5 拓扑排序后确定当前执行域
    /// </summary>
    public string DomainKey { get; set; } = string.Empty;

    /// <summary>
    /// 计划期起始时间
    /// 【2号位填充】阶段0.1 根据业务规则（当前时间/工厂日历下一班次/原计划版本起点）确定
    /// </summary>
    public DateTime PlanHorizonStart { get; set; }

    /// <summary>
    /// 计划期结束时间
    /// 【2号位填充】阶段0.1 = PlanHorizonStart + 配置的计划窗口天数
    /// </summary>
    public DateTime PlanHorizonEnd { get; set; }

    /// <summary>
    /// 待排程的任务列表（由2号位填充，1号位填写StartTime/EndTime）
    /// 【2号位填充】阶段2 Pegging + 拆批后生成（每个订单工序展开为独立 Task）
    /// 【1号位消费】阶段3 填充 PlannedStartTime/PlannedEndTime
    /// </summary>
    public List<SchedulingTask> Tasks { get; set; } = new();

    /// <summary>
    /// 设备资源列表
    /// 【2号位填充】阶段1 从 Resource 表装载（ProductionDepartmentId/CapacityFactor/DispatchPriority）
    /// </summary>
    public List<SchedulingResource> Resources { get; set; } = new();

    /// <summary>
    /// 设备日历（ResourceId → 可用时间窗口列表）
    /// 【2号位填充】阶段1 合成（ResourceCalendar 基础班次 + HolidayCalendar 节假日 + MaintenanceSchedule 停机 + ResourceLock 手动锁定）
    /// </summary>
    public Dictionary<string, List<TimeWindow>> ResourceCalendars { get; set; } = new();

    /// <summary>
    /// 虚拟库存（跨域硬约束：MaterialId → 最早可用时间）
    /// 【2号位填充】阶段1 从上游域已落盘的排程结果读取（跨域协同场景），单域场景为空
    /// </summary>
    public Dictionary<string, DateTime> VirtualInventory { get; set; } = new();

    /// <summary>
    /// 现货库存（L5 内存消费层，阶段1.5 从 InventoryBalance 装载）
    /// Key = "MaterialCode|ProductFamilyId|FactoryId"（字符串复合键以便序列化）
    /// Value = 可用量（OnHandQty - AllocatedQty）
    /// 【2号位填充】阶段1.5 从 InventoryBalance 表装载
    /// 【5号位消费】阶段2 Pegging 规则消费（内存副本，不回写数据库）
    /// </summary>
    public Dictionary<string, decimal> InventorySupplies { get; set; } = new();

    /// <summary>
    /// 拼装库存键（与 InventorySupplies 的 Key 格式保持一致）
    /// </summary>
    public static string BuildInventoryKey(string materialCode, int productFamilyId, int factoryId)
        => $"{materialCode}|{productFamilyId}|{factoryId}";

    /// <summary>
    /// 排产策略配置（由0号位在界面配置，2号位装载）
    /// 【0号位配置】用户在界面维护 StrategyProfile/RuleSet/ParameterSet
    /// 【3号位填充】阶段0.1 根据 StrategyProfileVersionId 加载规则参数
    /// 【1号位消费】阶段3 读取 Mode/FrozenHorizonDays/DefaultSetupMinutes 等配置
    /// </summary>
    public StrategyConfig Strategy { get; set; } = new();

    /// <summary>
    /// 规则集配置列表（从 RuleSetVersion → RuleSet 加载）
    /// 【3号位填充】阶段0.1 根据 StrategyProfileVersionId 加载
    /// </summary>
    public List<RuleConfig> RuleConfigs { get; set; } = new();

    /// <summary>
    /// 参数集配置列表（从 ParameterSetVersion → ParameterSet 加载）
    /// 【3号位填充】阶段0.1 根据 StrategyProfileVersionId 加载
    /// </summary>
    public List<SchedulingParamConfig> SchedulingParamsList { get; set; } = new();

    /// <summary>
    /// 获取指定设备
    /// </summary>
    public SchedulingResource? GetResourceById(string resourceId)
    {
        return Resources.FirstOrDefault(r => r.ResourceId == resourceId);
    }

    /// <summary>
    /// 获取指定设备的日历（可用时间窗口列表）
    /// </summary>
    public List<TimeWindow>? GetResourceCalendar(string resourceId)
    {
        return ResourceCalendars.TryGetValue(resourceId, out var calendar) ? calendar : null;
    }

    /// <summary>
    /// 获取物料的最早可用时间（虚拟库存硬约束）
    /// </summary>
    public DateTime? GetMaterialAvailableTime(string materialId)
    {
        return VirtualInventory.TryGetValue(materialId, out var time) ? time : null;
    }

    /// <summary>
    /// MES 进度快照（阶段1.7 装载，1号位用于扣减已完成量）
    /// Key = "ProductionInstructionNo|MaterialCode|StageCode"
    /// Value = RemainingQty（剩余数量，已不低于0）
    /// 优先从 StageProgressSnapshot 装载（粒度粗、性能好）
    /// </summary>
    public Dictionary<string, decimal> MESRemainingQty { get; set; } = new();

    /// <summary>
    /// 拼装 MES 进度键
    /// </summary>
    public static string BuildMESKey(string productionInstructionNo, string materialCode, string stageCode)
        => $"{productionInstructionNo}|{materialCode}|{stageCode}";
}

/// <summary>
/// 排程任务（1号位视角的Task，轻量级内存对象）
/// 由2号位在阶段2生成，1号位在阶段3填充时间
/// </summary>
public class SchedulingTask
{
    /// <summary>
    /// 任务ID
    /// </summary>
    public string TaskId { get; set; } = string.Empty;

    /// <summary>
    /// 关联订单ID
    /// </summary>
    public string OrderId { get; set; } = string.Empty;

    /// <summary>
    /// 物料ID
    /// </summary>
    public string MaterialId { get; set; } = string.Empty;

    /// <summary>
    /// 目标设备ID
    /// </summary>
    public string ResourceId { get; set; } = string.Empty;

    /// <summary>
    /// 工序编码（路径内唯一，如 OP10, OP20）
    /// </summary>
    public string OperationCode { get; set; } = string.Empty;

    /// <summary>
    /// 工艺路径编码（V1固定'DEFAULT'，V2扩展多路径）
    /// </summary>
    public string RouteCode { get; set; } = "DEFAULT";

    /// <summary>
    /// 路径序号（V1固定1，V2扩展多路径）
    /// </summary>
    public int PathId { get; set; } = 1;

    /// <summary>
    /// 工序序号（来自 RoutingOperation.OperationSeq，V1 作为同一订单内的串行顺序依据）
    /// 在优先级并列时作为次级排序依据，确保同一订单的前道工序先排
    /// </summary>
    public int OperationSeq { get; set; }

    /// <summary>
    /// 前置工序Task ID列表（v5.0：图模型支持多前驱，并行支路汇合时有多个前驱）
    /// 空列表表示该工序是起始工序
    /// </summary>
    public List<string> PredecessorTaskIds { get; set; } = new();

    /// <summary>
    /// 优先级（值越大越优先，来自阶段2的综合打分）
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// 加工数量
    /// </summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// 标准加工时长（分钟）
    /// </summary>
    public double DurationMinutes { get; set; }

    /// <summary>
    /// 换型属性（模具编号/颜色代码/材质规格，由5号位在阶段2.3标注）
    /// </summary>
    public string? SetupAttribute { get; set; }

    /// <summary>
    /// 客户交期（用于倒排寻址）
    /// </summary>
    public DateTime? CustomerDueDate { get; set; }

    /// <summary>
    /// 【1号位填充】计划开工时间
    /// </summary>
    public DateTime? PlannedStartTime { get; set; }

    /// <summary>
    /// 【1号位填充】计划完工时间
    /// </summary>
    public DateTime? PlannedEndTime { get; set; }

    /// <summary>
    /// 是否被锁定（PMC手动锁定或在制品锚点，重排时不可移动）
    /// </summary>
    public bool IsLocked { get; set; }

    /// <summary>
    /// 是否冻结（由5号位在阶段5.0判定，IsFrozen=true才下发MES）
    /// </summary>
    public bool IsFrozen { get; set; }
}

/// <summary>
/// 排程设备资源
/// </summary>
public class SchedulingResource
{
    /// <summary>
    /// 设备ID
    /// </summary>
    public string ResourceId { get; set; } = string.Empty;

    /// <summary>
    /// 设备名称
    /// </summary>
    public string ResourceName { get; set; } = string.Empty;

    /// <summary>
    /// 工厂ID
    /// </summary>
    public string FactoryId { get; set; } = string.Empty;

    /// <summary>
    /// 排程责任部门ID（v5.0.16：替代原 WorkshopCode）
    /// </summary>
    public int ProductionDepartmentId { get; set; }

    /// <summary>
    /// 产能系数（v5.0：来自 Resource.CapacityFactor，可被 ResourcePlanningContext.OverrideCapacityFactor 覆盖）
    /// </summary>
    public decimal CapacityFactor { get; set; } = 1.0m;

    /// <summary>
    /// 派工优先级（越小越优先，来自 ResourcePlanningContext.DispatchPriority）
    /// </summary>
    public int DispatchPriority { get; set; } = 100;

    /// <summary>
    /// 是否可用
    /// </summary>
    public bool IsAvailable { get; set; } = true;
}

/// <summary>
/// 排产策略配置（由0号位在界面配置）
/// </summary>
public class StrategyConfig
{
    /// <summary>
    /// 冻结区天数（如：未来3天）
    /// </summary>
    public int FrozenHorizonDays { get; set; } = 3;

    /// <summary>
    /// 确认区天数（如：未来7天）
    /// </summary>
    public int FirmHorizonDays { get; set; } = 7;

    /// <summary>
    /// 默认换型时间（分钟）
    /// </summary>
    public double DefaultSetupMinutes { get; set; } = 30;

    /// <summary>
    /// 排程模式：Backward（倒排）/ Forward（正排）
    /// </summary>
    public SchedulingMode Mode { get; set; } = SchedulingMode.BackwardThenForward;

    /// <summary>
    /// 换型优化前瞻窗口大小
    /// </summary>
    public int SetupLookAheadSize { get; set; } = 5;
}

/// <summary>
/// 规则集配置（从 RuleSetVersion → RuleSet 加载）
/// </summary>
public class RuleConfig
{
    public long RuleSetVersionId { get; set; }
    public string RuleSetCode { get; set; } = string.Empty;
    public string RuleSetName { get; set; } = string.Empty;
    public string VersionCode { get; set; } = string.Empty;
}

/// <summary>
/// 参数集配置（从 ParameterSetVersion → ParameterSet 加载）
/// </summary>
public class SchedulingParamConfig
{
    public long ParameterSetVersionId { get; set; }
    public string ParameterSetCode { get; set; } = string.Empty;
    public string ParameterSetName { get; set; } = string.Empty;
    public string VersionCode { get; set; } = string.Empty;
}

/// <summary>
/// 排程模式
/// </summary>
public enum SchedulingMode
{
    /// <summary>
    /// 倒排（从交期往前推）
    /// </summary>
    Backward,

    /// <summary>
    /// 正排（从当前往后推）
    /// </summary>
    Forward,

    /// <summary>
    /// 倒排优先，撞墙后翻转正排（推荐）
    /// </summary>
    BackwardThenForward
}
