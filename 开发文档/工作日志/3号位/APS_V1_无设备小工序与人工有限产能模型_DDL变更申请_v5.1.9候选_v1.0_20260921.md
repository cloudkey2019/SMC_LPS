# APS V1 无设备小工序与人工有限产能模型 —— DDL 变更申请单（v5.1.9 候选）

**版本**：v1.0
**日期**：2026-09-21
**申请人**：3号位（规则/参数治理）
**提交**：0号位（批准）
**抄送**：2号位（DDL 落库执行 + 主键口径确认，红线 #6）
**依据**：
- 0号位《未命名的Markdown文件 (51)(1).md》（最终裁决）§二 R6 / §六 人工 Resource Slot / §八 Transition Rule 与 StageTransferTime / §九 统一裁决表
- 《APS_V1_无设备小工序与人工有限产能模型最终裁决_3号位接收回执_v1.0_20260921.md》（S1 交付物）
- 先例：《APS_数据库_v5.1.7到v5.1.8_Setup换型规则_升级补丁.sql》（2026-09-17）

**变更范围**：APS_Production 库新增 **3 张表**（OperationPlanningParam / OperationTransitionRule / StageTransferTime），不修改既有表。
**性质**：规则/规划属性新增，非结构重构；红线 #3（唯一索引仅 DB 兜底）与红线 #6（落库执行归属）按下列声明处理。

---

## 〇、三表定位总览

| 表 | 定位（裁决原文口径） | 冻结维度 | 治理方 | 加载方 | 消费方 |
|---|---|---|---|---|---|
| `OperationPlanningParam` | APS 工艺规划属性（Operation Planning Attribute），**非规则引擎**、非 SetupTransitionRule 动态规则 | 无（OperationCode + EffectiveVersion 生效版本） | 3号位 维护 | 2号位 加载 | 1号位 消费 |
| `OperationTransitionRule` | 工序转运规则（部门/Stage 内部默认转运），3号位 规则参数 | RuleSetVersionId（参照 SetupTransitionRule） | 3号位 维护 | 2号位 加载 | 1号位 消费 |
| `StageTransferTime` | 跨大工艺固定转运时长（如 机加工→表面处理 4h），3号位 规则参数 | RuleSetVersionId（参照 SetupTransitionRule） | 3号位 维护 | 2号位 加载 | 1号位 消费 |

传输：三表均经 **DomainSolveRequest** 进 1号位，**不进 Frozen 六块**（裁决 §二 R1/R2）。

---

## 一、表 1：OperationPlanningParam（APS Operation Extension / 规划属性载体）

**依据**：裁决 §二 R6 ——「OperationPlanningMode 属于 APS 工艺规划属性，不是规则引擎；建议载体 OperationPlanningParam / APS Operation Extension：OperationCode + PlanningMode + EffectiveVersion」。三态码 `FINITE_RESOURCE` / `UNCONSTRAINED` / `WAIT_ONLY`（裁决 §九）。

```sql
CREATE TABLE OperationPlanningParam (
    Id               BIGINT IDENTITY(1,1) PRIMARY KEY,
    OperationCode    NVARCHAR(50)  NOT NULL,
    PlanningMode     NVARCHAR(30)  NOT NULL,
    EffectiveVersion NVARCHAR(30)  NOT NULL,
    IsActive         BIT           NOT NULL DEFAULT 1,
    CreatedAt        DATETIME2     NOT NULL DEFAULT GETDATE(),
    CreatedBy        NVARCHAR(100) NULL,
    UpdatedAt        DATETIME2     NULL,
    UpdatedBy        NVARCHAR(100) NULL,
    CONSTRAINT CK_OperationPlanningParam_Mode
        CHECK (PlanningMode IN ('FINITE_RESOURCE','UNCONSTRAINED','WAIT_ONLY'))
);
GO

-- 同一 OperationCode + 生效版本 仅一条有效（DB 兜底；主防线 = 3号位 写入校验，红线 #3）
CREATE UNIQUE INDEX UQ_OperationPlanningParam_Op_Version
ON OperationPlanningParam (OperationCode, EffectiveVersion)
WHERE IsActive = 1;
GO

-- 装载辅助索引：按 OperationCode + 有效 装载（2号位）
CREATE INDEX IX_OperationPlanningParam_OperationCode
ON OperationPlanningParam (OperationCode, IsActive);
GO
```

**设计要点**：
- **无 RuleSetVersionId 冻结**——规划属性，非动态规则（裁决 R6 明示），版本语义走 `EffectiveVersion`（如 `V1.0`）；同一 OperationCode 同一版本仅一条有效。
- 生效版本选取规则（同码多版本取最新 IsActive=1）在 S4 契约文档定义，DB 层不承载。
- `OperationCode` 的 FK 到 RoutingOperation 主链口径**待 2号位 确认后补**（参照 v5.1.8 先例 ResourceId/FromMaterialId 延期补 FK）。

## 二、表 2：OperationTransitionRule（工序转运规则）

**依据**：裁决 §八 ——「3号位 维护 Operation Transition Rule，例如 部门/Stage：机加工内部 默认转运 30 分钟」；§九 「Transition Rule = 3号位规则参数」；R1 参照 SetupTransitionRule（RuleSetVersionId 冻结）。**不做复杂规则引擎**（裁决 §八 明示）。

```sql
CREATE TABLE OperationTransitionRule (
    Id                     BIGINT IDENTITY(1,1) PRIMARY KEY,
    RuleSetVersionId       BIGINT        NOT NULL,
    ProductionDepartmentId INT           NULL,
    StageCode              NVARCHAR(50)  NULL,
    FromOperationCode      NVARCHAR(50)  NULL,
    ToOperationCode        NVARCHAR(50)  NULL,
    TransferMinutes        DECIMAL(18,4) NOT NULL,
    IsActive               BIT           NOT NULL DEFAULT 1,
    CreatedAt              DATETIME2     NOT NULL DEFAULT GETDATE(),
    CreatedBy              NVARCHAR(100) NULL,
    UpdatedAt              DATETIME2     NULL,
    UpdatedBy              NVARCHAR(100) NULL,
    CONSTRAINT FK_OperationTransitionRule_RuleSetVersion
        FOREIGN KEY (RuleSetVersionId) REFERENCES RuleSetVersion(Id),
    CONSTRAINT CK_OperationTransitionRule_TransferMinutes
        CHECK (TransferMinutes >= 0),
    -- 防全空规则：部门/Stage/工序 至少命中一个范围维度
    CONSTRAINT CK_OperationTransitionRule_Scope
        CHECK (ProductionDepartmentId IS NOT NULL
            OR StageCode IS NOT NULL
            OR FromOperationCode IS NOT NULL
            OR ToOperationCode IS NOT NULL)
);
GO

-- 同一版本内，同类范围（部门+大工艺+起止工序）仅一条有效（DB 兜底；主防线 = 3号位 发布前冲突校验）
CREATE UNIQUE INDEX UQ_OperationTransitionRule_Version_Scope
ON OperationTransitionRule (
    RuleSetVersionId, ProductionDepartmentId, StageCode, FromOperationCode, ToOperationCode
)
WHERE IsActive = 1;
GO

-- 装载辅助索引：按冻结版本 + 有效装载（2号位）
CREATE INDEX IX_OperationTransitionRule_RuleSetVersion
ON OperationTransitionRule (RuleSetVersionId, IsActive);
GO
```

**设计要点**：
- 范围语义（裁决 §八）：`ProductionDepartmentId`/`StageCode` 表达「部门/Stage 内部默认转运」；`FromOperationCode`/`ToOperationCode` 可空表达**兜底默认**（NULL=范围内任意→任意）。匹配优先级（具体工序对 > 默认兜底）在 S4 契约文档定义。
- 规则随 RuleSetVersionId 冻结，复用现有 RuleSetVersion 治理（与 SetupTransitionRule 同通道）；发布前冲突校验主防线 = 3号位（红线 #3）。

## 三、表 3：StageTransferTime（跨大工艺转运时长）

**依据**：裁决 §八 ——「Stage Transfer Time，例如 机加工→表面处理 4 小时」；§九 「Stage Transfer Time = 3号位规则参数」。

```sql
CREATE TABLE StageTransferTime (
    Id                BIGINT IDENTITY(1,1) PRIMARY KEY,
    RuleSetVersionId  BIGINT        NOT NULL,
    FromStageCode     NVARCHAR(50)  NOT NULL,
    ToStageCode       NVARCHAR(50)  NOT NULL,
    TransferMinutes   DECIMAL(18,4) NOT NULL,
    IsActive          BIT           NOT NULL DEFAULT 1,
    CreatedAt         DATETIME2     NOT NULL DEFAULT GETDATE(),
    CreatedBy         NVARCHAR(100) NULL,
    UpdatedAt         DATETIME2     NULL,
    UpdatedBy         NVARCHAR(100) NULL,
    CONSTRAINT FK_StageTransferTime_RuleSetVersion
        FOREIGN KEY (RuleSetVersionId) REFERENCES RuleSetVersion(Id),
    CONSTRAINT CK_StageTransferTime_TransferMinutes
        CHECK (TransferMinutes >= 0)
);
GO

-- 同一版本内，起止大工艺组合仅一条有效（DB 兜底；主防线 = 3号位 发布前冲突校验）
CREATE UNIQUE INDEX UQ_StageTransferTime_Version_Stage
ON StageTransferTime (RuleSetVersionId, FromStageCode, ToStageCode)
WHERE IsActive = 1;
GO

-- 装载辅助索引：按冻结版本 + 有效装载（2号位）
CREATE INDEX IX_StageTransferTime_RuleSetVersion
ON StageTransferTime (RuleSetVersionId, IsActive);
GO
```

**设计要点**：起止 `StageCode` 必填（跨大工艺转运必有方向）；方向性由应用层保证（From→To 单向，逆向需另配或双向默认——口径在 S4 契约文档定义）。

---

## 四、红线核对

| 红线 | 符合性 |
|---|---|
| #3 SqlBulkCopy 先去重、不依赖 DB 约束保证完整性 | ✅ 三表唯一索引均为 **DB 兜底**；主防线 = 3号位 写入/发布前冲突校验（注释已声明） |
| #6 DB schema 变更 2号位 专属，其余号位提交变更申请 | ✅ 本单为**申请**；落库执行归属见 §六-① 待 0号位 指定 |
| #5 接口即契约 | ✅ 三表为新增，不改既有接口；DomainSolveRequest 扩展走 S4 契约文档先行 |

---

## 五、与既有表关系

- `OperationTransitionRule` / `StageTransferTime`：`FK → RuleSetVersion(Id)`（复用冻结版本治理，同 SetupTransitionRule 先例）。
- `OperationPlanningParam`：**无 FK**（规划属性独立；`OperationCode` FK 待 2号位 确认主链口径后补）。
- 人工 Resource Slot（FINISH_SLOT_01~N）+ Calendar：**本单不建表**——裁决 §五/§六 明确由 APS 资源主数据侧维护、2号位 加载，资源/Calendar 表结构不在 3号位 治理范围。

---

## 六、待 0号位 确认项

| # | 确认项 | 3号位 建议 |
|---|---|---|
| ① | 落库执行归属（红线 #6） | 默认 **2号位** 执行；或参照 Setup v5.1.8 先例授权 **3号位 直接落地**（2026-09-17 后 Setup 规则 DDL 已由 3号位 落地） |
| ② | `OperationCode`/`StageCode` 是否补 FK 到 RoutingOperation/Stage 主链 | **暂不补**，待 2号位 确认主链口径后随 v5.2 再议（对齐 v5.1.8 延期先例） |
| ③ | `EffectiveVersion` 版本选取规则 | 取同码 **IsActive=1 最新版本**，随 S4 契约文档定案并备案 |

---

## 七、回执表

| 问 | 答复 |
|---|---|
| 三表结构与裁决口径是否一致 | （待 0号位） |
| 落库执行归属 ① | （待 0号位） |
| 主键 FK 口径 ② / 版本规则 ③ | （待 2号位） |

---

*批准后：生成《APS_数据库_v5.1.8到v5.1.9_无设备小工序_升级补丁.sql》→ 落库 dev → S2 治理模型实施。*
