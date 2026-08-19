# BOM Workset 生成与错误处理技术方案

**版本**：v1.8  
**日期**：2026-05-22  
**基于**：《BOM展开经验库》R01~R31 + 7 个复杂样本案例（MGGMB40-450 等）  
**配套 DDL 版本**：`APS_数据库表结构设计_v5.0.sql` **v5.0.26**  
**配套字段文档**：`APS_数据库字段说明文档_v5.0.md` **v5.0.26**  
**维护责任人**：2 号位（架构）+ 5 号位（实现）  
**适用阶段**：APS 每日 00:00 批量 BOM 展开 / 白天插单实时展开

---

### v1.8 变更要点（2026-05-22 R29补R17工厂过滤+失败路径；R28补代理后仍空降级R29）

1. **§1.4 R29 全量查后增加R17工厂过滤步骤**：
   - 全量查 `MES_BOM_Edge_Active WHERE ParentMaterialCode=MaterialCode` 后，增加 `EXISTS ProcessCodeDict WHERE FactoryCode=@OrderFactory` 过滤
   - **R17过滤后为空 → `BOM_ENTRY_NOT_FOUND`（ERROR）**，不走R30外购兜底（R30仅适用于全量查本身无结果）
   - 务必同工厂匹配，不允许跨工厂兜底
2. **§1.4 R28 补代理后仍无出口库→降级R29**：
   - Step1代理（CN6课→CN）后 `@ExportProcCodes` 仍为空 → 降级走R29+R17路径
   - 降级后仍用 `@OrderFactoryCode` 作工厂过滤，不放弃工厂约束

### v1.7 变更要点（2026-05-21 BOMNO为空/0时的首层BOM入口推导规则，对齐经验库 R28/R29/R30）

1. **§1.4 BOM入口解析分流 — `SALES_ORDER` 新增 MaterialCode 前缀分流子规则**（R28/R29/R30）：
   - `BOMNO IS NULL OR BOMNO='0'` + `OrderType=SALES_ORDER` 下，按 `MaterialCode` 前缀三分支处理，替代原有单一的 `ParentMaterialCode` 直查逻辑
   - `ASSY` 前缀：先查 `ProcessCodeDict.WarehouseRole='出口库'` 得出口ProcessCode集，再用 `ParentProcRefCode IN 集合 AND ParentMaterialCode=MaterialCode` 查首层（R28）
   - `WIP`/`RAW` 前缀：直接 `ParentMaterialCode=MaterialCode` 查首层，套用R17/R22（R29）
   - `RAW` 前缀+无结果：判定外购件，不展开，不写Issues（R30）
   - **第二层及以后不变**：完全复用常规展开规则（R16/R17/R22/R25等）
2. **`PRODUCTION_INSTRUCTION` + BOMNO为空/0 新增必写Issues**（R31）：
   - 查找逻辑同第二层BOM（`ParentMaterialCode=MaterialCode`，MES来源优先，R17/R22）
   - **无论是否找到候选，必须写 `Issues(BOMNO_MISSING_PRODUCTION)`**（WARN=找到/ERROR=未找到）
   - 生产类订单理论上应有确定BOMNO，缺失属异常，不阻塞批次但必须留审计痕迹

---

### v1.6 变更要点（2026-05-14 MES_BOM_Edge_Active 物化边表架构切换，对齐 DDL v5.0.26）

1. **展开数据源迁移 — `MES_BOM_Edge_Active` 替代 `MES_BOM_View` 进入展开路径**：
   - `MES_BOM_Edge_Active` 为V1正式 BOM 防腐合同层+执行优化层（物化边表），由 `sp_RefreshBOMEdgeActive` 每日刷新
   - `MES_BOM_View` v5.0.26 降为兼容视图（`SELECT * FROM MES_BOM_Edge_Active WHERE IsActive=1`），字段契约不变
   - **禁止对 `MES_BOM_View` 做递归 CTE**：`sp_ExpandBOMBatch_vNext` / `sp_ExpandBOMRealtime_vNext` 必须直接读 `MES_BOM_Edge_Active`

2. **`sp_ExpandBOMBatch_vNext` WHILE 迭代展开模式**：
   - Step 0：RefreshLog 前置校验（最新刷新必须 COMPLETED，否则报错）
   - Step 1：第1层按 BOMNO 寻址，把 `Request_Detail.Id` 写入 `RequestDetailId` 透传
   - Step 2：WHILE 循环每层 JOIN `MES_BOM_Edge_Active` 聚集索引，逾层携带 `RequestDetailId`
   - Step 3：落地至 `MES_APS_BOM_Workset`（含 RequestDetailId）；调用 `sp_EnrichBOMWorkset`
   - 旧 `sp_ExpandBOMBatch`（递归CTE版）标记 deprecated，稳定后下线

3. **`WorksetId` 字段加入 StageDetail 变体（v5.0.26）**：
   - `MES_APS_BOM_Workset_StageDetail` / `_Archive` / `_Realtime` 均新增 `WorksetId BIGINT NULL`（FK→Workset.Id）
   - `sp_CleanupBOMWorkset` 改按 `WorksetId` 级联删除 StageDetail，替代旧 BatchNo+BOMNO+Material 关联清理
   - 2号位搬运 `StageDetail → APS_BOM_STAGE_PATH_RAW` 时同步透传 `WorksetId`（跨库引用，非FK）

4. **`RequestDetailId` 加入 Workset_Archive + Workset_Realtime（v5.0.26）**：
   - 归档/实时写入时从 Workset 透传 `RequestDetailId`，按请求明细追溯历史归档行无需过 Request 表反查
   - **禁止将 RequestDetailId 加入 StageDetail 表**（经 WorksetId→Workset.RequestDetailId 反查即可）

---

### v1.5 变更要点（2026-05-08 BOM入口解析分流 + RequestDetailId 追溯锚点，对齐 DDL v5.0.21）

1. **BOM 入口解析分流**（新增 §1.4）：
   - v5.0.21 起，`MES_API_BOM_Request_Detail.BOMNO` 改为可空
   - 5 号位在 `sp_ExpandBOMBatch` 执行前**先做入口分流**：`BOMNO IS NOT NULL` → 直接按 BOMNO 定位 `MES_BOM_View`；`BOMNO IS NULL` → 从 `Model`/`MaterialCode` 推导 BOM 入口（查 `MES_BOM_View` WHERE `ParentMaterialCode=MaterialCode`），若推导失败登记 `MES_APS_BOM_Workset_Issues`（IssueType=`BOM_ENTRY_NOT_FOUND`）
   - **2 号位职责**：只推送基础字段（`OrderStagingId`/`Model`/`MaterialCode`/`FactoryCode`/`BOMNO?`），不做入口解析
   - **设计决策写死**：入口解析分流归属 5 号位，禁止上移到 2 号位

2. **RequestDetailId 追溯锚点**（§2.1 字段映射新增）：
   - `MES_APS_BOM_Workset.RequestDetailId BIGINT NULL` → FK→`MES_API_BOM_Request_Detail.Id`
   - `MES_APS_BOM_Workset_Issues.RequestDetailId BIGINT NULL` → 同上
   - 5 号位展开时从 `MES_API_BOM_Request_Detail` 读取 `Id` 写入；**非业务键，1 号位不消费**；用于 2/5 号位追溯、回写、运营闭环

3. **MES_API_BOM_Request_Detail 新结构**（v5.0.21）：
   - 新增字段：`OrderStagingId` / `Model` / `MaterialCode` / `FactoryCode`
   - `BOMNO` 改可空
   - 唯一约束从 `(BatchNo, BOMNO)` 变更为 `(BatchNo, OrderStagingId)`

---

### v1.4 变更要点（2026-04-29 生产部门主链 + ProcessCode→StageCode 共享映射）

对齐 DDL v5.0.16 + 防腐层 v1.15 + 架构总表 v3.12：

1. **5 号位与 2 号位共享 ProcessCode → StageCode 基础映射**（架构总表红线 #16）：
   - 5 号位 `sp_EnrichBOMWorkset`（BOM 边粒度的 ProcessCode → StageCode 推导）
   - 2 号位 `sp_RebuildMaterialStageDeptContext`（仓库级 MSC 归一化时也需要 StageCode）
   - **统一查同一列**：`MES_ProcessCode_View.StageCode`（v5.0.16 新增 APS 增强列）
   - **禁止各写一套规则**——否则两边映射不一致会造成 1 号位按 `(MaterialId, StageCode)` 锁部门时静默断裂
   - 上层场景逻辑（"如何选出 ProcessCode"，5 号位走 BOM 边、2 号位走 MSC + Override）不共享，但底层映射必须共享

2. **5 号位职责边界明确**：
   - 5 号位**只产出** `StageDetail.StageCode`（BOM 边/根产品的阶段路径）+ `BOM_Workset_Issues`
   - 5 号位**不参与** 部门维度组装（部门是 2 号位 `sp_RebuildMaterialStageDeptContext` 的产物）
   - 5 号位**不写** `MaterialStageDeptContext` / `MaterialStageDeptOverride` / `ProductionDepartment` 任何一张表

3. **降级哲学保持一致**（与 §2.4e MaterialStageDeptContext_Issues 同向）：
   - BOM_Workset_Issues：批次永不阻塞；写 Issues + DegradeAction，状态机走 READY
   - MaterialStageDeptContext_Issues（v5.0.16 新增）：旧 IsCurrent=1 不动；新问题登记，待人工修正后局部重建
   - 两条降级链路独立运行，互不干扰

4. **ProcessCodeDict 维护方变更**（影响 5 号位 IDataLoader 调用契约）：
   - v5.0.15：5 号位通过 `MES_ProcessCode_View` 消费，物理表由 `sp_SyncMasterData(@SourceType='ProcessCode')` 自动同步
   - v5.0.16：5 号位**仍**通过 `MES_ProcessCode_View` 消费（不变），但物理表由 **APS 系统管理员人工维护**（不再自动同步）；新增 `StageCode` 增强列；`SourceSystem` 重命名为 `CodeOrigin`

5. **输入契约保持稳定**：5 号位的 ODS 输入（`MES_BOM_View` / `MES_ProcessCode_View` / `ext_*`）字段语义不变；`MES_ProcessCode_View` 仅扩列（StageCode + CodeOrigin），不影响既有列消费  

---

## 0. 文档定位与设计哲学

本文档是 **BOM Workset 与 StageDetail 生成逻辑的唯一技术真源**，明确：

1. 从 ERP BOM 原始数据 → `MES_APS_BOM_Workset` + `StageDetail` + `Issues` 的完整推导流程
2. R17 Produce→工厂映射、R24 原生序、R25 异厂收敛、R26 受托隔离、R27 错误分治 的工程化实现
3. ERP 升级时的字段吸收策略
4. BOM 错误数据的检测、登记、追溯、ERP 源端修复闭环

### v1.3 变更要点（2026-04-28 ProduceToFactoryMap 照片权威对齐）

1. **R20 目标工厂映射纠正**：§2.2 业务含义简表 + §3.3 R25 说明中的 “6→BJ、7→TJ、11→SH” 纠正为 **“6→BJ、7→CN、11→TJ”**（原 7、11 两处目标工厂写反，导致 R17 厂映射结果全错）
2. **Produce 分类体系收敛**：Produce=5/8/9 从 “内制·特注” 纠正为 “内制·自用”；SourceCategory 取消 INHOUSE_SPECIAL，值域收敛为 3 类：`PURCHASE` / `INHOUSE_SELF` / `INHOUSE_CROSS`
3. **依据**：以用户提供的 ERP Produce 权威照片为准（详见字段文档 v5.0.14 §1.8 + DDL v5.0.14 §1.9b INSERT 数据）

### v1.2 变更要点（2026-04-24 工艺数据三层模型收敛）

1. **BOM↔Routing 对接主键明确** = `(MaterialCode, StageCode)` 二元组；三层模型（OperationName / ProcessType / StageCode）固化到本文档 §3.4
2. **StageSeq 唯一权威源** = `StageDetail.StageSeq`；`RoutingStage.StageSeq` 已从 DDL 中**删除**
3. **R20 跨组织视角统一** = BOM 侧 StageDetail.StageCode 采用**目标工厂视角**（父件 TJ + R20 指派 BJ → 直接写 BJ_MACH，不是 TJ_MACH）
4. **1 号位 R20 消费语义调整**（相对 v1.1）：
   - v1.1：`CrossOrgHandoffFlag=1` → 1 号位"跳过本厂 Task 生成，产出虚拟需求"
   - v1.2：`CrossOrgHandoffFlag=1` → 1 号位**按 StageDetail.StageCode（他用方视角）直接去他用方工厂的 RoutingOperation 找小工序生成 Task**；本厂不生成该子件的 Task
   - 含义：1 号位是统一排程器，同时生成本厂 + 他用方工厂的 Task；R20 只是决定"该 Task 落在哪个工厂的产能队列"
5. **ProcessType 配置化** = 新增 `ProcessTypeDict`（骨架期 IsActive=0，预留扩展）；`ProcessType` **不参与** BOM↔Routing 对接，仅作统计/粗分组
6. **OperationCode 不全局字典化**（MES 侧不可控 + 新增频繁；跨厂对接靠 StageCode 足够）

---

### 设计哲学（v1.1 核心声明）

> **防腐层只做“吸震 + 登记”，不做“生产准入判断”。**

具体表现：
- 批次状态机：`PENDING → EXPANDED → ENRICHED → READY`（**永不因数据质量进 FAILED**）
- 任何 BOM 数据异常 → “降级 + 登记”，不阻塞批次
- Issues 表 Severity 只表达“事后处置优先级”，不是“批次准入开关”
- `FAILED` 仅保留给 **SP 进程崩溃**（tempdb 满、连接中断等极端情况）

理由：
- **业务现实**：10 万级 Task 排程不能因几条 BOM 数据问题整批停掉
- **工业哲学**："让不完美的计算跑完" > "让完美的计算停掉"
- **防腐纯度**：承担准入判断等于防腐层侵入了生产管理职责
- **兼容兼包**：1 号位对 `ChildRequiredStageCode=NULL` 本就有保守策略兑底

> **与其他文档的边界**
> - 防腐分层架构 → 详见《APS_数据架构与防腐层设计方案_v5.0》§3.1.3b
> - 表结构字段 → 详见《APS_数据库字段说明文档_v5.0》§1.3 / §1.3b / §1.5 / §1.6 / §1.7
> - 业务规则索引 → 详见《BOM展开经验库.md》R01~R27

---

## 1. 防腐分层与职责边界

### 1.1 三层架构图

```
┌─────────────────────────────────────────────────────────────┐
│ L0  ERP / MES 源系统 → MES_BOM_Source_View（刷新输入，防腐入口） │
│   → sp_RefreshBOMEdgeActive → MES_BOM_Edge_Active（物化边表） │
│     BOMNO/ParentMaterialCode/ChildMaterialCode/Quantity       │
│     ParentProcRefCode/ChildProcRefCode/ChildSourceHintCode    │
│   ← 触发源：MES_API_BOM_Request_Detail（v5.0.21：订单粒度     │
│     OrderStagingId/Model/MaterialCode/FactoryCode；BOMNO可空） │
│                                                              │
│   ⚠️ ERP 升级时，改 MES_BOM_Source_View（吸震层）              │
│   ⚠️ MES_BOM_View 为下游兼容视图，禁止做递归CTE，禁止刷新来源   │
└────────────────────────┬─────────────────────────────────────┘
                         ↓ sp_ExpandBOMBatch_vNext WHILE 迭代展开（直读 MES_BOM_Edge_Active）
┌─────────────────────────────────────────────────────────────┐
│ L1  ODS 核心合同层（APS 稳定输出）                            │
│   MES_APS_BOM_Workset                                        │
│     + ChildRequiredStageCode  (v5.0.7 5号位回填)              │
│     + ChildRequiredFactory    (v5.0.10 5号位回填，R17 推导)   │
│     + RequestDetailId         (v5.0.21 追溯锚点，nullable)    │
│   MES_APS_BOM_Workset_StageDetail  (EDGE + ROOT)             │
│   MES_APS_BOM_Workset_Issues       (v5.0.10 诊断独立表)      │
│     + RequestDetailId              (v5.0.21 追溯锚点)        │
│                                                              │
│   ⚠️ 核心表合同稳定，字段变更需大版本评审                      │
└────────────────────────┬─────────────────────────────────────┘
                         ↓ 2 号位 BOMDataLoader
┌─────────────────────────────────────────────────────────────┐
│ L2  APS 本地缓存层（APS 断网排程）                            │
│   APS_BOM_RAW + APS_BOM_STAGE_PATH_RAW                       │
│   （仅携带 L1 稳定字段，**不下沉** ERP 特征）                  │
└────────────────────────┬─────────────────────────────────────┘
                         ↓ ScheduleContext
                         1号位排程引擎
```

### 1.2 旁路视图（非防腐层）

`vw_MES_BOM_Stage_Enriched` 是 **ODS 内部便利视图**（不属于上述 3 层的任何一层），用于委外 ShippingTask 生成器、运维诊断、BI 报表。

**定位边界**：
- ✅ ODS 内部消费
- ❌ APS 排程内核禁用（含 ERP 特征字段，直接查会穿透防腐墙）
- ❌ APS 本地不做 `vw_APS_BOM_Stage_Enriched` 对称视图

### 1.4 BOM 入口解析分流（v1.5 新增 2026-05-08；v1.6 改用 MES_BOM_Edge_Active 2026-05-15；v1.7 SALES_ORDER前缀分流 2026-05-21）

v5.0.26 起，5 号位在 `sp_ExpandBOMBatch_vNext` 内部完成 BOM 入口分流（即 `#EntryResolved` 阶段），**数据来源改为 `MES_BOM_Edge_Active`（物化边表），禁止查 `MES_BOM_View`**：

```
-- #Request: 收集当前批次所有 MES_API_BOM_Request_Detail 行
--   字段：DetailId / BOMNO(可空) / MaterialCode / FactoryCode / OrderType

-- #EntryCandidates: 从 MES_BOM_Edge_Active 查入口候选（含多候选排名）
For each row in #Request:
  IF BOMNO IS NOT NULL:
    → 查 MES_BOM_Edge_Active WHERE BOMNO = r.BOMNO AND ParentMaterialCode = r.MaterialCode
      AND IsActive = 1
      ORDER BY IsDefaultVersion DESC, Id DESC → CandidateRank
  ELSE (BOMNO IS NULL OR BOMNO='0'):
    -- 按 OrderType + MaterialCode前缀 规则确定入口查找逻辑
    CASE 'PRODUCTION_INSTRUCTION' (原MTS/SS/SS_U):
      -- R31：查找逻辑同第二层，但必须写Issues（BOMNO缺失在PI类属异常）
      → 查 MES_BOM_Edge_Active WHERE ParentMaterialCode = r.MaterialCode AND IsActive = 1
        ORDER BY (SourceSystem='MES' → 优先), IsDefaultVersion DESC, Id DESC → CandidateRank
      → 无论是否找到候选，写 Issues(IssueType=BOMNO_MISSING_PRODUCTION,
          Severity=WARN(找到)/ERROR(未找到))

    CASE 'SALES_ORDER' (原SO/MTO):
      -- ⚠️ v1.7 新增：按MaterialCode前缀分流（R28/R29/R30）
      IF r.MaterialCode LIKE 'ASSY%':
        -- R28：通过出口库ProcessCode定位首层（见经验库§4.4 R28）
        Step1: 查 ProcessCodeDict WHERE FactoryCode=r.FactoryCode AND WarehouseRole='出口库' AND IsActive=1
               → @ExportProcCodes（取全部，可能多个）
        Step1补充: 若 @ExportProcCodes 为空（如CN6课无独立出口库）
               → @EffectiveFactory = 母体工厂（CN6课→CN，按R10/§2.1隶属关系）
               → 重新查 ProcessCodeDict WHERE FactoryCode=@EffectiveFactory AND WarehouseRole='出口库'
               否则 @EffectiveFactory = r.FactoryCode
        Step1最终兜底: 代理后 @ExportProcCodes 仍为空
               → 降级走R29路径（以 r.FactoryCode 为工厂过滤起点，见下方ELSE IF R29展开伪代码）
               → 不执行 Step2
        Step2: 查 MES_BOM_Edge_Active WHERE ParentProcRefCode IN @ExportProcCodes
                 AND ParentMaterialCode = r.MaterialCode AND IsActive = 1
               → CandidateRank 按 R17（@EffectiveFactory 为父件工厂起点，非订单原始FactoryCode）

      ELSE IF r.MaterialCode LIKE 'WIP%' OR r.MaterialCode LIKE 'RAW%':
        -- R29：全量查 + R17工厂过滤（务必同工厂匹配）
        Step1: 查 MES_BOM_Edge_Active WHERE ParentMaterialCode = r.MaterialCode AND IsActive = 1
               → @AllCandidates
        -- R30：全量查本身无结果且RAW前缀 → 外购件兜底（不写Issues）
        IF @AllCandidates 为空 AND r.MaterialCode LIKE 'RAW%':
          → ResolveStatus='PURCHASE'，跳过展开，不登记Issues
        -- R17 工厂过滤（适用于全量查有结果情况）
        Step2: FILTER @AllCandidates:
               EXISTS (SELECT 1 FROM ProcessCodeDict p
                       WHERE p.ProcessCode = e.ParentProcRefCode
                         AND p.FactoryCode = r.FactoryCode AND p.IsActive = 1)
               → @FactoryCandidates
        IF @FactoryCandidates 不为空:
          → CandidateRank 按 IsDefaultVersion DESC, Id DESC；R22并行生效
        ELSE:
          → 登记 Issues(IssueType=BOM_ENTRY_NOT_FOUND, Severity=ERROR)，不阻塞批次
          -- ⚠️ 注意：R17过滤后为空 ≠ R30，此处有BOM边但无匹配工厂的边，属ERP数据异常

      ELSE（其他前缀）:
        -- 原有逻辑
        → 查 MES_BOM_Edge_Active WHERE ParentMaterialCode = r.MaterialCode AND IsActive = 1
          ORDER BY IsDefaultVersion DESC, Id DESC → CandidateRank

    DEFAULT (OrderType IS NULL 或未知):
      → 同 SALES_ORDER 其他前缀规则；登记 Issues(IssueType=ORDER_TYPE_UNKNOWN, Severity=WARN)

-- #EntryResolved: 每个 DetailId 保留最优候选
--   携带：BOMNO(resolved) / EntryParent=MaterialCode / RootMaterialCode / RootFactoryCode
  ├ 找到候选（Rank=1）→ 进入 #WorksetRaw 展开
  ├ 多候选（R17过滤后仍多条）→ R22并行生效，登记 Issues(IssueType=BOM_ENTRY_AMBIGUOUS, Severity=WARN)
  ├ R30外购件路径        → 直接标 PURCHASE，不进入展开
  └ 未找到（非R30）      → 登记 Issues(IssueType=BOM_ENTRY_NOT_FOUND, Severity=ERROR)，跳过不阻塞批次
```

**⚠️ 分流归属红线**：
- BOM 入口解析内嵌在 **5 号位** `sp_ExpandBOMBatch_vNext` 的 Stage B/C（`#EntryCandidates`/`#EntryResolved`），禁止上移到 2 号位
- 2 号位只推送原始字段（`OrderStagingId`/`MaterialCode`/`FactoryCode`/`BOMNO?`），不做任何 BOM 查询
- ❌ 禁止查 `MES_BOM_View`（兼容视图，等价于读 `MES_BOM_Edge_Active`，但语义上是旧接口）；直读 `MES_BOM_Edge_Active`
- `BOM_ENTRY_NOT_FOUND` / `BOM_ENTRY_AMBIGUOUS` 登记 Issues，**不阻塞批次**（降级哲学）

---

### 1.3 视图 3 分类与命名规范

| 类别 | 命名 | 防腐等级 | ERP 升级时 |
|---|---|---|---|
| A. Socket-Plug 契约视图 | `ERP_*_View` / `MES_*_View` | **防腐入口** | 改这里 |
| B. 跨库包装视图 | `ext_*_View` | 防腐（跨库隔离）| 不改 |
| C. 派生便利视图 | `vw_*` | **非防腐** | 改 SELECT 列表 |

---

## 2. 字段映射

### 2.1 MES_BOM_Edge_Active → Workset（v1.6 更新：源契约改为物化边表）

| MES_BOM_Edge_Active（源契约）| MES_APS_BOM_Workset（L1 合同）| 填充时机 |
|---|---|---|
| BOMNO | BOMNO | 展开时 |
| ParentMaterialCode | ParentMaterialCode | 展开时 |
| ChildMaterialCode | ChildMaterialCode | 展开时 |
| Quantity | Quantity | 展开时 |
| ParentProcRefCode | ParentProcRefCode | 展开时（原样透传）|
| ChildProcRefCode | ChildProcRefCode | 展开时（原样透传）|
| ChildSourceHintCode | ChildSourceHintCode | 展开时（原样透传）|
| — | Level / Path | WHILE 迭代生成 |
| — | **ChildRequiredStageCode** | 5 号位**后置回填**（R05/R07/R17/R25/R26）|
| — | **ChildRequiredFactory** | 5 号位**后置回填**（R17 推导）|
| MES_API_BOM_Request_Detail.Id | **RequestDetailId** | 5 号位展开时从请求明细读取写入（v5.0.21）；nullable；非业务键；1 号位不消费 |

### 2.2 Produce 字段（ChildSourceHintCode）值域与 R17 映射

**v1.1 重要变更**：Produce → 工厂映射从硬编码 → **配置表驱动**，权威数据在 `ProduceToFactoryMap`（详见字段文档 §1.8）。

**业务含义简表**（v1.3 以用户照片为准重写；值域以 `ProduceToFactoryMap` 表数据为准）：

| Produce | 类别 | R17 推导工厂 | Strategy | ShouldDrilldown | CrossOrgHandoffFlag |
|---:|---|---|---|:---:|:---:|
| 0/2/3/4/10 | 购入（保税/课税，对 APS 透明）| NULL | NONE | 0 | 0 |
| 1 | 内制·自用（通用）| 继承父件工厂（R21 算法）| INHERIT | 1 | 0 |
| 5/8 | 内制·自用（CN 制造 6 课）| **CN6课** | FIXED | 1 | 0 |
| 9 | 内制·自用（SH）| **SH** | FIXED | 1 | 0 |
| **6** | **内制·他用（R20）** | **BJ** | **FIXED** | **1** | **1** |
| **7** | **内制·他用（R20）** | **CN** | **FIXED** | **1** | **1** |
| **11** | **内制·他用（R20）** | **TJ** | **FIXED** | **1** | **1** |

> 完整 12 行初始化数据见 `ProduceToFactoryMap`（配套 DDL v5.0.14）。
> ⚠️ v1.3 修复：原简表 “6/7/11 → BJ/TJ/SH” 为错误映射，已以照片权威为准纠正为 “6→BJ、7→CN、11→TJ”。

**R20 口径（v1.2 更新，三字段协同）**：
- `ShouldDrilldown=1`：**本厂仍继续下钻 BOM**，拿到下阶物料明细（用于成本核算、提前期估计、物料需求汇总）
- `CrossOrgHandoffFlag=1`：**打跨组织交接标签**，标记"该链归他用方工厂排产"
- `StageDetail.StageCode` = **目标工厂视角**（v1.2 新增约定）：父件 TJ + R20 指派 BJ → 直接写 `BJ_MACH`
- 三字段职责正交：
  - `ShouldDrilldown` = "BOM 展开控制"（**仅外购 R07 终止**）
  - `CrossOrgHandoffFlag` = "排产归属标签"（决定 Task 落在哪个工厂的产能队列）
  - `StageCode` 的工厂前缀 = "对接主键视角"（决定 1 号位去哪个工厂的 RoutingOperation 找小工序）
- **1 号位消费方（v1.2 调整）**：读到 `CrossOrgHandoffFlag=1`
  - 按 `StageDetail.StageCode` 去**他用方工厂**的 `RoutingOperation` 匹配 `(MaterialId, StageCode)` → 正常生成 Task
  - 该 Task 落在他用方工厂的产能队列，**本厂不占产能**
  - 本质：1 号位是统一排程器，同时为本厂和他用方工厂生成 Task；R20 标签只影响"Task 归属哪个工厂的队列"

**ERP 语义升级时**：直接 `UPDATE ProduceToFactoryMap ...`或 `INSERT` 新行，不改代码不改 DDL。

### 2.3 父件工厂推导（R21）

`parent_factory(GoodsProcCode, MaterialProcCode)` = 查 `工序对照表.代码所属工厂`，优先 `GoodsProcCode`（父件完成位），回退 `MaterialProcCode`（父件领料位）。

---

## 3. 5 号位回填流程（核心算法）

### 3.1 总流程

```
每条 Workset 行（父 pm → 子 cm，Produce=pr）：

1. IF pr ∈ {0,2,3,4,10}          # 外购
     ChildRequiredStageCode = NULL
     ChildRequiredFactory  = NULL
     ResolveStatus = 'PURCHASE'    # 记录在 StageDetail 的元数据（非 Workset 列）
     CONTINUE

2. inherit_factory = required_factory(pr, gpc, mpc)    # R17 映射
     pr=1   → parent_factory(gpc,mpc)
     pr=5/8 → 'CN6课'
     pr=6   → 'BJ'
     pr=7   → 'TJ'
     pr=9/11→ 'SH'

3. ch = material_stage_chain(cm, inherit_factory=inherit_factory)
     # 按 R24 原生序取 cm 作为父件的 BOM 边的工艺链
     # 按 R26 工厂过滤（匹配 `代码所属工厂`，受托关系不穿透）
     # 入库码（R02 IsInStock）跳过

4. IF ch 非空：   # OK
     ChildRequiredFactory   = inherit_factory
     CrossOrgHandoffFlag    = 查 ProduceToFactoryMap               # 0 or 1

     # v1.2：R20 跨组织视角统一 —— StageCode 写"目标工厂视角"
     effective_factory = inherit_factory       # R17/INHOUSE_SELF（含 1/5/8/9）
     IF CrossOrgHandoffFlag = 1:               # R20 Produce ∈ {6,7,11}
         effective_factory = target_factory_of_produce(pr)          # 6→BJ / 7→CN / 11→TJ（v1.3 照片权威纠正）

     # StageCode 需按 effective_factory 查 StageDict 得到"目标工厂前缀"的阶段码
     ChildRequiredStageCode = rebase_to_factory(ch[-1].StageCode, effective_factory)
     # 如：ch[-1] = TJ_MACH，effective_factory=BJ → rebase 后 = BJ_MACH

     写 StageDetail（EDGE，IsSupplyThreshold 最后一段=1，StageCode=目标工厂视角）
     CONTINUE

5. # ch 为空，进入 R27 容错分治（v1.1：全部降级 + 登记，不阻塞）
   IF cm 作为父件无任何 BOM 边：
     → LEAF：Issues Severity=INFO，DegradeAction='STAGE_NULL'
              Workset.ChildRequiredStageCode=NULL，ChildRequiredFactory=inherit_factory
              StageDetail 不写入该子件的 EDGE 行（无下阶即无边），由 1 号位保守策略兜底
              （注：StageScopeType 值域仅 EDGE/ROOT，"LEAF" 只作为 IssueType / DegradeAction 语义存在）
   ELSE：
     ch_all = material_stage_chain(cm, inherit_factory=None)   # 不过滤
     IF ch_all 为空：
       → NO_STAGE：Issues Severity=WARN，DegradeAction='STAGE_NULL'
                    Workset.ChildRequiredStageCode=NULL；1 号位保守策略兜底（批次继续）
     ELSE：
       IF 单 BOMNo 或单工厂：
         → FACTORY_MISMATCH：Issues Severity=WARN；保留 ch_all 作为回退链
       ELSE：
         → FACTORY_MISMATCH_MULTI：Issues Severity=WARN；保留 ch_all 作为回退链
       ChildRequiredStageCode = ch_all[-1].StageCode
       ChildRequiredFactory   = inherit_factory  # 仍写"应归"工厂，但标记 Issues
       写 StageDetail（含回退链）

6. 写 ROOT 路径（v5.0.8）：
   取 Level=1 的 ParentProcRefCode → 映射标准化阶段路径
   若多条不一致 → 取最长 + Issues Severity=WARN 记录
   写 StageDetail StageScopeType='ROOT'，ParentMaterialCode=NULL
```

### 3.2 R24 原生序规则

`material_stage_chain(cm)` 的实现关键：

- 按 cm 作为父件的每条 BOM 边读取 `(MaterialProcCode → GoodsProcCode)` 配对
- **不**用工艺名称（CN外协/CN机加等）做词典序重排
- 按 BOM 边的**原生顺序**拼接（领料位 → 完成位）
- 入库码（`IsInStock=1`）跳过，出口码同理

### 3.3 R25 异厂收敛规则

cm 作为父件出现在多个 BOMNo、不同工厂：

- Produce=1（继承）→ 按 R17 得到的 inherit_factory 精确匹配，**唯一路径**
- Produce ∈ {6,7,11}（R20 他用）→ **本厂仍继续下钻 BOM**（`ShouldDrilldown=1`）+ 打 `CrossOrgHandoffFlag=1`；ChildRequiredFactory 填 R17 目标工厂（**6→BJ、7→CN、11→TJ**，v1.3 照片权威纠正）；v1.2：StageDetail.StageCode 统一写**目标工厂视角**（如 BJ_MACH），1 号位按此 StageCode 直接去目标工厂 RoutingOperation 找小工序生成 Task，Task 落在目标工厂产能队列
- Produce=5/8/9（内制·自用，v1.3 从“特注”改名）→ 按对应实体精确匹配（CN6课 / SH）

### 3.4 R26 受托隔离

- 工艺过滤**按 `代码所属工厂`**（账面），**不按** `实际生产工厂（含受托）`
- 例：`370694` 账面=TJ，实际=BJ（受托 `070693`）——对 Produce=6（应 BJ）的子件**不保留**
- `实际生产工厂 / 受托process` 作为**元数据**仅在 `vw_MES_BOM_Stage_Enriched` 暴露

### 3.5 伪代码（Python 参考实现）

完整参考实现见 `d:\CascadeProjects\APS\_workset_excel.py`（`resolve_child_chain` 函数）；SQL 版实现放在 5 号位内部 SP。

### 3.6 BOM↔Routing 对接模型（v1.2 新增）

**三层分层模型**（见字段文档 §1.9b）：

| 层 | 字段 | 粒度 | 值域举例 | 是否参与 BOM↔Routing 对接 |
|---|---|---|---|---|
| 1. 具体工序 | `OperationCode` / `OperationName` | 执行粒度 | NC / MC / 切断 / 精修 | ❌ 只在 Routing 侧内部（按 RoutingDependency 串联） |
| 2. 辅助分类 | `ProcessType` | 报表粒度 | MACHINING / ASSEMBLY（`ProcessTypeDict`）| ❌ **完全不参与**；仅统计/粗分组 |
| 3. 大工艺 | `StageCode` | 对接粒度 | TJ_MACH / BJ_PAINT（`StageDict`）| ✅ **BOM↔Routing 对接主键之二** |

**对接主键 = `(MaterialCode, StageCode)` 二元组**

**1 号位统一排程流程**：

```
FOR each (MaterialCode, StageCode, StageSeq) IN StageDetail            # BOM 侧给出：经过哪些大工艺，顺序如何
    ORDER BY StageSeq:                                                  # StageSeq 是 BOM 权威顺序

    factory_of_task = StageCode 的工厂前缀                              # 如 BJ_MACH → BJ
      # R20 场景下 factory_of_task 是他用方工厂（视角已在 StageDetail 中统一）

    ops = SELECT * FROM RoutingOperation
          WHERE MaterialId = @MatId
            AND StageCode  = @StageCode                                # ← 对接主键
    
    IF ops 为空：
        # 外协阶段或 MES 工艺数据不完整
        查 StageLeadTimeParam 按参数化 LeadTime 生成单一 Task
    ELSE：
        FOR each op IN ops:
            生成 Task：
              - FactoryCode = factory_of_task（决定 Task 归属哪个工厂的产能队列）
              - OperationCode / OperationName
              - StandardDuration + SetupTime
            按 RoutingDependency 决定小工序之间的串并行
```

**R20 场景下的一致性保证**：
- `StageDetail.StageCode` 采用目标工厂视角 → `factory_of_task` 自然是他用方工厂 → Task 自动落在他用方工厂产能队列
- 上述流程**无需显式判断** `CrossOrgHandoffFlag`；视角统一后分支消失
- `CrossOrgHandoffFlag=1` 仅作为**旁路标签**供审计、报表、Issues 登记使用

---

## 4. 错误处理方案（R27 扩展）

### 4.1 错误分类与处置矩阵

**v1.1 重写：所有 IssueType 均"降级 + 登记"，永不阻塞批次**

| IssueType | Severity | 触发条件 | DegradeAction | 降级行为 |
|---|---|---|---|---|
| `LEAF` | INFO | Produce 声明内制但物料无下阶 BOM | `STAGE_NULL` | `ChildRequiredStageCode=NULL`；1号位保守策略兜底 |
| `FACTORY_MISMATCH` | WARN | Produce 厂 ≠ BOM 实际厂（单厂）| `FACTORY_FALLBACK` | 保留 BOM 原生链；ChildRequiredFactory 仍填 R17 值 |
| `FACTORY_MISMATCH_MULTI` | WARN | 跨多厂 | `FACTORY_FALLBACK` | 同上；建议优先复核 |
| `NO_STAGE` | **WARN** | 有 BOM 但全为入库/出口码，无大工艺段 | `STAGE_NULL` | 1号位保守策略兜底。**v1.0→v1.1：ERROR→WARN** |
| `UNKNOWN_PROCCODE` | **WARN** | 工序码查不到字典 | `STAGE_NULL` / 部分链 | 未知码跳过，可识别部分正常推导。**v1.0→v1.1：ERROR→WARN** |
| `QUANTITY_INVALID` | **WARN** | Quantity ≤ 0 或 NULL | `QTY_DEFAULT_1` | 按 1 兜底写入。**v1.0→v1.1：ERROR→WARN** |
| `MISSING_PRODUCE` | WARN | Produce 为空 | `PRODUCE_DEFAULT_1` | 按 Produce=1 兜底 |
| `CYCLIC_BOM` | **ERROR** | BOM 环路 | `CYCLE_SKIP` | **首次访问保留 + 重复循环跳过**（CTE 的 `Path NOT LIKE '%Child%'`）。**v1.0→v1.1：CRITICAL→ERROR** |
| `EXPAND_FAILED` | CRITICAL | 单个 BOMNo 展开 SP 抛异常 | `BOMNO_SKIP` | try-catch 捕获；其他 BOMNo 正常继续 |

**⚠️ v1.1 核心原则**：批次状态机永远走 READY；`FAILED` 仅保留给 SP 进程崩溃。

### 4.2 Issues 表登记规范

必填：`BatchNo, BOMNO, ChildMaterialCode, IssueType, Severity, Detail`  
建议填：`ParentMaterialCode, Produce, ExpectedFactory, ActualFactory, RawRefJson`

**`RawRefJson`** 设计：登记触发时的 ERP 原始字段快照（含 `produce`, `GoodsProcCode`, `MaterialProcCode`, 其他相关 ERP 字段），格式：

```json
{
  "produce": "11",
  "GoodsProcCode": "310499",
  "MaterialProcCode": "310498",
  "BOMNo": "102407056",
  "erp_fields_version": "2026-04-23"
}
```

**防腐价值**：ERP 升级改字段名时，只改登记时的映射逻辑；Issues 表本身结构不变，历史记录依然可读。

### 4.3 降级数据追溯查询（v1.1 替代原放行校验）

批次永不阻塞，降级行为必须**可见且可追溯**。1 号位/业务查询 Workset 时，推荐使用以下 SQL 查"哪些子件走了降级路径"：

```sql
-- 查询某批次中走了降级路径的子件
SELECT w.BOMNO, w.ChildMaterialCode,
       w.ChildRequiredStageCode, w.ChildRequiredFactory,
       i.IssueType, i.Severity, i.DegradeAction, i.Detail
FROM MES_APS_BOM_Workset w
JOIN MES_APS_BOM_Workset_Issues i
  ON w.BatchNo = i.BatchNo AND w.BOMNO = i.BOMNO
 AND w.ChildMaterialCode = i.ChildMaterialCode
WHERE w.BatchNo = @BatchNo
  AND i.Severity IN ('WARN','ERROR','CRITICAL');
```

排程结果的"哪些依赖了降级数据"完全可追溯，保障业务事后可评估修复优先级。

### 4.4 业务复核与 ERP 源端修复闭环（v1.1 重写，无值班）

```
┌──────────────────────┐
│ Issues 持续登记       │← 每批次写入降级记录（批次本身照常 READY）
└──────────┬───────────┘
           ↓
┌──────────────────────┐
│ 月度巡检              │ 业务复核人员统计过去 30 天 WARN/ERROR
│                      │ 识别高频 IssueType + 高频物料
└──────────┬───────────┘
           ↓
┌──────────────────────┐
│ 反馈 ERP 维护方       │ 批量修正源头（如系统性 Produce 录错）
│ ＋更新 ReviewStatus   │ CONFIRMED / IGNORED / FIXED
└──────────┬───────────┘
           ↓
┌──────────────────────┐
│ 下次批次自动减少      │ ERP 改完后次日批次 Issues 自动少/消失
└──────────────────────┘
```

**关键特点**：
- **无人值班需求**：批次不阻塞，无需半夜响应
- **数据驱动改进**：Issues 表作为 ERP 数据质量镜子
- **系统性问题可见**：某型号反复出 FACTORY_MISMATCH → ERP 源头系统性错误

### 4.5 运营 SLA（v1.1 新增）

| Severity | 响应机制 | 承接人 | 使用场景 |
|---|---|---|---|
| INFO | 不关注，BI 报表汇总 | — | LEAF 终止，正常现象 |
| WARN | **月度巡检**；累积高频反馈 ERP | **业务复核人员** / 0 号位 | FACTORY_MISMATCH / NO_STAGE / QTY_INVALID 等 |
| ERROR | **次日晨会**过一遍 | 业务复核人员 + 5 号位 | CYCLIC_BOM（知道哪些型号有环）|
| CRITICAL | **追责 SP 本身**（不是数据问题）| 5 号位 / DBA | EXPAND_FAILED = 展开程序有问题 |

**运营原则**：
- 业务复核人员通过 Issues 表观察 ERP 数据质量趋势，系统性问题 → 集中反馈 ERP 维护方
- ERP 源头修复后次日批次 Issues 自动减少，无需在 APS 侧手动干预

### 4.6 监控指标

建议运维每日查询：

```sql
-- 各严重等级的 PENDING 数量（昨日）
SELECT Severity, COUNT(*) AS Cnt
FROM MES_APS_BOM_Workset_Issues
WHERE ReviewStatus='PENDING' AND CreatedAt >= DATEADD(DAY,-1,GETDATE())
GROUP BY Severity;

-- WARN 级重复出现的 Top 型号（可能是系统性 ERP 数据问题）
SELECT ChildMaterialCode, IssueType, COUNT(*) AS Cnt
FROM MES_APS_BOM_Workset_Issues
WHERE Severity='WARN' AND CreatedAt >= DATEADD(DAY,-30,GETDATE())
GROUP BY ChildMaterialCode, IssueType
HAVING COUNT(*) >= 5
ORDER BY Cnt DESC;

-- 每批次 DegradeAction 分布（监控降级行为的规模）
SELECT BatchNo, DegradeAction, COUNT(*) AS Cnt
FROM MES_APS_BOM_Workset_Issues
WHERE CreatedAt >= DATEADD(DAY,-1,GETDATE())
  AND DegradeAction IS NOT NULL
GROUP BY BatchNo, DegradeAction
ORDER BY BatchNo, Cnt DESC;
```

---

## 5. ERP 升级适配指南

### 5.1 字段变化吸收点（**仅这些地方改，APS 核心不动**）

| ERP 变化类型 | 改动点 | 不影响 |
|---|---|---|
| `produce` 字段改名 / 扩值域 | `MES_BOM_Source_View` 的 SELECT 列别名（吸震层）；R17 映射表 | Workset 结构、`ChildRequiredFactory` 枚举 |
| 工序码编码体系升级（如 6 位 → 8 位）| `vw_MES_BOM_Stage_Enriched`、工序对照字典；5号位 `material_stage_chain` 算法 | Workset、StageDetail 表结构 |
| 受托关系重构（如拆独立表）| `vw_MES_BOM_Stage_Enriched` 的 JOIN 逻辑；委外 ShippingTask 生成器 | APS 排程代码（本就不查 vw_*）|
| BOM 表拆分 / 新增字段 | `MES_BOM_Source_View` 的 FROM/SELECT（吸震层）；`MES_BOM_Edge_Active` 刷新后自动更新 | 下游所有 |

### 5.2 ERP 升级测试回归清单

1. ✅ `MES_BOM_Source_View` 能暴露 9+2 必需字段并刷新到 `MES_BOM_Edge_Active`（见字段说明文档 §4.1）
2. ✅ Produce 值域仍能覆盖 0-11（或 5 号位 R17 映射表扩展）
3. ✅ 工序对照字典的"代码所属工厂"维度保留（即使字段名改）
4. ✅ 新旧字典并存期：5 号位映射逻辑兼容处理
5. ✅ 跑一批历史样本 BOM，Workset/StageDetail 结果与旧版一致（除非业务有意变更）
6. ✅ Issues 表 `RawRefJson` 格式适配新 ERP 字段名

---

## 7. StageCode 全局字典（v1.1 新增）

### 7.1 定位

StageCode 是BOM 大工艺阶段码的**唯一权威字典**，物理宝体=**`StageDict`** 表（详见字段文档 §1.9）。

### 7.2 命名规范（方案 B）

- **格式**：`{工厂}_{阶段类别}`
- **示例**：`CN_MACH` / `TJ_OUTS` / `JP_ASSY` / `CN6_MACH`
- **理由**：StageCode 单字段就锺定工厂+阶段维度，符合经验库中"CN机加→CN外协"的业务语言习惯

### 7.3 引用源

`StageDict.StageCode` 被以下表/字段引用：

| 表/视图 | 字段 | 用途 |
|---|---|---|
| `RoutingStage` | `StageCode` | 物料级阶段配置 |
| `MES_APS_BOM_Workset_StageDetail` | `StageCode` | BOM 派生阶段链 |
| `APS_BOM_STAGE_PATH_RAW` | `StageCode` | APS 本地缓存 |
| `StageLeadTimeParam` | `StageCode` | 阶段提前期参数 |
| `MES_APS_BOM_Workset.ChildRequiredStageCode` | 值 | 5 号位回填结果 |

### 7.4 扩展策略

- 新增阶段 → `INSERT INTO StageDict` 一行，不改 DDL/代码
- 废弃阶段 → `UPDATE StageDict SET IsActive=0`，历史数据保留
- 外键约束：当前**不强制**（避免展开时引用未注册 StageCode 导致整批失败，而是触发 `UNKNOWN_PROCCODE` Issue 降级）

### 7.5 与工序字典的关系

- `StageDict` = **业务管理级**大工艺阶段字典（APS 自主维护）
- 工序对照表 = **工序级**具体工序码字典（ERP/MES 维护）
- 两者通过 5 号位的 `material_stage_chain` 逻辑建立关联：从工序码推导出 `StageCode`

---

## 6. 案例附录（经验库引用）

本方案基于 7 个复杂样本案例验证，完整报告见 `d:\CascadeProjects\APS\_complex_reports\`：

| 案例 | 验证规则 | 关键现象 |
|---|---|---|
| `CQ2B32-10+10DCZ-XC11` | R01~R08 | 基础展开、外购终止、跨厂运输 |
| `C2Q50RBAM863-040` | R17 判例 | Produce=1 继承父件 CN，CN6/SH 候选被排除 |
| `MGG40-40-S2403A` | R24 | CN外协→CN机加 的原生序（反 STAGE_ORDER 硬排序）|
| `MLGPL50-100Z-F` | R19/R20 | ASSY 品库 + 内制他用跨厂标记 |
| `MGGMB40-450` | R25 / R26 | 异厂替代路径收敛 + 受托关系不穿透 |
| 全量 183 个 rootModel | R27 | 49 条⚠无链分治：17 LEAF + 32 FACTORY_MISMATCH |

---

## 附录 A：索引快查

| 术语 | 归属 |
|---|---|
| 设计哲学（防腐只吸震不准入）| §0 |
| L0 / L1 / L2 分层 | §1.1 |
| Produce 0-11 值域 | §2.2 |
| R17 工厂映射 （ProduceToFactoryMap）| §2.2 / §3 / 字段文档§1.8 |
| R24 原生序 | §3.2 |
| R25 异厂收敛 | §3.3 |
| R26 受托隔离 | §3.4 |
| R27 错误分治 | §3.1 / §4.1 |
| 9 类错误与降级矩阵 | §4.1 |
| 降级追溯查询 | §4.3 |
| 复核闭环（无值班）| §4.4 |
| 运营 SLA | §4.5 |
| 监控 SQL | §4.6 |
| StageCode 全局字典 | §7 ＋ 字段文档§1.9 |
| ERP 升级适配 | §5 |
| 稳定合同原则 | §1.1 |
| 视图 3 分类 | §1.3 |

---

**变更历史**：
- v1.5（2026-05-08）：BOM入口解析分流（§1.4）+ RequestDetailId追溯锚点（§2.1）+ MES_API_BOM_Request_Detail新结构对齐（DDL v5.0.21）
- v1.0（2026-04-23）：初版发布，基于经验库 R01~R27 + DDL v5.0.10
- v1.1（2026-04-24）：规则资产化·批次永不阻塞·StageCode 全局字典
  - 新增设计哲学声明：防腐层只吸震不准入
  - 降级矩阵重写：所有 IssueType 均"降级 + 登记"策略
  - CYCLIC_BOM 策略：首次保留 + 重复循环跳过；Severity CRITICAL→ERROR
  - Produce 映射 → `ProduceToFactoryMap` 配置表（字段文档 §1.8）
  - 新增 §7 StageCode 全局字典 → `StageDict`（字段文档 §1.9）
  - 运营 SLA 新增：月度巡检 + ERP 源端修复闭环

**下一步行动**：
1. 待用户评审此文档，修正或补充
2. 5 号位依据此方案实现 SQL 版 `sp_EnrichBOMWorkset`（回填 SP）
3. 2 号位依据此方案在 ODS 建 `vw_MES_BOM_Stage_Enriched`
4. 0 号位审定 `StageDict` 初始化数据，补充实际工厂/阶段组合
5. 运维/业务就 Issues 月度巡检工具（可能是 APS Portal 的一个模块）提需求

---

## 8. 回归测试包与验收方法（2026-05-15 新增）

### 8.1 测试目标

验证 SQL 版 `sp_ExpandBOMBatch_vNext` + `sp_EnrichBOMWorkset` 是否能复现 Python 黄金结果。

黄金结果来源：`_workset_excel.py` 基于 `order.xlsx` + `工序对照表.xlsx` 生成的 `_APS_Workset_ALL.xlsx`，包含 183 个 rootModel、5974 条 BOM 边、6337 行 StageDetail、49 条 Issues。

### 8.2 测试文件

| 文件 | 说明 |
|---|---|
| `_regression_seed.py` | 读取 `order.xlsx`，生成 `_regression_seed.sql`（5974 行 BOM 边 INSERT，12 批次） |
| `_regression_seed.sql` | 向 `MES_BOM_Edge_Active` 写入样本 BOM 边（`RefreshBatchNo='REGTEST_20260515'`） |
| `_regression_run.sql` | 构造 ERP_Order_Staging mock 行 + Request + Detail，执行 `sp_ExpandBOMBatch_vNext` + `sp_EnrichBOMWorkset`，输出行数统计 |
| `_regression_check.sql` | 执行 Workset / StageDetail / Issues 校验，包含 Check 3/4/5 断言查询 |

### 8.3 执行顺序

```powershell
# 步骤 0（可选）：如需重新生成种子 SQL
python _regression_seed.py

# 步骤 1：写入 BOM 边样本数据
sqlcmd -S <server> -d <db> -i _regression_seed.sql

# 步骤 2：构造请求并执行 SP
sqlcmd -S <server> -d <db> -i _regression_run.sql

# 步骤 3：执行校验断言
sqlcmd -S <server> -d <db> -i _regression_check.sql
```

### 8.4 验收标准

| 检查项 | 断言 | 来源 |
|---|---|---|
| **Check 3a** Workset 行数 | 接近 5974（差异 < 5%） | Golden Reference |
| **Check 3b** StageDetail EDGE WorksetId IS NULL | = 0（每行必须有 WorksetId） | Issue 1/2 修复验证 |
| **Check 3c** Issues 类型覆盖 | FACTORY_MISMATCH ≥ 32，LEAF ≥ 17 | Golden Reference |
| **Check 4** Produce 驱动的多链 ChildMaterialCode | ≥ 6 个（含 `LEF16-2-10-150-1`、`P3800312` 等） | Python 分析确认 |
| **Check 5** Realtime BOMNO mismatch | = 0（`Request_Realtime.BOMNO` = `Workset_Realtime.BOMNO`） | R4 修复验证 |

> **Check 4 说明**：Python Golden Reference 中确认 6 个 ChildMaterialCode 在不同 `Produce` 下出现不同阶段链（Produce=0 外购→无链，Produce=1/6 内制→有链），SQL SP 须正确复现此行为。

> **StageDetail 行数说明**：SQL 版 StageDetail 行数会**大于** 6337，因为 `WorksetId` 已改为每 Workset 行独立写入（不再按 BOM 边去重）。这是正确行为，不是 Bug。

### 8.5 注意事项

- **仅允许在测试库执行，不得在生产库执行。**
- 测试数据以 `RefreshBatchNo='REGTEST_20260515'`、`BatchNo='REGTEST_20260515'`、`SourceSystem='REGTEST'` 标识，可通过这三个字段完整清除。
- Realtime Check 5 需手动触发一次 `sp_ExpandBOMRealtime_vNext`；`_regression_check.sql` 末尾已提供注释模板，取消注释后填入任意 rootModel 物料编码即可执行。
