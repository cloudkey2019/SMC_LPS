-- ============================================================
-- APS_数据库 v5.1.7 → v5.1.8 升级补丁：产品转换换型规则
-- 文件：SetupTransitionRule 建表 + 唯一性约束
-- 数据库：SQL Server 2019+ / APS_Production（Dapper 访问）
-- 依据：《APS V1 Setup换型规则与有限产能优化——冻结文档修改指导 v1.2（最终收口版，2026-09-16）》§九/§十
-- 归属：3号位（Setup规则治理）；2026-09-17 起 DDL 由 3号位 直接落地（不再等 2号位）
-- 说明：
--   - 规则随 RuleSetVersionId 冻结（FK → RuleSetVersion），复用现有规则版本治理；
--   - 唯一性主防线 = 3号位 发布前冲突校验；本文件过滤唯一索引仅 DB 兜底（红线 #3：不依赖 DB 约束保证完整性）；
--   - ResourceId / FromMaterialId / ToMaterialId 的 FKs 到 Resource/Material 主键口径待 2号位 确认后补。
-- ============================================================

-- -----------------------------------------------
-- 产品转换换型规则表
-- 明确规则（EXACT）：ProductionDepartmentId + StageCode + OperationCode + ResourceId
--                    + FromMaterialId + ToMaterialId 组合唯一（§十 明确规则）
-- 默认规则（DEFAULT）：ProductionDepartmentId + StageCode + OperationCode + ResourceId
--                    组合唯一（§十 默认规则），FromMaterialId/ToMaterialId 为 NULL
-- -----------------------------------------------
CREATE TABLE SetupTransitionRule (
    Id                     BIGINT IDENTITY(1,1) PRIMARY KEY,
    RuleSetVersionId       BIGINT        NOT NULL,
    ProductionDepartmentId INT           NOT NULL,
    StageCode              NVARCHAR(50)  NOT NULL,
    OperationCode          NVARCHAR(50)  NOT NULL,
    ResourceId             INT           NOT NULL,
    FromMaterialId         INT           NULL,
    ToMaterialId           INT           NULL,
    RuleType               NVARCHAR(20)  NOT NULL,
    SetupMinutes           DECIMAL(18,4) NOT NULL,
    IsActive               BIT           NOT NULL DEFAULT 1,
    CreatedAt              DATETIME2     NOT NULL DEFAULT GETDATE(),
    CreatedBy              NVARCHAR(100) NULL,
    UpdatedAt              DATETIME2     NULL,
    UpdatedBy              NVARCHAR(100) NULL,
    CONSTRAINT FK_SetupTransitionRule_RuleSetVersion
        FOREIGN KEY (RuleSetVersionId) REFERENCES RuleSetVersion(Id),
    CONSTRAINT CK_SetupTransitionRule_RuleType
        CHECK (RuleType IN ('EXACT','DEFAULT')),
    CONSTRAINT CK_SetupTransitionRule_SetupMinutes
        CHECK (SetupMinutes >= 0),
    CONSTRAINT CK_SetupTransitionRule_EXACT_Materials
        CHECK (RuleType <> 'EXACT' OR (FromMaterialId IS NOT NULL AND ToMaterialId IS NOT NULL)),
    CONSTRAINT CK_SetupTransitionRule_DEFAULT_Materials
        CHECK (RuleType <> 'DEFAULT' OR (FromMaterialId IS NULL AND ToMaterialId IS NULL))
);
GO

-- -----------------------------------------------
-- EXACT 唯一性：同一 RuleSetVersion 内，明确规则组合仅一条有效（§十 明确规则）
-- -----------------------------------------------
CREATE UNIQUE INDEX UQ_SetupTransitionRule_EXACT
ON SetupTransitionRule (
    RuleSetVersionId, ProductionDepartmentId, StageCode, OperationCode, ResourceId,
    FromMaterialId, ToMaterialId
)
WHERE RuleType = 'EXACT' AND IsActive = 1;
GO

-- -----------------------------------------------
-- DEFAULT 唯一性：同一 RuleSetVersion 内，默认规则组合仅一条有效（§十 默认规则）
-- -----------------------------------------------
CREATE UNIQUE INDEX UQ_SetupTransitionRule_DEFAULT
ON SetupTransitionRule (
    RuleSetVersionId, ProductionDepartmentId, StageCode, OperationCode, ResourceId
)
WHERE RuleType = 'DEFAULT' AND IsActive = 1;
GO

-- -----------------------------------------------
-- 查询辅助索引：按规则集版本 + 有效规则装载（§19.3 2号位 按本 Run 冻结版本装载）
-- -----------------------------------------------
CREATE INDEX IX_SetupTransitionRule_RuleSetVersion
ON SetupTransitionRule (RuleSetVersionId, IsActive, RuleType);
GO
