using LPS.APS.Engine.Services.Sync.Dto;

namespace LPS.APS.Engine.Services.Sync;

/// <summary>
/// 库存同步服务（2号位职责 — 每日 00:35）
/// 
/// 五层库存架构链路：
///   L1 事实层      InventoryFact_ERP / InventoryFact_MES（本服务刷新）
///                      ↓ 通过 MaterialMapping 统一 MaterialCode
///   L2 候选池      InventorySupplyCandidate（本服务刷新）
///                      ↓ 产品族仓库准入 + 来源例外规则
///   L3 规则层      ProductFamilyInventoryScope + InventorySourceRule（人工配置）
///                      ↓ 按 UQ_Inventory_Balance 聚合
///   L4 可用库存    InventoryBalance（本服务刷新，排程唯一真相）
///                      ↓ 阶段1.5 装载
///   L5 内存消费    SchedulingContext.InventorySupplies（2号位沙盘装载阶段）
/// 
/// 契约链路：
///   ODS.ERP_Inventory_View / MES_Inventory_View（3号位创建）
///     → APS.ext_ERP_Inventory_View / ext_MES_Inventory_View（2号位创建）
///       → sp_SyncInventory 全量刷新 L1
///         → sp_RefreshInventoryBalance 级联刷新 L2→L3→L4
/// 
/// 架构红线（§2.5.2 互斥隔离）：
///   ❌ 禁止同 MaterialCode 双源库存默认叠加
///   ✅ 双源并存必须通过 InventorySourceRule 按 PREFER 规则单选
///   ✅ AllocatedQty 由 5 号位 Pegging 扣减规则维护，本服务只刷新 OnHandQty
///   ⚠️ V1 产品族仓库准入策略：未配置即放行；显式 IncludeFlag=0 才排除
/// </summary>
public interface IInventorySyncService
{
    /// <summary>
    /// 同步库存全链路（L1 事实层 → L4 可用库存）
    /// 调用 sp_SyncInventory 存储过程在 APS 库本地完成刷新
    /// </summary>
    Task<InventorySyncResultDto> SyncAsync(CancellationToken cancellationToken = default);
}
