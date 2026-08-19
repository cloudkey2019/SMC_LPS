# BOM 展开经验库

> **累积性文档**：每新增一个示例案例，将新规则、新反例、新结论合并进本文档。  
> **首次建立**：2026-04-21（源自案例1 `CQ2B32-10+10DCZ-XC11` 的业务对齐）  
> **版本基线**：v5.0.8 表结构（`MES_APS_BOM_Workset` + `MES_APS_BOM_Workset_StageDetail`），保持不动  
> **v5.0.16 备注**（2026-04-29，与本经验库无影响）：APS_Production 库新增的 `ProductionDepartment` / `MaterialStageDeptContext` / `MaterialStageDeptOverride` 三张表与本库 BOM 展开规则**完全解耦**——BOM 展开仍然只产出 `(MaterialId, StageCode)` 维度的 StageDetail，部门维度由 2 号位 `sp_RebuildMaterialStageDeptContext` 后续组装；本库 R01~R27 规则**零修改**。  
> **维护原则**：
> - 新规则入库前必须经业务方确认，确认前一律放到第 6 章"待验证清单"
> - 任何动 DDL / 加表 / 写代码的决定都在本库充分积累后再评审

---

## 0. 文档地图

| 章节 | 内容 |
|------|------|
| 1. 规则索引 | 已确认规则的速查表 |
| 2. 组织实体 | 工厂/公司/部门的业务定位与APS归并 |
| 3. 字段字典 | ERP 原始字段的真实语义 |
| 4. BOM展开规则 | 下钻终止、工艺链推导、跨厂识别 |
| 5. 案例库 | 每个示例数据一份条目 |
| 6. 待验证清单 | 未定论的规则和业务问题 |
| 7. 纠错记录 | 理解偏差的历史教训 |

---

## 1. 规则索引（已确认）

| ID | 规则简述 | 出处 | 确认日期 |
|----|---------|------|---------|
| **R01** | 每行BOM = `GoodsModel` 在工艺链中经历的一段：在 `MaterialProcCode` 这道工艺中消耗 `MaterialModel`，GoodsModel 最终完成于 `GoodsProcCode` 位置。因此 **GoodsModel 的工艺链必然包含 MaterialProcCode 这道大工艺** | 案例1 | 2026-04-20 |
| **R02** | `ProcessCode` 三类型：**大工艺码**（独立工艺）、**入库码**（上游大工艺的尾巴，不算独立工艺）、**出口码**（销售库存节点，不算大工艺）。按 `工序对照表` 的 `Process` 字段字面判定 | 案例1 | 2026-04-20 |
| **R03** | 数量用 `RequestQty`（含损耗），**不要用** `MaterialQty`（标准用量） | 案例1 | 2026-04-20 |
| **R04** | `Produce` 字段分两大类：外购（0/2/3/4/10）和内制（1/5/6/7/8/9/11）。完整定义见 §3.1 | 案例1 + Produce说明表 | 2026-04-21 |
| **R05** | `ChildRequiredStageCode` 语义 = **子件自身完工的最后一道大工艺**（入库尾巴不计）。**不是** 父件消耗子件时父件所处的工艺 | 案例1 | 2026-04-20 |
| **R06** | EDGE 记录的是**子件自身的工艺链**，**不穿透**展开到子件的子件 | 案例1 | 2026-04-20 |
| **R07** | 外购件（`Produce ∈ {0,2,3,4,10}`）BOM 下钻到此为止，不再往下展开 | 案例1 | 2026-04-20 |
| **R08** | **任何跨厂的物理运输都必须生成 ShippingTask / 计入运输LT**，不论是否跨大工艺（即便业务上算"同一道工艺"） | 案例1 | 2026-04-20 |
| **R09** | `CN/BJ/TJ/SH` 是制造公司代号，**不绑定地理位置**（不等于常州/北京/天津/上海的地理概念） | Produce说明表 | 2026-04-21 |
| **R10** | `CN制造6课` 和 `SH上海公司` 是**特注品生产部门**，在APS中作为**独立工厂实体**与 `CN/BJ/TJ` **并列**对待；`CN` 下其他课是量产部门，归并为 CN 实体本身 | 用户澄清 | 2026-04-21 |
| **R11** | `Produce=4` 和 `Produce=10` 都是"海外课税"，业务上通过**适用范围不同**区分（具体区分维度属业务部门定义，APS 只需保留此差异） | 用户澄清 | 2026-04-21 |
| **R12** | 所有 `ProcessCode`（含 `GoodsProcCode` / `MaterialProcCode` / `KogoProcessCode`）**固定6位**。样本中看到的5位值是 Excel/CSV 把数字型列的**前导0丢失**导致，APS 摄入时必须左补0到6位后再匹配 `工序对照表` | 用户澄清（案例4） | 2026-04-22 |
| **R13** | BOM 对"完工后入库"环节的标注具有 **工艺类型差异化省略**规律：<br>① **机加完工**：BOM **会**明示入库仓库（`*98` 入库码，作为 `GoodsProcCode`）<br>② **涂装/氧化等表面完工**：BOM **不**明示入库仓库（但物理上仍入正规库）<br>③ 差异化省略是 ERP 的既定规约，非数据缺失 | 用户澄清（案例4） | 2026-04-22 |
| **R14** | **追加工件**的语义：父件 = 追加工产出件；子件 = 追加工前的基件。基件已完成其自身完整工艺链并入了正规库（可能是 `*98` 入库或表面后隐藏入库），然后**为了追加工被重新取出**进入**追加工现场库**——这是**不连续**的物料动作（已入库→再出库）。<br>⚠️ **追加工现场库的识别必须查 `工序对照表.是否追加工='是'`**，**不能靠 ProcessCode 尾号** `*96` 猜测；同为 `*96` 尾号可能是非追加工的"ASSY品库"（如 `510496`），也可能是追加工现场库（如 `010496`） | 用户澄清（案例1难点说明 + 案例4 + 案例5修订） | 2026-04-22 |
| **R15** | 同一父件在 **同一 `BOMNo`** 下可以出现**多条 BOM 记录**，这些记录的 `MaterialProcCode` 可能不同（典型：**课税仓 + 保税仓并行**，如 `510789` 课税组装 + `510799` 保税组装）。**这是常态**，不限于特注品。父件的该段工艺 = **并行多仓消耗**，APS 摄入时需保留全部记录，不能去重 | 用户澄清（案例4） | 2026-04-22 |
| **R16** | 同一父件在 **不同 `BOMNo`** 下出现多条"BOM头"，**不是多版本二选一**，而是**多道工序段并行**，每个 `BOMNo` 对应一段工序的"消耗+完工"，父件生产**要求全部 `BOMNo` 都生效**（各消耗各自的零件）。<br>⚠️ 之前"多BOM选择规则 RC-01"的二选一理解**已作废**，见 §7 纠错 #12 | 用户澄清（案例4） | 2026-04-22 |
| **R17** | **BOMNo 候选集的工厂筛除**（R16 的前置步骤）：<br>对子件展开时若有多条候选 `BOMNo`，先按工厂精确匹配筛除：<br>① 子件在本上下文中的**应归属工厂** = 由 `Produce` + 上游父件所属工厂推出（例：`Produce=1` = "继承父件工厂"，但**不含 CN6/SH** 等特注实体；`Produce=5/8` = CN6；`Produce=9` = SH；`Produce=6/7/11` = 对应他用方工厂）<br>② 候选 `BOMNo` 的 `GoodsProcCode` **所属工厂必须严格等于** ①<br>③ 不等者全部排除；剩余的按 R16 并行生效<br>判例（案例2）：`C2Q50RBAM863-040` Produce=1、父件 CN → 应归属 CN（不含 CN6）→ 4 条候选中 `GoodsProcCode` 属 CN6 / SH 的 3 条被排除，仅保留 `102602061`（`510498` 属 CN） | 用户澄清（案例2 判例） | 2026-04-22 |
| **R18** | `WBM-*` 前缀的物料是**同本体的中间阶段产物**——与同名去除 `WBM-` 前缀的母件是同一物件，处于"机加完→表面前"之类的中间工艺节点。BOM 中用独立物料号表示，以便挂接下阶工艺 | 用户确认（案例3） | 2026-04-22 |
| **R19** | **组装大工艺内部的两种库位角色**：<br>① **组装现场库**（如 `510799`）= 组装工艺的**领料位**，上游父件从这里消耗散件供自己组装<br>② **ASSY 品库**（如 `510496`）= 组装工艺的**完成位**（sub-ASSY 完工存放处），**上游父件 `MaterialProcCode` 指向此库 即表示消耗的是一个已完成的 sub-ASSY**<br>识别方式：查 `工序对照表.仓库角色`（`现场库` / `ASSY品库`）而非尾号推断。sub-ASSY 在 BOM 中体现为"父件 `GoodsProcCode` = ASSY品库"的单独 BOMNo 展开链 | 用户确认（案例5） | 2026-04-22 |
| **R20** | **内制他用（`Produce ∈ {6, 7, 11}`）子件的 BOM 下钻策略 = 终止下钻 + 打跨厂跨域标记**：<br>① 此类子件**不继续递归展开 BOM**（BOM 边记录到此子件为止，不追其下层零件）<br>② 必须输出**显式标记** `CROSS_ORG_HANDOFF`（或等价语义字段），表示此子件由**跨厂跨域出荷流程**交由他用方工厂自行排产，本上下文的 APS 展开在此节点"收束"<br>③ 与 R07（外购件终止）**不同标记**：R07 → `PURCHASE`；R20 → `CROSS_ORG_HANDOFF`（便于下游排程区分"买进来"和"由关联工厂送过来"） | 用户澄清 | 2026-04-22 |
| **R21** | **产品根工厂的推导**（R17 所需"上游工厂"传递链的起点）：<br>产品根的**所属工厂** = 产品根 `GoodsProcCode` 在 `工序对照表.代码所属工厂` 列的值<br>（例：根 `GoodsProcCode=980201` → `代码所属工厂=CN` → 根工厂=CN）<br>向下展开时，每一层子件的"应归属工厂"按 R17 用 `Produce` 推出，再作为下一层的"上游工厂" | 用户澄清 | 2026-04-22 |
| **R22** | **R17 筛除后若仍有多条 `BOMNo`**：各条**独立生效**——**前提：父件型号相同 且 父件 `ParentProcRefCode`（GoodsProcCode）也相同**，子件型号不同时，是**同时消耗多种子件**的并行结构，不是重复记录。APS 摄入时保留全部，各自生成工序任务并消耗各自子件（与 R16 "多工序段并行" 一致）。⚠️ `ParentProcRefCode` 不同的多套 BOMNO **不满足 R22 前提**，不得判定为并行（属于不同工序段视角的 BOM，需经 R17/R25/R32 进一步收敛）。 | 用户正式确认 + 2026-05-27 补充 | 2026-04-22 |
| **R23** | **展平 BOM 边表中"完全相同的边"出现多次 = 多路径汇聚，必须保留**：<br>当 BOM 被展平为 `(父件, 子件, 工艺码, Lev)` 边表（如 `order.xlsx`）时，同一 `rootModel` 组内若出现**完全相同的边**（6字段全等：`GoodsProcCode / GoodsModel / MaterialProcCode / MaterialModel / Produce / Lev`）**多次**，含义是该子件被**多个不同上游父件**在同层次同时消耗——**不是**数据冗余，**不能去重**。保留全部行用于后续用量倍数汇总 | 用户确认（order.xlsx 批量） | 2026-04-22 |
| **R24** | **物料工艺链的原生序规则**（推翻 v1 的 STAGE_ORDER 硬排序）：<br>同一物料的工艺链**按 BOM 边上 `MaterialProcCode → GoodsProcCode`（领料位 → 完成位）的原生配对顺序**拼接，**不得**用工艺名称（CN外协/CN机加等）做词典序硬排序。<br>典型反例：`MGG40-40-S2403A` 单条边 `MaterialProc=513499(CN机加) / GoodsProc=513498(CN外协发货库)` 真实顺序 = **CN机加 → CN外协**（本厂机加后料送社外）；若按"CN外协 在 CN机加 之前"的 STAGE_ORDER 硬排序 → 错误输出"CN外协 → CN机加"。<br>外协↔机加、表面↔机加 等相对次序**只能由数据本身决定**，规则代码不做名称层面的重排 | 案例6 修订（MGGMB40-450） | 2026-04-23 |
| **R25** | **异厂替代路径的收敛规则**（区分 R16 并行 vs 多厂可替代）：<br>同一物料作为父件出现在**多个 BOMNo**，若这些 BOMNo 的 `GoodsProcCode / MaterialProcCode` **分布在不同工厂**（如 `C1G40B-AH114A450` 的 CN 路径 `101302051` 与 SH 路径 `302302061`），**不是 R16 多段并行**，而是**可替代路线**。<br>收敛策略：<br>① **Produce=1（继承父件工厂）**：按 R17/R21 取父件工厂 = 领料位 `MaterialProcCode.代码所属工厂`，仅保留匹配的 BOMNo（唯一），其余排除<br>② **Produce ∈ {6,7,11}（R20 内制他用）**：保留所有路径，交人工/下游他用方选择<br>③ **Produce=5/8/9（特注 CN6/SH）**：按对应实体精确匹配<br>与 R16（多段并行）的区分：R16 的多 BOMNo **工厂相同、工序段不同**（如 ASSY 子展开 BOMNo + 本层组装 BOMNo）；R25 的多 BOMNo **工厂不同、工序段等价**（都是机加入库 + 机加现场库） | 案例6 新增（MGGMB40-450） | 2026-04-23 |
| **R26** | **工艺链工厂过滤按"代码所属工厂"维度，受托关系不穿透**：<br>`material_stage_chain` 按 Produce→应归属工厂过滤时，**匹配 `工序对照表.代码所属工厂`（账面归属）**，而不是 `实际生产（含受托）工厂`。<br>典型：`370694`（描述="TJ_6课_长尺外协库" / 代码所属=TJ / 实际生产=BJ / 受托process=070693）——对 Produce=6（应 BJ）的子件**不保留**此段（账面属 TJ，是"TJ 委托 BJ 外协"语义，不是"BJ 自己做"）。<br>`实际生产（含受托）工厂` 与 `受托process` 字段作为**元数据**在 `StageDetail.ActualFactory / TrusteeProcCode` 列展示，用于后续生成**委外发货 ShippingTask**，但**不影响**工艺链的归属过滤 | 案例7 新增（全量 Excel） | 2026-04-23 |
| **R27** | **⚠无链的两类成因分治**（Produce 与 BOM 数据不一致的容错）：<br>① `LEAF`：Produce 声明内制但物料**作为父件无任何 BOM 边** → 按**终止节点**处理（等同外购件的语义），`ChildRequiredStageName='LEAF终止'`；Workset 标 `R27_LEAF`；Issues 记 `leaf_no_bom`<br>② `FACTORY_MISMATCH`：物料有下阶 BOM 但**全部边在非 Produce 声明厂**（典型：Produce=11 应 SH，但 BOM 只有 TJ 段）→ **保留 BOM 原生链**（不过滤 inherit_factory 的回退），Workset 标 `R27_FACTORY_MISMATCH`；Issues 记 `produce_factory_mismatch` 并列出 `声明厂 / 实际厂 / BOMNo` 供业务复核 ERP Produce 是否录错<br>③ `FACTORY_MISMATCH_MULTI`：同 ② 但 BOM 跨多厂（需要业务优先复核）<br>判定顺序：先按 R17/R25 严格过滤；命中 → OK；否则按 ①/② 判；**回退策略不阻断主流程** | 案例7 新增 | 2026-04-23 |
| **R28** | **BOMNO为空/0时，ASSY前缀订单的首层BOM入口规则**：仅适用于 `MaterialCode` 以 `ASSY` 开头 + `OrderType=SALES_ORDER` + `BOMNO IS NULL OR BOMNO='0'`。Step1：查 `ProcessCodeDict` WHERE `FactoryCode`=订单所属工厂 AND `WarehouseRole`='出口库' AND `IsActive`=1 → 取**所有**匹配（一厂可能多个，全部取）。**Step1补充（无出口库的子工厂）**：若Step1结果为空，说明该工厂无独立出口库（当前仅CN6课有此情况）；按R10/§2.1的工厂隶属关系取**母体工厂**（CN6课→CN）的出口库ProcessCode代入，记 `@EffectiveFactory`=母体工厂；否则 `@EffectiveFactory`=订单工厂。Step2：查 `MES_BOM_Edge_Active` WHERE `ParentProcRefCode` IN（Step1结果集）AND `ParentMaterialCode`=订单`MaterialCode` AND `IsActive`=1 → 得首层BOMNO候选集。多BOMNO处理套用R17（**以`@EffectiveFactory`为父件工厂起点**，而非订单原始FactoryCode）+R22（过滤后剩余并行生效）。**第二层及以后按常规展开规则（R16/R17/R22/R25等），与有BOMNO的路径完全一致。** | 用户澄清（2026-05-21） | 2026-05-21 |
| **R29** | **BOMNO为空/0时，WIP/RAW前缀订单的首层BOM入口规则**：仅适用于 `MaterialCode` 以 `WIP` 或 `RAW` 开头 + `OrderType=SALES_ORDER` + `BOMNO IS NULL OR BOMNO='0'`。直接查 `MES_BOM_Edge_Active` WHERE `ParentMaterialCode`=订单`MaterialCode` AND `IsActive`=1 → 得首层BOMNO候选集。多BOMNO处理同R28（订单工厂为父件工厂起点，R17+R22）。**第二层及以后按常规展开规则，与有BOMNO路径完全一致。** R17工厂过滤后若无候选，触发**R37**降级兜底。 | 用户澄清（2026-05-21） | 2026-05-21 |
| **R30** | **RAW前缀订单首层入口无结果时判定为外购件**：`MaterialCode` 以 `RAW` 开头 + `OrderType=SALES_ORDER` + **R29查找结果为空** → 直接按**外购件**处理（不展开BOM，等同R07），**不写Issues**（此为正常业务情况，RAW物料本为采购原料，BOM无下阶是预期行为）。⚠️ 与R27-LEAF区分：R27-LEAF是 Produce声明内制但无BOM（数据异常）；R30是MaterialCode前缀本身暗示可能无BOM（正常兜底）。 | 用户澄清（2026-05-21） | 2026-05-21 |
| **R31** | **BOMNO为空/0时，生产类订单的首层BOM入口规则（必写Issues）**：适用于 `OrderType=PRODUCTION_INSTRUCTION` + `BOMNO IS NULL OR BOMNO='0'`。查找逻辑同第二层BOM查找规则：查 `MES_BOM_Edge_Active` WHERE `ParentMaterialCode`=订单`MaterialCode` AND `IsActive`=1，排序优先 MES来源，多 BOMNO 套用R17/R22。与R29的核心区别：**无论是否找到BOMNO，必须写入Issues表**（IssueType=`BOMNO_MISSING_PRODUCTION`，Severity=WARN），因为生产类订单理论上应总有確定BOMNO，缺失属异常。找到候选后继续展开（不阻塞）。未找到时Issues Severity升为ERROR | 用户澄清（2026-05-21） | 2026-05-21 |
| **R32** | **第二层及以上多BOMNO兜底收敛**（R17/R22/R25全部未能收敛时的最终过滤，适用于所有物料）：<br>**触发前提**：R17/R22/R25 全部未能将多套有效BOMNO收敛至唯一。<br>**Step1 SourceSystem过滤**：若候选集中同时存在 `SourceSystem='ERP'` 和 `SourceSystem='MES'` 的BOMNO → 保留全部ERP来源，淘汰全部MES来源（不写Issues）。<br>**Step2 出口库过滤**：Step1后ERP来源仍有多套 → 查 `ProcessCodeDict` 找 `WarehouseRole='出口库'` 的ProcessCode，保留 `ParentProcRefCode` 属于出口库的BOMNO，淘汰其余（不写Issues）。<br>**Step2后仍未收敛（含Step1后候选集为空）**→ 写Issues（`IssueType=MULTI_BOMNO_UNRESOLVED`，`Severity=ERROR`），不阻塞批次。<br>⚠️ 此规则为兜底规则，触发说明BOM数据或Produce标注存在异常，需业务复核。 | 用户澄清（2026-05-27） | 2026-05-27 |
| **R33** | **RequestedBOMNO"为空"的判定口径**：以下三种情况均视为RequestedBOMNO为空，触发R28/R29/R30/R31的"BOMNO为空"分支：① `NULL`；② 空字符串 `''`；③ 字符串 `'0'`。不满足以上三条则视为非空，走R34路径。 | 用户澄清（2026-05-27） | 2026-05-27 |
| **R34** | **RequestedBOMNO非空时首层BOM精确匹配规则**：当R33判定RequestedBOMNO非空时，首层BOM查找 = 直接按BOMNO精确匹配 `MES_BOM_Edge_Active`，**不加 `IsActive=1` 过滤**（指定BOMNO即代表业务方有意引用该版本，无论其活跃状态）。第二层及以后按常规展开规则（R16/R17/R22/R25/R32等），不受此节影响。 | 用户澄清（2026-05-27） | 2026-05-27 |
| **R35** | **MES边Produce代入**：WHILE展开结束后、落地Workset前，对 `ChildSourceHintCode IS NULL OR = 'NULL'` 的行依优先级修复：① 从同一(Parent,Child)对的ERP边继承；② 子件前缀推断（RAW%→2, 其余→1）。静默修复，不写Issues。Step 0b（Enrich层）是最后兜底防线仍保留。 | 2026-05-27 |
| **R37** | **R29工厂过滤降级兆底（FACTORY_MISMATCH_FALLBACK）**：R29按R17工厂过滤后如果无候选入口，第二阶去掉工厂过滤条件再查一次。命中则继续展开，并写入Issues（`IssueType=FACTORY_MISMATCH_FALLBACK`，`Severity=WARN`）提示订单工厂与BOM工厂不一致。厒发前提：必须是R29场景（SALES_ORDER+WIP%/RAW%+BOMNO=NULL）且R17第一阶确实为空。如降级后仍无入口 → 实际BOM中无此物料边，正常写`BOM_ENTRY_NOT_FOUND` ERROR。**典型场景**：订单`FactoryCode=CN`但BOM边`ParentProcRefCode=010699`（BJ工厂），R17过滤把唯一有效入口过滤掉。这类情况下BOM应考虑是否属于跨工厂委托或BOM工厂编码异常，需业务复核。 | 2026-05-29 |

---

## 2. 组织实体

### 2.1 五个独立工厂实体（APS 调度粒度）

| 实体代号 | 名称 | 业务定位 | APS处理 |
|---------|------|---------|---------|
| **CN** | 中国公司（量产主体） | 量产部门（含CN下多个课，6课除外） | 独立工厂实体 |
| **BJ** | 北京工厂 | 量产部门 | 独立工厂实体 |
| **TJ** | 天津工厂 | 量产部门 | 独立工厂实体 |
| **CN6** | CN制造6课 | **特注品部门**（CN下子单元，但业务独立） | **独立工厂实体**，与CN并列 |
| **SH** | 上海公司 | **特注品部门** | **独立工厂实体** |

> 📌 **重要**：CN6 虽然组织上隶属CN，但在APS视角按独立厂处理——CN6 ↔ CN 间的物料流转需生成 ShippingTask（按R08）。

### 2.2 量产 vs 特注

- **量产**：CN（除6课）、BJ、TJ
- **特注**：CN6、SH
- 两者在物料主数据、BOM结构、排程策略上通常有差异，APS 规则可能需要分支处理（具体场景待案例积累）。

---

## 3. 字段字典

### 3.1 `Produce` 字段完整定义

#### 3.1.1 值表

| Produce | 大分类 | 子分类 | 定义 | 内外购 |
|:---:|---|---|---|:---:|
| 0 | 购入 | 保税 | 日本保税/国内保税 | 外购 |
| 2 | 购入 | 课税 | 国内课税 | 外购 |
| 3 | 购入 | 保税 | 海外保税（日本以外） | 外购 |
| 4 | 购入 | 课税 | 海外课税 | 外购 |
| 10 | 购入 | 课税 | 海外课税（使用范围与4不同） | 外购 |
| 1 | 内制 | 自用 | 中国/北京/天津内制自用 | 内制 |
| 5 | 内制 | 自用 | CN制造6课内制自用 | 内制 |
| 8 | 内制 | 自用 | CN制造6课内制ASSY自用 | 内制 |
| 9 | 内制 | 自用 | SH上海公司内制自用 | 内制 |
| 6 | 内制 | 他用 | BJ北京工厂内制他用 | 内制 |
| 7 | 内制 | 他用 | CN中国公司内制他用 | 内制 |
| 11 | 内制 | 他用 | TJ天津工厂内制他用 | 内制 |

#### 3.1.2 适用范围矩阵

| Produce | 定义 | CN | BJ | TJ | CN6 | SH |
|:---:|---|:---:|:---:|:---:|:---:|:---:|
| 0 | 日本保税/国内保税 | ✅ | ✅ | ✅ | ✅ | ✅ |
| 2 | 国内课税 | ✅ | ✅ | ✅ | ✅ | ✅ |
| 3 | 海外保税（日本以外） | ✅ | ✅ | ✅ | ✅ | ✅ |
| 4 | 海外课税 | ✅ | ✅ | ✅ | ✅ | ✅ |
| 10 | 海外课税（范围不同） | ✅ | ✅ | ✅ | ✅ | ✅ |
| 1 | 中国/北京/天津内制自用 | ✅ | ✅ | ✅ | ✅ | ❌ |
| 5 | CN6内制自用 | ✅ | ❌ | ❌ | ✅ | ❌ |
| 8 | CN6制造ASSY自用 | ✅ | ❌ | ❌ | ✅ | ❌ |
| 9 | SH内制自用 | ❌ | ❌ | ❌ | ❌ | ✅ |
| 6 | BJ内制他用 | ❌ | ✅ | ❌ | ❌ | ❌ |
| 7 | CN内制他用 | ✅ | ❌ | ❌ | ❌ | ❌ |
| 11 | TJ内制他用 | ❌ | ❌ | ✅ | ❌ | ❌ |

#### 3.1.3 "自用" vs "他用" 业务区别

- **自用**（1/5/8/9）：本厂生产、本厂自己下游的父件消耗
- **他用**（6/7/11）：A厂生产，供给 B厂 作为零部件使用（跨厂供给）

### 3.2 `ProcessCode` 判定流程

```
给定一个 ProcessCode → 查 工序对照表 → 看 Process 字段：
  • 以"入库"结尾（如 BJ机加入库） → 入库码（R02：不算独立工艺）
  • 以"出口"结尾（如 CN出口）     → 出口码（R02：不算独立工艺）
  • 其他（如 BJ机加/CN表面）      → 大工艺码（R02：独立工艺）
```

### 3.3 v5.0.8 `MES_APS_BOM_Workset` 字段命名（决策：取向A，保持DDL不变）

| 字段 | 语义 |
|------|------|
| `ParentMaterialCode` | 父件 = 该BOM行的 `GoodsModel` |
| `ChildMaterialCode` | 子件 = 该BOM行被消耗的 `MaterialModel` |
| `ParentProcRefCode` | 父件此行的 `GoodsProcCode`（父件完成工序码） |
| `ChildProcRefCode` | 父件此行的 `MaterialProcCode`（父件消耗子件所处工序码） |
| `ChildSourceHintCode` | ERP `Produce` 字段值（外购/内制提示） |
| `ChildRequiredStageCode` | 见 R05：**子件自身完工的最后一道大工艺** |

---

## 4. BOM 展开规则

### 4.1 下钻终止规则

| 条件 | 是否下钻 | 依据 |
|------|---------|------|
| 外购件（Produce ∈ {0,2,3,4,10}） | ⏹ **不下钻** | R07 ✅ 已确认 |
| 内制自用（Produce ∈ {1,5,8,9}） | ⏬ 下钻 | 案例1 实证 |
| **内制他用**（Produce ∈ {6,7,11}） | ❓ **待验证** | 见 §6.1 |
| 子件无下级BOM数据 | ⏹ 到底 | 案例1 实证 |

### 4.2 工艺链推导

- 对每个 GoodsModel（作为父件出现的BOM行），按 R01 + R02 推导其工艺链
- 工艺链 = `MaterialProcCode`（起始大工艺） + ...（若有多道） + `GoodsProcCode`（终点大工艺或入库尾巴）
- **入库码、出口码不是工艺链的独立节点**，只是上游大工艺的归属库
- 案例1 典型推导结果：
  - `C2Q032-05-51642B`：工艺链 = [BJ_MACH]，落CN机加入库（同工艺跨厂入库）
  - `C2Q32ABAL123-020`：工艺链 = [CN_MACH → CN_SURF]
  - `S500A61T32X08X30`：工艺链 = [BJ_PROF]
  - `A6061-228-1200-WX`：工艺链 = [BJ_OUTS]

### 4.3 跨厂识别（R08 展开）

两种跨厂子类型，**都需要生成 ShippingTask**：

1. **同工艺跨厂入库**（如案例1 的 51642B）
   - BJ生产 → 直接落CN入库
   - 业务口径算"一道工艺"，但物理跨厂运输存在
   - **必须生成 ShippingTask**
2. **跨工艺跨厂依赖**（如案例1 的 AD138A20K）
   - BJ完成型材 → CN机加领用
   - 跨厂运输明显
   - **必须生成 ShippingTask**

### 4.4 BOMNO为空/0时的首层BOM入口推导规则（2026-05-21 新增；R28/R29/R30/R31）

> **适用前提**：RequestedBOMNO 按 **R33** 判定为空（`NULL` / 空字符串 `''` / 字符串 `'0'`）。以下规则**仅用于找到首层BOM记录**（即 `#EntryResolved` 阶段，`sp_ExpandBOMBatch_vNext` 内部）；第二层及以后**与有BOMNO时的常规展开规则（R16/R17/R22/R25/R32等）完全一致，不受此节规则影响。**

#### 规则分支判定顺序

```
RequestedBOMNO 按R33判定为空（NULL / '' / '0'）：

  IF OrderType=PRODUCTION_INSTRUCTION：→ R31（MaterialCode直查，MES来源优先，必写Issues）

  IF OrderType=SALES_ORDER：
    IF MaterialCode以 'ASSY' 开头：→ R28（出口库ProcessCode+MaterialCode查首层）
                                    └ 出口库为空（含代理后仍空）→ 降级走R29+R17路径（见R29节）
    ELSE IF MaterialCode以 'WIP' 或 'RAW' 开头：→ R29（全量查首层 → R17工厂过滤）
      ├ R17过滤后有结果：→ R22并行生效
      ├ R17过滤后为空：→ BOM_ENTRY_NOT_FOUND（ERROR）
      └ 全量查无结果 且 MaterialCode以 'RAW' 开头：→ R30（判定外购件，不展开）
    ELSE（其他前缀）：→ 原有逻辑（ParentMaterialCode直查 + IsDefaultVersion排序）
```

#### R28 详细步骤（ASSY前缀订单）

```sql
-- Step 1: 查订单所属工厂的"出口库"ProcessCode（可能多个）
SELECT ProcessCode
FROM ProcessCodeDict
WHERE FactoryCode = @OrderFactoryCode
  AND WarehouseRole = '出口库'
  AND IsActive = 1
→ 结果集 @ExportProcCodes

-- Step 1 补充：无出口库时取母体工厂代理（当前仅CN6课适用）
IF @ExportProcCodes 为空:
  -- CN6课无独立出口库，复用CN的出口库（组织隶属关系见R10/§2.1）
  -- 母体工厂映射：CN6课 → CN（可扩展为配置表，V1直接按已知映射）
  SET @EffectiveFactory = '母体工厂'（如CN6课→CN）
  重新查: ProcessCodeDict WHERE FactoryCode=@EffectiveFactory AND WarehouseRole='出口库' AND IsActive=1
  → 更新 @ExportProcCodes
ELSE:
  SET @EffectiveFactory = @OrderFactoryCode

-- Step 1 最终兜底：代理后仍无出口库 → 降级走R29+R17路径
IF @ExportProcCodes 仍为空:
  -- ⚠️ 降级不等于放弃工厂约束，仍必须同工厂匹配（@OrderFactoryCode）
  → 执行R29流程：全量查 MES_BOM_Edge_Active WHERE ParentMaterialCode=@MaterialCode AND IsActive=1
     → R17过滤（ProcessCodeDict.FactoryCode=@OrderFactoryCode，见R29节）
     → 过滤后为空 → BOM_ENTRY_NOT_FOUND（ERROR）
  → 不执行 Step 2（跳过出口库过滤）

-- Step 2: 查首层BOMNO候选
SELECT BOMNO, ParentMaterialCode, ParentProcRefCode, ...
FROM MES_BOM_Edge_Active
WHERE ParentProcRefCode IN @ExportProcCodes
  AND ParentMaterialCode = @MaterialCode   -- MaterialCode本身（含ASSY-前缀）
  AND IsActive = 1

-- Step 3: 多BOMNO处理
→ 套用R17（以 @EffectiveFactory 为父件工厂起点，非订单原始FactoryCode）
→ 过滤后剩余BOMNO按R22并行生效
→ 首层入口解析完成，第二层起按常规规则展开
```

#### R29 详细步骤（WIP/RAW前缀订单；R28降级后也走此节）

```sql
-- Step 1: 全量查首层BOMNO候选（无需出口库过滤）
SELECT BOMNO, ParentMaterialCode, ParentProcRefCode, ...
FROM MES_BOM_Edge_Active
WHERE ParentMaterialCode = @MaterialCode   -- MaterialCode本身（含WIP-/RAW-前缀；R28降级时含ASSY-前缀）
  AND IsActive = 1
→ 结果集 @AllCandidates

-- Step 1 兜底：全量查无结果且RAW前缀 → R30（见R30节），不再执行后续步骤

-- Step 2: R17 工厂过滤（必须同工厂匹配，不允许跨工厂兜底）
FILTER @AllCandidates:
  AND EXISTS (
    SELECT 1 FROM ProcessCodeDict p
    WHERE p.ProcessCode = e.ParentProcRefCode
      AND p.FactoryCode = @FactoryCode   -- 订单原始FactoryCode（R28降级时同用 @OrderFactoryCode）
      AND p.IsActive = 1
  )
→ 结果集 @FactoryCandidates

-- Step 3: 分支处理
IF @FactoryCandidates 不为空:
  → R22并行生效（多BOMNO各自展开）
  → 多BOMNO时登记 Issues(BOM_ENTRY_AMBIGUOUS, WARN)
  → 首层入口解析完成，第二层起按常规规则展开
ELSE:
  → 登记 Issues(BOM_ENTRY_NOT_FOUND, ERROR)；不阻塞批次
  ⚠️ 注意：R17过滤后为空 ≠ R30（R30仅在Step1全量查本身无结果时触发）
     此处有BOM边但无匹配工厂的边，属ERP数据异常
```

#### R30 兜底（RAW前缀+R29无结果）

```
IF MaterialCode以'RAW'开头 AND R29查询结果为空：
  → 直接按外购件处理（ResolveStatus='PURCHASE'，不展开BOM）
  → 不写Issues（此为正常业务情况：RAW物料本为采购原料，无BOM下阶是预期行为）
  → 等同R07语义，但触发条件是MaterialCode前缀+首层入口无结果的组合
```

#### R31 详细步骤（PRODUCTION_INSTRUCTION + BOMNO为空/0）

```sql
-- 查首层BOMNO候选（同第二层查找逻辑，MES来源优先）
SELECT BOMNO, ParentMaterialCode, ParentProcRefCode, ...
FROM MES_BOM_Edge_Active
WHERE ParentMaterialCode = @MaterialCode
  AND IsActive = 1
  ORDER BY (CASE WHEN SourceSystem='MES' THEN 0 ELSE 1 END),
           IsDefaultVersion DESC, Id DESC

-- 无论查询结果如何，必须写Issues
INSERT MES_APS_BOM_Workset_Issues (...)
VALUES (
  IssueType = 'BOMNO_MISSING_PRODUCTION',
  Severity  = CASE WHEN 找到候选 THEN 'WARN' ELSE 'ERROR' END,
  Detail    = '生产类订单BOMNO为空/0，已按MaterialCode推导首层入口，请核查ERP生产计划数据'
)

-- 找到候选 → 继续展开（R17/R22处理多BOMNO）
-- 未找到   → Issues已写ERROR，跳过展开（不阻塞批次）
```

#### 与R27-LEAF的区分

| 场景 | 触发条件 | 语义 | Issues |
|------|---------|------|--------|
| **R30** | RAW前缀+R29无结果 | 正常外购兜底 | ❌ 不写 |
| **R27-LEAF** | Produce声明内制但物料无任何BOM边 | ERP数据异常 | ✅ 写 `leaf_no_bom` |

### 4.5 第二层及以上多BOMNO兜底收敛（2026-05-27 新增；R32）

> **触发前提**：在 `sp_ExpandBOMBatch_vNext` WHILE 循环中，当前展开节点（第二层及以上）的父件查找到多套有效 BOMNO，且 **R17 / R22 / R25 全部未能收敛至唯一**（典型：父件上游 Produce=外购导致 R17 工厂链断裂）。适用于所有物料，不限前缀。

#### 判定流程

```
R17/R22/R25 全部未收敛，候选集仍有多套 BOMNO：

Step1：SourceSystem 过滤
  IF 候选集中同时存在 SourceSystem='ERP' 和 SourceSystem='MES'：
    → 保留全部 ERP 来源，淘汰全部 MES 来源
    → 不写 Issues
  ELSE（仅 ERP 或仅 MES）：
    → 候选集不变，直接进 Step2

Step2：出口库 ProcessCode 过滤
  IF Step1 后仍有多套 BOMNO：
    → 查 ProcessCodeDict WHERE WarehouseRole='出口库' AND IsActive=1
       取所有出口库 ProcessCode 集合 @ExportProcCodes
    → 保留 ParentProcRefCode IN @ExportProcCodes 的 BOMNO
      淘汰其余（不写 Issues）
  IF Step2 后 = 唯一：
    → 收敛完成，继续展开

Step3：兜底报错
  IF Step2 后仍有多套，或 Step1 后候选集为空：
    → 写 Issues(IssueType='MULTI_BOMNO_UNRESOLVED', Severity='ERROR')
    → 不阻塞批次（跳过此节点展开）
```

> **V1实现说明**：当前 SP 实现（v5.0.35）简化了 WHILE 迭代中的 R32 逻辑：
> - Step1（ERP>MES）：已实现
> - Step3 取 MAX BOMNO 兜底：已实现，Severity 降为 **WARN**（因仍可产出结果）
> - **Step2（出口库ProcessCode过滤）**：暂未在 WHILE 迭代中实现，留 V2 增强

#### 典型判例（ASSY-AW23P-270AS）

| BOMNO | ParentProcRef | SourceSystem | Step1结果 | Step2结果 |
|-------|-------------|-------------|---------|---------|
| 301902201 | 990202（出口库）| ERP | ✅ 保留 | ✅ 保留 |
| 301907171 | 020796 | ERP | ✅ 保留 | ❌ 淘汰 |
| NULL | 51305101 | MES | ❌ 淘汰 | — |

最终收敛：**BOMNO=301902201**，无 Issues。

---

### 4.6 RequestedBOMNO 判定口径（2026-05-27 新增；R33/R34）

#### R33：RequestedBOMNO"为空"的三种等价形式

以下三种情况**完全等价，均视为 RequestedBOMNO 为空**，触发 R28/R29/R30/R31 分支：

| 形式 | 示例值 | 说明 |
|------|-------|------|
| `NULL` | — | 字段未传 |
| 空字符串 | `''` | 传了但内容为空 |
| 字符串零 | `'0'` | ERP 习惯性填写的无效占位值 |

不满足以上三条 → 视为非空，走 R34 路径。

```sql
-- 判定函数（伪代码）
CASE
  WHEN @RequestedBOMNO IS NULL       THEN 'EMPTY'
  WHEN @RequestedBOMNO = ''          THEN 'EMPTY'
  WHEN @RequestedBOMNO = '0'         THEN 'EMPTY'
  ELSE                                    'NON_EMPTY'  -- 走 R34
END
```

#### R34：RequestedBOMNO 非空时的首层精确匹配规则

当 R33 判定为非空时：

```sql
-- 首层 BOM 查找：精确匹配 BOMNO，不过滤 IsActive
SELECT *
FROM MES_BOM_Edge_Active
WHERE BOMNO = @RequestedBOMNO
  AND ParentMaterialCode = @MaterialCode
-- ⚠️ 不加 AND IsActive = 1
-- 理由：业务方显式指定 BOMNO，有意引用该版本（含历史版本），IsActive 过滤由业务层决定
```

- 第二层及以后：按常规展开规则（R16/R17/R22/R25/R32），不受此节影响
- 首层查询无结果（指定 BOMNO 在该物料下无任何边）→ 写 Issues(`BOM_ENTRY_NOT_FOUND`, `ERROR`)

---

### 4.7 MES边Produce代入（2026-05-27 新增；R35）

#### 触发场景

当 `#WorksetRaw`（批量）或 `#RT_Expand`（实时）中某行 `ChildSourceHintCode` 为 `NULL` 或字符串 `'NULL'` 时触发。

典型根因：MES BOM 数据源（`SourceSystem='MES'`）未填写 Produce 字段，或从 MES 导出时 NULL 被序列化为字符串 `'NULL'`。

#### 处理策略（WHILE 循环结束后、落地 Workset 前执行）

```
优先级（依次尝试）：
  1. 从同一 (ParentMaterialCode, ChildMaterialCode) 对的 ERP BOM 边继承
     条件：ee.SourceSystem='ERP' AND ee.ChildSourceHintCode IS NOT NULL AND ee.IsActive=1
     排序：IsDefaultVersion DESC, BOMNO DESC（取最新默认版本）

  2. 按子件物料编码前缀推断：
     ChildMaterialCode LIKE 'RAW%'  → Produce='2'（外购·直购）
     其余                            → Produce='1'（内制·继承）
```

**适用范围**：`#WorksetRaw.ChildSourceHintCode IS NULL OR = 'NULL'`（批量）；`#RT_Expand` 同理（实时）。

#### 与 Step 0b 兜底的关系

| 阶段 | 时机 | 处理 | 写 Issue |
|------|------|------|----------|
| **R35**（展开层） | WHILE 结束后、落地前 | ERP继承 或 前缀推断 | 否（静默修复） |
| **Step 0b**（Enrich层） | 落地后、Enrich 前 | 兜底 Produce=1 | 是（`MISSING_PRODUCE` WARN） |

R35 目标是在落地前尽量消除 NULL Produce，Step 0b 是最后兜底防线（仍保留）。经 R32 收敛后若已选用 ERP 边，该物料 Produce 通常已非空，R35 仅对仅MES BOM的剩余情况生效。

---

### 4.8 R29 工厂过滤降级兜底（2026-05-29 新增；R37）

#### 触发场景

`OrderType=SALES_ORDER` + `MaterialCode LIKE 'WIP%'/'RAW%'` + `BOMNO IS NULL` 的订单，经 R29 的 R17 工厂过滤（`ProcessCodeDict WHERE FactoryCode=@FactoryCode`）后，**候选入口集为空**。

典型根因：订单 `FactoryCode='CN'`，但该物料的 BOM 边 `ParentProcRefCode='010699'`（属 BJ 工厂），`ProcessCodeDict` 中 `010699.FactoryCode='BJ' ≠ 'CN'` → INNER JOIN 无行 → R17 过滤后空集。

#### 两阶处理策略

```
阶段1（R29 主路径）：INNER JOIN ProcessCodeDict + FactoryCode = 订单工厂
  → 有结果 → 正常入口，进入展开
  → 无结果 → 触发阶段2

阶段2（R37 降级）：去掉 ProcessCodeDict JOIN，直接按 ParentMaterialCode 查
  → 有结果 → 入口命中，写 FACTORY_MISMATCH_FALLBACK WARN，继续展开
  → 无结果 → BOM 表中确实无此物料边，写 BOM_ENTRY_NOT_FOUND ERROR（正常）
```

#### Issue 字段

| 字段 | 值 |
|---|---|
| `IssueType` | `FACTORY_MISMATCH_FALLBACK` |
| `Severity` | `WARN` |
| `Detail` | `MaterialCode=xxx 订单FactoryCode=CN R17工厂无匹配BOM边，已降级为无工厂过滤入口 BOM=yyy` |

#### 与 R27（展开中段工厂不匹配）的区分

| | R27 FACTORY_MISMATCH | R37 FACTORY_MISMATCH_FALLBACK |
|---|---|---|
| **发生阶段** | BOM **展开中段**（第2层及以下子件） | **首层入口查找**（R29 阶段） |
| **触发条件** | Produce 声明工厂与 BOM 边工厂不一致 | 订单 FactoryCode 与 BOM ParentProcRefCode 归属工厂不一致 |
| **处理结果** | 保留 BOM 原生链，标 FACTORY_MISMATCH | R17 降级后取到入口，标 FACTORY_MISMATCH_FALLBACK |

---（2026-05-29 新增；R37）

## 5. 案例库

### 案例1：`CQ2B32-10+10DCZ-XC11`（气缸整机，产品族CYL）

- **原始数据**：`BOM示例数据.csv`（27行） + `工序对照表.csv`（168行）
- **展开结果文档**：`@d:\CascadeProjects\APS\BOM展开示例_CQ2B32-10+10DCZ-XC11_v2.md`
- **挑战的规则 / 贡献的规则**：R01 ~ R08
- **关键结构**：
  - 22 条有效BOM边（1条重复去重后），4 层深度
  - 2 处跨厂依赖（同工艺跨厂入库 + 跨工艺跨厂依赖）
  - Level 3 型材 / Level 4 外购铝棒到底
- **特殊现象**：`C2Q032-05-51642B` 的"同工艺跨厂入库"是本案例最重要的发现

### 案例2：`CDQ2B50-30DFZ`（CQ 系列气缸，Sheet 2）

- **原始数据**：`BOM示例数据.xlsx` Sheet 2（19行）
- **难点说明原文**：产成品构成件之一 `C2Q50RBAM863-040` 再找下阶 BOM 时，出现**多条数据**，需要根据 `Produce` 和 `GoodsProcCode` 的所属工厂**综合判断哪些不在本制品的组成BOM中**；中间子件下面又有两个子件构成
- **关键结构**：
  - 产品根 `CDQ2B50-30DFZ`，`BOMNo=202603241`，`GoodsProcCode=980201`（CN出口）
  - Level 1：11 个子件，`MaterialProcCode` 全部 = `510799`（CN组装保税）；`Produce` 分布：6 (BJ他用) × 4、0/2 (外购) × 5、1 (继承CN) × 2
  - 关键子件 `C2Q50RBAM863-040`（Produce=1 继承CN）出现于 **4 条不同 `BOMNo`**：
    | BOMNo | GoodsProcCode | 查表含义 | 消耗 |
    |---|---|---|---|
    | 102602061 | `510498` | CN机加入库 | `C2Q50RBAL041-040` (Produce=6) |
    | 200707031 | `540488` | CN6课_加工M工号库 | `C2Q50RBAL041-040` (Produce=1) |
    | 201704011 | `510495` | CN6课_特注加工M工号库 | `C2Q50RBAL041-040` (Produce=1) |
    | 201802271 | `640488` | SH入库（需验证） | `C2Q50RBAL041-040` (Produce=1) |
  - 下一层 `C2Q50RBAL041-040`（BOMNo=102603171，`GoodsProcCode=510699` CN表面氧化现场库）消耗 `C2Q50-31-AC556` 和 `C2Q50A-AD140A20K` 各一个；再下层都是型材 → 外购铝棒
- **贡献规则**：R17（BOMNo 工厂筛除规则，判例来自本案例）
- **判例结论**：4 条 `BOMNo` 经 R17 筛除后仅保留 `102602061`（`GoodsProcCode=510498` 属 CN），其余 3 条（CN6课 2 条 + SH 1 条）排除；筛除后仅剩 1 条，表面上像"二选一"，实际上是"精确匹配后唯一匹配"

### 案例3：`MB1B100-1000Z`（MB 系列气缸，Sheet 3 —— 跨公司外协 + WBM中间件）

- **原始数据**：`BOM示例数据.xlsx` Sheet 3（22行）
- **难点说明原文**：天津工厂涂装或者氧化**外协**的代码，在 `工序对照表` 中有一个特殊字段叫 **受托process**，也就是这个仓库本身是天津工厂的，但只是**暂存**要到 CN 或 BJ 进行工序处理的，因为是不同公司，所以称为**外协**（经过多公司处理）
- **关键结构**：
  - 产品根 `MB1B100-1000Z`，`BOMNo=202506096`，`GoodsProcCode=960201`（TJ出口，需确认）
  - Level 1：18 个子件，`MaterialProcCode=310799`（TJ组装）；`Produce` 分布：0/2 (外购) × 10、1 (继承TJ) × 8
  - **WBM 中间件链**（在 TJ 内部的多阶段工艺）：
    ```
    MB-A0-02-AL557  [Produce=1, TJ]
      ├─ BOMNo=102604151, GoodsProcCode=310498 (TJ机加入库), MaterialProcCode=311594
      │  消耗 WBM-MB-A0-02-AL557   [中间件，Produce=1, TJ]
      │
      └─ (同BOMNo 102604151) GoodsProcCode=310594, MaterialProcCode=310499
         消耗 MB-A0-02-AK090-C    [Produce=6 → BJ他用，基件在 BJ 生产]
            └─ BOMNo=102011041, GoodsProcCode=011298 (BJ型材入库)
               消耗 D3X-GC  [Produce=2 外购] ⏹
    ```
  - **"WBM-" 前缀语义**（已确认→ R18）：`WBM-MB-A0-02-AL557` 是 `MB-A0-02-AL557` 的同本体**中间阶段产物**（同一物件在机加完成后→进入涂装/氧化前的中间状态）
  - **跨公司外协场景**（难点说明所指）：TJ 的某些工序在 `工序对照表` 中对应的 `受托process` 列指向 CN/BJ 的某个工序——即**物理仓库在 TJ**，但**实际加工由 CN/BJ 代工完成**。此类行需按"受托"字段关联到**代工方的工序**进行排程
- **贡献规则**：R18（WBM 中间件语义）；R08 跨厂运输的**跨公司外协子类**（新类型，待专案例取得定义）
- **历史对话要点**：用户曾举例 "TJ加工310499接到生产指令后，将 MB-A0-02-AK090-C 生产成 WBM-MB-A0-02-AL557，然后本来应该 TJ 涂装生产，但 TJ 涂装设备维修..."——这是**临时跨厂代工**的典型场景（不一定按 ERP 主数据固定的受托关系走，可能因产能/故障临时切换代工方）

### 案例4：`25A-MGPM12-10AZ-DNY9019`（MGPM系列特注导杆缸）

- **原始数据**：`BOM示例数据.xlsx` Sheet 4（34行） + `工序对照表.xlsx`（155行，含 `代码所属工厂` / `实际生产（含受托）工厂` / `受托process`）
- **贡献规则**：R12（6位工序码）、R13（入库省略差异）、R14（追加工语义）、R15（同 BOMNo 多 MaterialProcCode 并行）、R16（多 BOMNo 并行非二选一）
- **关键结构**：
  - 产品根（`25A-MGPM12-10AZ-DNY9019`）在 `BOMNo=202603271` 下 **28 条记录**，`MaterialProcCode` 两种并行：`510789`（课税组装）× 4 条 + `510799`（保税组装）× 24 条
  - `MGP12-42-AK911-DNX0085` 出现于 **两条不同 `BOMNo`**：`201912345`（消耗钢板，GoodsProcCode=`540488`）+ `202504066`（消耗不锈钢板，GoodsProcCode=`510495`），两条**均生效**，是同一件的多工序段，不是版本二选一
  - 追加工链：`MGP12A-AK911-010` 父件 / `MGP12A-AC891-010` 子件，子件的 `GoodsProcCode=010699`（表面氧化现场库）与 被消耗时的 `MaterialProcCode=010496`（追加工现场库）不一致——差异段是"氧化完成 → 隐藏入库 → 为追加工再出库 → 进追加工现场库"
  - 原始样本中曾存在前导0丢失的 5 位 ProcessCode（如 `10498`/`10496`/`10499`/`10599`/`10699`/`13499`），已经依 R12 规范化为 `010498`/`010496`/`010499`/`010599`/`010699`/`013499`（xlsx 源文件已写回，参见 `_normalize_proccode.py` 的执行报告）

### 案例5：`MGGMF32TF-300-XB6`（MGG 系列气缸 —— sub-ASSY 多层嵌套 + 首次出现"锻造"工艺）

- **原始数据**：`BOM示例数据.xlsx` Sheet 5（37行）
- **难点说明原文**：产成品下有组件，组件又由子件构成，层次较多
- **贡献规则**：R19（ASSY 品库/组装内部两种库位角色）；R14 的边界修订（不靠尾号判追加工）
- **关键结构**（深度 5，目前案例最深）：
  - 产品根 `MGGMF32TF-300-XB6`，`BOMNo=200707088`，`GoodsProcCode=980201`（CN出口）
  - Level 1：23 个子件
    - 22 个散件从 `510799`（CN 组装现场库）消耗
    - **1 个 sub-ASSY** `CG1ZN32TF-300Z-XB6`（Produce=1）从 `510496`（CN_3课_**ASSY品库**）消耗 → 这是 R19 的典型形态
  - sub-ASSY 展开：
    ```
    CG1ZN32TF-300Z-XB6  [BOMNo=302603191, GoodsProcCode=510496 完成入ASSY品库]
      └─ 从 510799 消耗 10 个子件，含两个 CN 内制件：
         ├─ C1G32AAAL521-300  [工艺链: CN机加511499 → CN表面510699]
         │   └─ C1G32-03IAH113-F  [🆕 工艺链: CN锻造510299 → CN锻造入库510298]
         │       └─ C1G32-03TM5962-K  [Produce=6 BJ他用, BJ型材]
         │           └─ A6061-228-1200-GC  [外购] ⏹
         └─ C1G32BAAM407-300  [工艺链: CN机加510499 → CN机加入库510498]
             └─ S016S45R12X30  [外购] ⏹
    ```
- **其他新发现**：
  - 🆕 **`CN锻造` 大工艺**首次出现（`510298`=CN_CG缸筒挤压M工号/入库、`510299`=CN_CG缸筒挤压现场）——工艺类型词典扩展
  - 🆕 **尾号 `*96` 的歧义**：`510496` = ASSY品库（非追加工）；`010496` = 追加工现场库（是追加工）——**尾号不是可靠判据**，R14 已修订
  - BOMNo `302603191` 首位 "3" 经用户确认**无业务语义**，只是号段巧合

---

## 6. 待验证清单

### 6.1 🔄 内制他用（Produce ∈ {6,7,11}）的 BOM 下钻行为 —— R20 已落库，但用户要求批量阶段**再验证**

**R20 定义**（不变）：终止下钻 + `CROSS_ORG_HANDOFF` 标记

**批量执行策略（2026-04-22 用户指示）**：
- 用户对 R20 的准确性有疑虑，要求 **临时放开下钻**（即 Produce=6/7/11 子件仍然递归展开）
- 同时在**每一个 Produce ∈ {6,7,11} 的节点**上**醒目标注**：`⚠️ [R20-候选: 按当前规则应终止下钻，现临时放开待你人工确认]`
- 用户对每个标注点逐一确认后，再决定是否维持 R20、收缩 R20 适用范围、或补充例外
- 批量结束后汇总所有候选点，形成 R20 的最终判例集


**业务描述**（用户口述）：A厂为B厂提供零部件，这类"内制他用"零件在 B厂BOM 展开时，可能不继续下钻，而是通过**厂间出荷指示**挂接到 A厂，由 A厂自己排产。

**现状**：
- 现有方案文档（`APS 核心排产全流程走查`、`APS_分域计算设计方案_v1.0`、`04_5号位对外契约`）已有：
  - 01:50 跨域依赖静态扫描（`Domain_Dependency` 表）
  - 虚拟库存硬约束（场景1）
  - ShippingTask / 厂间发货Task
  - `ICrossDomainRule` 插件
- **但没有**明文规则："Produce=6/7/11 是 BOM 下钻终止信号"

**待验证问题**：
1. B厂BOM展开到"内制他用"子件时，是否**终止下钻**？
2. 厂间出荷指示的数据模型是**复用** `Domain_Dependency`，还是**新建** `InterFactoryShippingDemand` 表？
3. A厂如何接收指示并转成自己的订单/需求（工作流节点）？
4. 是否存在"跨厂但不跨产品族"的情况，此时现有"跨域硬约束"机制是否适用？

**下一步**：用户正在考虑/验证此问题，暂不写入正式方案文档，也不写入第 1 章规则索引。

### 6.2 ✅ 追加工场景语义（已确认，规则化为 R14）

- 语义已由 R14 定义：父件=产出件，子件=基件，基件已完成其工艺链并入库，**为追加工再次出库** 进入 `*96 追加工现场库`。
- 遗留待定（见 §6.3）：APS 是否需要为"隐藏入库→为追加工再出库"这段物料动作生成显式任务/占用时间？

### 6.3 ⏳ R13/R14 的 APS 显式声明问题

- R13 指出"涂装/氧化完工→入正规库"这段在 BOM 中省略；R14 指出"基件为追加工从正规库再出库到 `*96`"也是隐式动作。
- **待定**：APS 排程是否需要为这些隐式动作生成**显式任务**？三个层级的备选方案：
  1. **完全忽略**（假设零耗时完成）
  2. **补齐"仓间流转"任务**（不占工艺资源、仅占时间）
  3. **只在追加工再出库时补任务**（因为是不连续动作，可能存在等待/批次合并），表面完工后的常规入库不补
- 用户倾向：**不确定，因为此类情形数量非常多**，若显式建模成本高；需后续结合排程精度诉求评估

### 6.4 ✅ R16/R17 体系已闭环

- R17 筛除 + R16 并行的两层判定模型完全成立
- R17 筛除后仍多条的情况 → **R22 正式确认独立生效**（父件型号相同、子件型号不同 = 同时消耗多种子件的并行结构）

### 6.5 ⏳ 字段 `KogoProcessCode` / `storkOutType` 语义

- 案例4 首次出现两个此前未关注字段：
  - `KogoProcessCode`：形似工艺代码，常见于特注/外协/追加工相关行
  - `storkOutType`：值域 `0 / 1 / NaN`，疑似出库类型标志
- 待用户定义其业务含义，再决定是否入模型

---

## 7. 纠错记录（v1 → v2 的理解偏差）

源自案例1 首次分析的11个典型错误，留作后续案例分析的**反例清单**：

| # | 曾经的错误 | 正确理解 | 对应规则 |
|---|----------|---------|---------|
| 1 | 把 `MaterialProcCode` 当"子件的工序位置" | 是"消耗子件生产父件"的工艺 | R01 |
| 2 | 把 `GoodsProcCode` 当父件的组装库 | 是 GoodsModel 最终完成的工艺/入库/出口位置 | R01 |
| 3 | 把所有 `Produce>0` 都当内制 | Produce ∈ {0,2,3,4,10} 外购、{1/5/6/7/8/9/11} 内制 | R04 |
| 4 | 数量用 `MaterialQty` | 用 `RequestQty`（含损耗） | R03 |
| 5 | 把入库码（510498/040198等）当独立大工艺 | 入库码是上游大工艺的尾巴 | R02 |
| 6 | 把出口码（980201）当独立大工艺 | 出口码是销售库存节点 | R02 |
| 7 | 对 `C2Q32ABAL123-020` 误推为 CN组装 | 正确经过 CN机加 → CN表面 | R01 |
| 8 | `ChildRequiredStageCode` 全填父件消耗工艺 | 应填子件自身完工大工艺 | R05 |
| 9 | EDGE 链穿透到子件的子件 | EDGE 只记录子件自身工艺链 | R06 |
| 10 | "工艺内仓间调拨"错误说法 | 跨厂物理运输必须 ShippingTask（R08） | R08 |
| 11 | 把 CN/BJ/TJ 当地理城市 | 是制造公司代号，不绑定地理 | R09 |
| 12 | 把同一父件**多条 `BOMNo`** 理解为"多版本二选一"（需按工厂/日期/特注标识筛选出一条） | 多 `BOMNo` = **多道工序段并行**，每条都要生效、各消耗各自零件 | R16（案例4） |
| 13 | 把同一父件**同 `BOMNo`** 下多个 `MaterialProcCode` 的并存认为是特注品特例 | 这是常态现象，典型是**课税仓+保税仓并行**，APS 不能去重 | R15（案例4） |
| 14 | 把涂装/氧化完工后 BOM 没有入库码**当成数据缺失** | BOM 对该段入库**按工艺类型有意省略**（机加明示 `*98`、涂装/氧化隐式入库），非缺失 | R13（案例4） |
| 15 | 把追加工理解为"同一批次从 `*98` 连续流到 `*96`"的连续动作 | 基件已入库 → **为追加工再次出库**，是**不连续**动作 | R14（案例4 + 案例1难点说明） |
| 16 | 用 5 位的 `ProcessCode` 直接匹配 `工序对照表`（导致查不到） | 所有工序码固定 6 位，5 位都是前导 0 丢失，**必须左补 0** 再匹配 | R12（案例4） |
| 17 | 面对案例2 的 4 条 BOMNo 曾多次摇摆于"按日期选"/"按特注选"/"全部并行" | 正确是按工厂精确匹配：Produce=1 子件在 CN 父件上下文下必归属 CN（不含 CN6/SH），排除 CN6/SH 三条，保留 CN 的 `102602061` 一条 | R17（案例2判例） |
| 18 | 曾把 `WBM-*` 前缀的物料当作普通中间物料（另一个独立实体） | `WBM-*` 是同本体在中间工艺阶段的状态，逻辑上是同一物件 | R18（案例3） |
| 19 | 遇到 `*96` 尾号就**推测**为追加工现场库 | `*96` 尾号**不是可靠判据**（`510496` 是 ASSY品库非追加工、`010496` 才是追加工现场库），**必须查 `是否追加工` 字段** | R14 修订（案例5） |
| 20 | 把 `MaterialProcCode` 指向 ASSY 品库（`510496`）的行误解为"组装现场库的一种" | ASSY 品库是组装工艺内部的**完成位**，指向它意味着消耗的是**已完工的 sub-ASSY**（单独 BOMNo 展开）；组装现场库（如 `510799`）才是领料位 | R19（案例5） |
| 21 | 曾对 Produce=6/7/11 的内制他用子件摇摆于"要不要递归展开"，特别是横跨多层 sub-BOM 时容易混淆 | 正确：终止下钻，打 `CROSS_ORG_HANDOFF` 标记（不同于外购 `PURCHASE`），由他用方工厂自行排产 | R20 |
| 22 | 曾不确定产品根的工厂怎么推，想用 `MaterialProcCode` 或其他上下文 | 正确：用根 `GoodsProcCode` 的 `代码所属工厂`字段，为 R17 推导链提供起点 | R21 |
| 23 | 曾对 R17 筛除后多条 BOMNo 的合并语义有疑虑 | 正确：都独立生效，每条消耗自己的子件，父件型号相同但子件不同 = 并行多种消耗不是重复 | R22 |
| 24 | 脚本 v3 用 `STAGE_ORDER = [..., CN外协, CN机加, ...]` 词典序重排工艺链 → `MGG40-40-S2403A` 输出"CN外协 → CN机加"（错） | 必须保留 BOM 边原生 `MaterialProc → GoodsProc` 配对顺序："CN机加 → CN外协"，外协/机加相对次序由数据决定 | R24（案例6 修订） |
| 25 | 曾把 `C1G40B-AH114A450` 的 CN 路径 + SH 路径 当作 R16"多段并行"合并输出两条工艺链（ChildRequiredStageCode 误选到 SH 机加） | Produce=1 应按 R17/R21 继承父件工厂（CN），收敛为唯一 CN 路径；多工厂 BOMNo = 可替代路线不是多段并行 | R25（案例6 新增） |
| 26 | 脚本曾把 `INTERNAL_SELF = {'1','5','8','9'}` **统一**当作"继承父件厂"处理 → Produce=5（应 CN6课）时传 `inherit_factory=CN` 过滤掉 CN6课 链 → 空链"⚠无链"；Produce=6/7/11 不做工厂过滤 → `chain[-1]` 随原生序误取 SH | R17 必须**按 Produce 精确映射**（v5.0.14 照片权威）：1=继承父件；5/8=CN6课；9=SH；6=BJ；7=CN；11=TJ。跨厂识别的 `cm_fact` 也要用此映射，不能从链尾猜 | R17 精化（案例7） |
| 27 | `370694` 代码所属=TJ、实际生产=BJ（受托）——曾想"按实际生产工厂匹配"会把 TJ 委外段纳入 BJ 链 | 按**代码所属工厂**做过滤（账面归属）；受托关系只作元数据辅助生成委外 ShippingTask | R26 |
| 28 | ERP 数据矛盾（Produce 与 BOM 实际厂不一致）曾被脚本默默输出"⚠无链" | 必须分治：叶子 → `LEAF` 终止；矛盾 → 保留 BOM 原生链 + Mark `FACTORY_MISMATCH` + Issues 登记，供业务复核 | R27 |

---

## 附：维护指引

- 新案例加入：按案例编号加到 §5，把新规则加到 §1，把新字段/实体加到 §2/§3
- 规则升级：改 §1 规则文字 + 在新规则旁标注修订日期
- 规则推翻：不直接删除，移到 §7 纠错记录
- DDL 改动评审触发条件：当待验证清单变空 + 规则数 ≥ 20条 + 至少3个不同复杂度案例入库
