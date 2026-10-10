# 裁决请求：P1-02 Setup 换型属性（SetupAttribute）数据源 —— 3号位 致 0号位

> 收件：0号位（PM）／抄 2号位（数据引擎）、1号位（排程算法）
> 发件：3号位（策略参数治理）
> 依据：0号位 终裁 §一「Setup 正式保留并实际消费」；冻结 `SetupParams.Dimensions`（Mold/Tool/Material/Color）
> 日期：2026-09-16
> 性质：Setup 换型属性数据源未定义，请 0号位 裁决

---

## 一、背景

终裁 §一 已把 Setup 换型钉死为 V1 必需能力（正式保留并实际消费）。落地 Setup 需要两样数据：

| 数据 | 中文含义 | 数据链现状 |
|---|---|---|
| `SetupTime` | 换型要停多久 | ✅ 已具备（MES 已供，全链已通）|
| `SetupAttribute` | 按什么判断「要不要换型」（前后工序模具/刀具/颜色/材质是否一致）| ❌ **全链缺失，数据源未定义** |

## 二、现状（全链核对的事实）

1. **算法侧有字段、无数据**：1号位 `SetupOptimizer` 读 `SchedulingContext.SetupAttribute`（`string?`），但该优化器「当前未启用」，且无主数据供货。
2. **冻结参数明确是「属性式换型」**：`SetupParams` = `{ Dimensions: Mold/Tool/Material/Color, DefaultSetupMinutes: 30, SetupLookAheadSize: 5 }` —— 即按维度判断「同不同属性」，不是纯「平铺时长」。
3. **数据链从源头就是断的**：链路为 MES → ODS 契约视图 `MES_APS_Routing_Operation_View` → APS 透传视图 `ext_MES_APS_Routing_Operation_View` → `sp_SyncRoutingData` → `RoutingOperation` 表。该链今日只输出 `SetupTime`；整套 APS DDL 搜索 `Mold / Tool / Color / 模具 / 刀具 / 颜色` **均无任何换型属性列**。
4. **一个待核实事实**：ODS 契约视图的上游（MES 源表）是否藏着「工序→模具/刀具/颜色/材质」字段、只是没被契约视图映射出来——**此点仓库内无证据，需 2号位/业务 到 MES 侧核对**。

## 三、请 0号位 判断的核心问题

**V1 的换型属性（`SetupAttribute`）数据从哪来？**

| 分支 | 前提 | 落地 | 影响 |
|---|---|---|---|
| **方案 1 ｜补数据源** | MES/ODS 上游已有模具/刀具/颜色/材质字段 | 2号位 补 ODS 视图列映射 + APS 三处（表列/实体/投影）| 全链闭环，Setup 保留，业务口径不变 |
| **方案 2 ｜降级为平铺时长** | MES 无属性字段，但业务接受粗粒度 | 删除/停用 `Dimensions`，改为「前后工序一变即加 `SetupTime`」| 需回退冻结 `SetupParams.Dimensions` 语义 |
| **方案 3 ｜顺延 V2** | MES 无属性字段，业务不能接受粗粒度 | V1 换型暂不生效，`SetupOptimizer` 保持未启用 | 需回退终裁 §一「必须实际消费」口径 |

## 四、3号位 建议

先请 **2号位 核实 ODS 上游（MES 侧）是否已有可映射换型属性字段**（低成本事实核查，一两天可回）；有 → 走方案 1 直接闭环；无 → 0号位 在方案 2 / 3 之间定，3号位 据裁决改 `SetupParams` 冻结口径。

在 0号位 裁决前，APS 侧补列 ①②③ 我**暂不催 2号位 动手**，以免「加列无源」。

（工艺约定沿用：只引「值+符号锚」，不 cite 行号。）