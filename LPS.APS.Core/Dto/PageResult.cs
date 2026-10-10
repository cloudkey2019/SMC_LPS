namespace LPS.APS.Core.Dto;

/// <summary>
/// 通用分页结果容器（R2 契约：data 承载 { items, total, page, pageSize }）。
/// 用于 RBAC 等列表 GET 端点，前端据此渲染 <c>ElPagination</c>。
/// </summary>
/// <typeparam name="T">当前页元素类型</typeparam>
public sealed class PageResult<T>
{
    /// <summary>当前页数据</summary>
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();

    /// <summary>命中总数（前端 total 联动）</summary>
    public int Total { get; set; }

    /// <summary>当前页码（从 1 起）</summary>
    public int Page { get; set; }

    /// <summary>每页条数</summary>
    public int PageSize { get; set; }
}