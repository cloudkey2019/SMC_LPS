# APS V1 无设备小工序与人工有限产能模型 —— DDL 变更申请单（v5.1.9 终版）

**版本**：v2.0（终版；**supersedes v1.0 候选**《…_DDL变更申请_v5.1.9候选_v1.0_20260921.md》）
**日期**：2026-09-22
**申请人**：3号位（规则/参数治理）
**提交**：0号位（批准）、2号位（落库执行，红线 #6）
**依据**：
- 0号位《APS_V1_1号位_3号位问题统一回复意见_v1.0_20260922.md》**五.1 / 五.2 / 七**（最终口径）
- 冻结 DDL《APS_数据库表结构设计_DDL_v5.1.8.2_最终可执行版_Setup换型规则_无设备小工序与人工有限产能_用户输出联合冻结版.sql》
- 《APS_V1_无设备小工序与人工有限产能模型最终裁决_3号位接收回执_v1.0_20260921.md》（R6 载体口径）

**变更范围**：**不新增任何表**。对齐冻结 DDL v5.1.8.2——① `RoutingOperation` 增加 `OperationPlanningMode` 列（三态 CHECK + DEFAULT）；② `ResourceType` 值域支持 `HUMAN`。
**性质**：冻结 DDL 落库执行申请；红线 #3（唯一索引仅 DB 兜底）与红线 #6（落库执行归属）按下列声明处理。

---

## 〇、修订说明（v1.0 候选 → v2.0 终版）

依据 0号位《统一回复意见》：

| # | 0号位 口径 | 对 v1.0 候选的处置 |
|---|---|---|
| 1 | **五.1**：OperationPlanningMode 属于工艺数据，继续归 `RoutingOperation`，不新增独立参数表 | 撤销 `OperationPlanningParam` 表 |
| 2 | **五.2**：不能因提出表名就认为冻结已规定 OperationTransitionRule / StageTransferTime 表；本轮不新增 | 撤销 `OperationTransitionRule` / `StageTransferTime` 两表 |
| 3 | **七**：本轮需要补齐清单（Task.SetupSource / ResourceCalendar / ScopeJson / 接口同步）不含两表；不得因示例场景扩大业务范围 | 等待/转运由 OperationPlanningMode 三态（WAIT_ONLY）兜底 + 1号位 默认 0 消费；未来确需作独立能力单独立项（不在本轮） |

**结论**：本终版**仅申请冻结 DDL v5.1.8.2 已定义的物理调整落库执行**，不新增任何表。

---

## 一、变更 1：RoutingOperation 增加 OperationPlanningMode（对齐冻结 DDL v5.1.8.2）

**依据**：0号位 回复五.1 + 冻结 DDL v5.1.8.2（§真实物理调整 1：RoutingOperation 增加 OperationPlanningMode）。

```sql
-- 对齐冻结 DDL v5.1.8.2（RoutingOperation 增加 OperationPlanningMode）
IF COL_LENGTH('RoutingOperation', 'OperationPlanningMode') IS NULL
BEGIN
    ALTER TABLE RoutingOperation
    ADD OperationPlanningMode NVARCHAR(32) NOT NULL
        CONSTRAINT DF_RoutingOperation_OperationPlanningMode
        DEFAULT ('FINITE_RESOURCE');
END
GO

ALTER TABLE RoutingOperation
    ADD CONSTRAINT CHK_RoutingOperation_PlanningMode
    CHECK
    (
        OperationPlanningMode IN
        (
            'FINITE_RESOURCE',
            'UNCONSTRAINED',
            'WAIT_ONLY'
        )
    );
GO
```

**设计要点**：
- 三态 = `FINITE_RESOURCE`（人工有限产能）/ `UNCONSTRAINED`（无约束）/ `WAIT_ONLY`（仅等待/转运占位，默认 0 兜底）；
- 默认 `FINITE_RESOURCE`，既有 RoutingOperation 行自动落默认值，无数据迁移；
- 归属 `RoutingOperation`（工艺数据），**不新增独立参数表**；治理侧由 3号位 工艺属性维护走既有工序主数据通道。

## 二、变更 2：ResourceType 值域支持 HUMAN（对齐冻结 DDL v5.1.8.2）

**依据**：冻结 DDL v5.1.8.2（§真实物理调整 2：ResourceType 支持 HUMAN；§ResourceType 值域说明）。

- 人工资源复用 `Resource` + `ResourceCalendar` + `OperationResourceEligibility`，**不新增 Human 专用资源表**；
- 如数据库通过 CHECK 约束限制 `ResourceType`，同步增加 `HUMAN`；如走字典维护，增加对应字典值（以冻结 DDL 实际形态为准）。

---

## 三、红线核对

| 红线 | 符合性 |
|---|---|
| #3 SqlBulkCopy 先去重、不依赖 DB 约束保证完整性 | ✅ 本单无新表、无新唯一索引；DEFAULT + CHECK 为工艺数据约束，非主防线 |
| #6 DB schema 变更 2 号位专属，其余号位提交变更申请 | ✅ 本单为**申请**；落库执行归属见 §五-① |
| #5 接口即契约 | ✅ 不加表不改既有接口；DomainSolveRequest 消费 OperationPlanningMode 走 S4 契约文档先行 |

---

## 四、与既有表关系

- **无新增表**；`RoutingOperation` 加列不引入 FK 变更；
- 人工 Resource（HUMAN）+ Calendar 由 APS 资源主数据侧维护、2号位 加载，不在 3号位 治理范围（0号位 回复三）；
- 等待/转运（工序间等待/自然冷却/阶段转运）本轮无承载表——三态 WAIT_ONLY 兜底 + 1号位 默认 0。

---

## 五、待确认项

| # | 确认项 | 3号位 建议 |
|---|---|---|
| ① | 落库执行归属（红线 #6） | 冻结 DDL v5.1.8.2 整体落库由 **2号位** 执行；或参照 Setup v5.1.8 先例（2026-09-17 授权 3号位 直接落地）授权 **3号位** 落地本补丁 |

（v1.0 候选原确认项②主键 FK 口径、③ EffectiveVersion 版本规则——随三表撤销，不再适用。）

---

## 六、回执表

| 问 | 答复 |
|---|---|
| 本终版与 0号位 回复五.1/五.2/七 是否一致 | （待 0号位） |
| 落库执行归属 ① | （待 0号位 / 2号位） |

---

*批准后：生成《APS_数据库_v5.1.8到v5.1.9_无设备小工序_升级补丁.sql》（RoutingOperation 加列 + ResourceType HUMAN）→ 落库 dev → S2 治理模型实施（OperationPlanningMode 工艺属性维护通道）。*
