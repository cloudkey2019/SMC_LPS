namespace LPS.APS.Core.Enum;

/// <summary>
/// 治理版本状态（冻结 DDL v5.1.2：三张版本表 CK 校验字面量）
/// 六态：DRAFT / SUBMITTED / APPROVED / PUBLISHED / DISABLED / ARCHIVED
/// 值即 DDL CHECK 字面量，实体 Status 字段直接存储该字符串。
/// 红线：已发布（PUBLISHED）版本不可原地修改，须创建新版本；正式排程只允许 PUBLISHED 状态。
/// </summary>
public static class GovernanceVersionStatus
{
    public const string Draft = "DRAFT";
    public const string Submitted = "SUBMITTED";
    public const string Approved = "APPROVED";
    public const string Published = "PUBLISHED";
    public const string Disabled = "DISABLED";
    public const string Archived = "ARCHIVED";

    /// <summary>全部六态（校验/枚举用）</summary>
    public static readonly string[] All = [Draft, Submitted, Approved, Published, Disabled, Archived];
}
