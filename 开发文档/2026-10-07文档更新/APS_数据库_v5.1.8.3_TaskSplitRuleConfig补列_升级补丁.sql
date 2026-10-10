-- =============================================================================
-- TaskSplitRuleConfig 补列 · 升级补丁（Batch Policy 治理结构）
-- 依据：0号位 正式裁决（《未命名的Markdown文件 (4).md》，2026-10-07）
-- 归属：3号位（Batch Policy 治理 Owner）——DDL 按业务 Owner，3号位 直接落地（Setup 补丁先例）
-- 目标：dev 库 Schema 对齐正式 DDL v5.1.8.3（最小物理回写 L9779–9786），解除 4号位 真实联调 500
-- 边界：仅执行正式 DDL 已定义的幂等升级内容；不新增字段/表/索引语义（0号位 裁决 §五/§八）
-- 幂等：全部 IF COL_LENGTH(...) IS NULL 守卫 + 索引 drop-if-exists/create-if-not-exists，可重复执行
-- 源：开发文档/2026-10-07文档更新/APS_数据库表结构设计_DDL_v5.1.8.3_20261005_真实RoutePath_TaskMES_BatchPolicy最小物理回写版.sql L9779–9786、L4484
-- =============================================================================

USE APS_Production;
GO

-- ① 补 8 列（缺几补几；dev 实测缺前 5 列，后 3 列守卫自动跳过）
IF COL_LENGTH('dbo.TaskSplitRuleConfig','ProductionDepartmentId') IS NULL ALTER TABLE dbo.TaskSplitRuleConfig ADD ProductionDepartmentId INT NULL;
IF COL_LENGTH('dbo.TaskSplitRuleConfig','MinExecutionBatchQty') IS NULL ALTER TABLE dbo.TaskSplitRuleConfig ADD MinExecutionBatchQty DECIMAL(18,4) NULL;
IF COL_LENGTH('dbo.TaskSplitRuleConfig','MaxExecutionBatchQty') IS NULL ALTER TABLE dbo.TaskSplitRuleConfig ADD MaxExecutionBatchQty DECIMAL(18,4) NULL;
IF COL_LENGTH('dbo.TaskSplitRuleConfig','PreferredBatchQty') IS NULL ALTER TABLE dbo.TaskSplitRuleConfig ADD PreferredBatchQty DECIMAL(18,4) NULL;
IF COL_LENGTH('dbo.TaskSplitRuleConfig','AllowSplit') IS NULL ALTER TABLE dbo.TaskSplitRuleConfig ADD AllowSplit BIT NOT NULL CONSTRAINT DF_TaskSplitRuleConfig_AllowSplit_v5183 DEFAULT 1;
IF COL_LENGTH('dbo.TaskSplitRuleConfig','AllowMerge') IS NULL ALTER TABLE dbo.TaskSplitRuleConfig ADD AllowMerge BIT NOT NULL CONSTRAINT DF_TaskSplitRuleConfig_AllowMerge_v5183 DEFAULT 0;
IF COL_LENGTH('dbo.TaskSplitRuleConfig','MaxOptimizationSplitCount') IS NULL ALTER TABLE dbo.TaskSplitRuleConfig ADD MaxOptimizationSplitCount INT NULL;
IF COL_LENGTH('dbo.TaskSplitRuleConfig','MaxBatchCandidates') IS NULL ALTER TABLE dbo.TaskSplitRuleConfig ADD MaxBatchCandidates INT NULL;
GO

-- ② 索引核对：确保 IX_TaskSplitRuleConfig_Query = (MaterialId, ProductionDepartmentId, IsActive)，与正式 DDL L4484 一致
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TaskSplitRuleConfig_Query' AND object_id = OBJECT_ID('dbo.TaskSplitRuleConfig'))
    DROP INDEX IX_TaskSplitRuleConfig_Query ON dbo.TaskSplitRuleConfig;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TaskSplitRuleConfig_Query' AND object_id = OBJECT_ID('dbo.TaskSplitRuleConfig'))
    CREATE INDEX IX_TaskSplitRuleConfig_Query ON dbo.TaskSplitRuleConfig (MaterialId, ProductionDepartmentId, IsActive);
GO