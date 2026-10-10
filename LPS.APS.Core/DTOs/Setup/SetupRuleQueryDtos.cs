namespace LPS.APS.Core.DTOs.Setup;

/// <summary>
/// 工序资源资格结果（4号位 Setup 契约 §11.1 #9）。
/// 算法（§3.3/§7.2）：当前 Operation 合法资源 ∩ 同部门同 Stage 任一 Operation 合法资源（前后产品都合法的设备交集）。
/// 口径终定：materialId/toMaterialId 为物料主键（Material.Id），响应回带 resourceCode/departmentCode/stageCode。
/// </summary>
public sealed class OperationResourceEligibilityDto
{
    /// <summary>当前小工序码</summary>
    public string OperationCode { get; set; } = string.Empty;

    /// <summary>合法资源编码列表（交集结果，Code）</summary>
    public List<string> ResourceCodes { get; set; } = new();

    /// <summary>合法资源明细（Code + 名称 + 部门/阶段）</summary>
    public List<OperationResourceInfoDto>? Resources { get; set; }
}

/// <summary>工序资源资格明细元素（#9）</summary>
public sealed class OperationResourceInfoDto
{
    /// <summary>资源编码</summary>
    public string ResourceCode { get; set; } = string.Empty;

    /// <summary>资源名称（主数据）</summary>
    public string? ResourceName { get; set; }

    /// <summary>所属部门编码（主数据）</summary>
    public string? DepartmentCode { get; set; }

    /// <summary>所属大工艺阶段码（主数据）</summary>
    public string? StageCode { get; set; }
}

/// <summary>
/// 规则集版本列表元素（#11）。
/// Status 为版本治理六态原文（版本管理页语义；区别于 SetupRuleDto.Status 的派生三态）。
/// </summary>
public sealed class SetupRuleSetVersionDto
{
    /// <summary>版本主键</summary>
    public long Id { get; set; }

    /// <summary>所属规则集</summary>
    public long RuleSetId { get; set; }

    /// <summary>版本编码</summary>
    public string VersionCode { get; set; } = string.Empty;

    /// <summary>版本治理状态（DRAFT/SUBMITTED/APPROVED/PUBLISHED/DISABLED/ARCHIVED）</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>生效起始</summary>
    public DateTime? EffectiveFrom { get; set; }

    /// <summary>生效截止</summary>
    public DateTime? EffectiveTo { get; set; }

    /// <summary>发布时间</summary>
    public DateTime? PublishedAt { get; set; }

    /// <summary>发布人</summary>
    public string? PublishedBy { get; set; }

    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// 规则集版本差异（#12）。
/// 对比两个版本的 Setup 换型规则（仅 IsActive 有效规则），按唯一键（EXACT 七元组 / DEFAULT 五元组）归类 added/modified/removed；
/// modified 判定 = 同唯一键下 SetupMinutes 变化。
/// </summary>
public sealed class SetupDiffDto
{
    /// <summary>源版本 Id（路径 id）</summary>
    public long RuleSetVersionId { get; set; }

    /// <summary>目标版本 Id（otherVersionId）</summary>
    public long OtherVersionId { get; set; }

    /// <summary>新增规则数</summary>
    public int AddedCount { get; set; }

    /// <summary>修改规则数</summary>
    public int ModifiedCount { get; set; }

    /// <summary>删除规则数</summary>
    public int RemovedCount { get; set; }

    /// <summary>目标版本治理状态（publishStatus）</summary>
    public string PublishStatus { get; set; } = string.Empty;

    /// <summary>对比时间</summary>
    public DateTime? ComparedAt { get; set; }

    /// <summary>Setup 换型规则变更明细（确认函 Q7 补维度）</summary>
    public SetupRuleChangesDto SetupRuleChanges { get; set; } = new();
}

/// <summary>Setup 换型规则变更明细（added/modified/removed 三维）</summary>
public sealed class SetupRuleChangesDto
{
    /// <summary>新增规则（目标版本有、源版本无）</summary>
    public List<SetupRuleChangeDto> Added { get; set; } = new();

    /// <summary>修改规则（同唯一键、SetupMinutes 变化；含 before/after 快照）</summary>
    public List<SetupRuleChangeDto> Modified { get; set; } = new();

    /// <summary>删除规则（源版本有、目标版本无）</summary>
    public List<SetupRuleChangeDto> Removed { get; set; } = new();
}

/// <summary>Setup 换型规则变更行（契约 §11.1；Code 呈现，modified 行另含 id/before/after）</summary>
public sealed class SetupRuleChangeDto
{
    /// <summary>规则类型（EXACT / DEFAULT）</summary>
    public string RuleType { get; set; } = string.Empty;

    /// <summary>当前小工序码</summary>
    public string OperationCode { get; set; } = string.Empty;

    /// <summary>生产部门编码（Code 回带）</summary>
    public string? DepartmentCode { get; set; }

    /// <summary>大工艺阶段码</summary>
    public string? StageCode { get; set; }

    /// <summary>当前设备编码（Code 回带）</summary>
    public string? ResourceCode { get; set; }

    /// <summary>前产品编码（EXACT；DEFAULT 为 null）</summary>
    public string? FromMaterialCode { get; set; }

    /// <summary>后产品编码（EXACT；DEFAULT 为 null）</summary>
    public string? ToMaterialCode { get; set; }

    /// <summary>换型分钟</summary>
    public decimal? SetupMinutes { get; set; }

    /// <summary>规则主键（modified 行）</summary>
    public long? Id { get; set; }

    /// <summary>修改前快照（modified 行；源版本读模型）</summary>
    public SetupRuleDto? Before { get; set; }

    /// <summary>修改后快照（modified 行；目标版本读模型）</summary>
    public SetupRuleDto? After { get; set; }
}

/// <summary>发布规则集版本请求体（#13 body：changeReason）</summary>
public sealed class SetupPublishRequest
{
    /// <summary>变更原因（可选）</summary>
    public string? ChangeReason { get; set; }
}

/// <summary>资源主数据读结果（IMasterDataLookupRepository 批查回带）</summary>
public sealed class ResourceLookupInfo
{
    /// <summary>资源主键</summary>
    public int Id { get; set; }

    /// <summary>资源编码</summary>
    public string ResourceCode { get; set; } = string.Empty;

    /// <summary>资源名称</summary>
    public string? ResourceName { get; set; }

    /// <summary>排程责任部门主键</summary>
    public int ProductionDepartmentId { get; set; }
}

/// <summary>部门主数据读结果（IMasterDataLookupRepository 批查回带）</summary>
public sealed class DepartmentLookupInfo
{
    /// <summary>部门主键</summary>
    public int Id { get; set; }

    /// <summary>部门编码</summary>
    public string DeptCode { get; set; } = string.Empty;

    /// <summary>单值归属大工艺阶段码（1:1）</summary>
    public string StageCode { get; set; } = string.Empty;
}

/// <summary>
/// 主数据 Code→Id 读端点响应：部门下拉项（Setup 三 Dialog 数据源；用户口径 masterId=表主键 Id，code=DeptCode）。
/// 红线 #4：始终返回列表；code 命中多条由前端/优先级判定（无唯一性假设）。
/// </summary>
public sealed class DepartmentLookupItem
{
    /// <summary>部门主键（= ProductionDepartment.Id，前端下拉 value 落 Id）</summary>
    public int MasterId { get; set; }

    /// <summary>部门编码（= DeptCode，前端下拉 label 主源）</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>单值归属大工艺阶段码（1:1）</summary>
    public string StageCode { get; set; } = string.Empty;

    /// <summary>源系统部门码（= ProductionDepartment.SourceDeptCode；2026-10-08 应 4号位 前端需求补返回）</summary>
    public string? SourceDeptCode { get; set; }

    /// <summary>生产单元 holon 码（= ProductionDepartment.Holon；与 DeptCode 内嵌 HL 码同源）</summary>
    public string? Holon { get; set; }
}

/// <summary>主数据 Code→Id 读端点响应：设备下拉项（masterId=表主键 Id，code=ResourceCode）</summary>
public sealed class ResourceLookupItem
{
    /// <summary>资源主键（= Resource.Id）</summary>
    public int MasterId { get; set; }

    /// <summary>资源编码（= ResourceCode）</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>资源名称（显示名）</summary>
    public string? Name { get; set; }

    /// <summary>排程责任部门主键</summary>
    public int ProductionDepartmentId { get; set; }
}

/// <summary>主数据 Code→Id 读端点响应：物料下拉项（masterId=Material.Id，code=MaterialCode）</summary>
public sealed class MaterialLookupItem
{
    /// <summary>物料主键（= Material.Id）</summary>
    public int MasterId { get; set; }

    /// <summary>物料编码（= MaterialCode）</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>物料名称（显示名）</summary>
    public string? Name { get; set; }

    /// <summary>规格（Spec）</summary>
    public string? Spec { get; set; }

    /// <summary>计量单位（UOM）</summary>
    public string? Uom { get; set; }

    /// <summary>启用标记（IsActive；activeOnly 过滤后恒 true）</summary>
    public bool IsActive { get; set; }
}
