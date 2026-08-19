using LPS.APS.Engine.Services.Sync.Dto;

namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// 跨域依赖静态扫描服务（2号位职责 — 每日 01:50）
/// 
/// 业务目的：
///   在 02:00 主排程启动前，把"哪个产品族依赖哪个产品族"这张图
///   静态固化到 APS.Domain_Dependency 表，供 3 号位做 Kahn 拓扑排序。
/// 
/// 为什么必须静态扫描（架构红线）：
///   若 3 号位在内存沙盘中动态发现跨域依赖，会产生"鸡生蛋蛋生鸡"悖论：
///     排 A 时才发现缺 B → 暂停切去排 B
///     排 B 时又发现缺 C → 再暂停切去排 C
///     资源动态回滚、推演震荡，15 分钟性能目标破产。
///   所以必须在 02:00 前把完整依赖图扫一次落盘，排程时只读不写。
/// 
/// 数据流：
///   APS.APS_BOM_RAW + Material + ProductFamily
///     → sp_ScanDomainDependency (TRUNCATE + INSERT 全量刷新)
///     → APS.Domain_Dependency 表
///     → 3 号位在 02:00 SELECT * FROM Domain_Dependency 构建拓扑
/// 
/// 调度契约：
///   Hangfire RecurringJob "domain-dependency-scan" @ Cron "50 1 * * *"
///   排在 nightly-batch (00:30, LLC 完成) 之后，scheduling (02:00) 之前
/// 
/// SP 契约：参见 .windsurf/docs/APS_跨域依赖扫描DDL补充_v1.0.sql
/// 走查文档：《APS 核心排产全流程走查 v3.3》§0.5 阶段0.5
/// </summary>
public interface IDomainDependencyScanService
{
    /// <summary>
    /// 执行跨域依赖静态扫描
    /// 调用 sp_ScanDomainDependency 存储过程在 APS 库本地完成 TRUNCATE + INSERT
    /// </summary>
    Task<DomainDependencyScanResultDto> ScanAsync(CancellationToken cancellationToken = default);
}
