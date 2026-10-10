-- =====================================================================
-- APS 联调种子数据：参数集 / 策略包 出新版本（PS-DEMO-V3 → SP-DEMO-V3.0）
-- 日期：2026-09-20
-- 作者：3号位（参数集治理 / P1-02 三预算）
-- 依据：2号位《夜间FULL方向口径 Q-C回执 v1.0 2026-09-20》§四——PS-DEMO-V2 的
--       $.SolverStrategy.Setup 仍为旧属性式换型字段（Dimensions / DefaultSetupMinutes /
--       SetupLookAheadSize 三死键），且缺 2026-09-20 新预算参数
--       SetupSearchBudget / SetupMaxNeighborhoodTries（1号位 已提值 500 / 50）。
-- 动作：按治理口径出 PUBLISHED 新版本（不改已冻结 V2 快照，保留审计差异）；
--       新 SP-DEMO-V3.0 打 IsDefault=1 接棒，旧 SP-DEMO-V2.0 收口 IsDefault=0 + 关窗口；
--       顺带剔除已摘除的 $.SolverStrategy.MaxIterations 死键（工程阈值归 1号位，见 2026-09-16 摘除交接），
--       使新版本 JSON 与 SolverStrategyBlock 当前字段集对齐。
--
-- 目标库：APS_Production（仅数据 INSERT/UPDATE，无 DDL 变更，红线 #6 合规）
-- Schema：v5.1.2 冻结对齐版（ContentSnapshotJson 方案 A 落点）
-- 幂等：按 (ParameterSetId, VersionCode) / (StrategyProfileId, VersionCode) IF NOT EXISTS 守卫；
--       V3 快照由现存 V2 的 ContentSnapshotJson 用 JSON_MODIFY 仅替换 $.SolverStrategy.Setup，
--       故对「V2 是否已改键」均稳健（已改则等价无操作）。
-- 前置：已执行《APS_联调种子数据_策略包_20260901.sql》（存在 PS_DEMO / RS-DEMO-V2 / SP_DEMO）。
-- ⚠️ PRODUCTION MUST REMOVE ⚠️ 仅供 dev 联调；生产严禁执行。
-- =====================================================================

SET NOCOUNT ON;

DECLARE @parameterSetId    BIGINT;
DECLARE @ruleSetV2Id       BIGINT;
DECLARE @strategyProfileId BIGINT;
DECLARE @paramV2Id         BIGINT;
DECLARE @paramV3Id         BIGINT;
DECLARE @v2Snapshot        NVARCHAR(MAX);
DECLARE @v3Snapshot        NVARCHAR(MAX);

SELECT @parameterSetId    = Id FROM ParameterSet     WHERE ParameterSetCode   = N'PS_DEMO';
SELECT @ruleSetV2Id       = Id FROM RuleSetVersion   WHERE VersionCode        = N'RS-DEMO-V2';
SELECT @strategyProfileId = Id FROM StrategyProfile  WHERE StrategyProfileCode = N'SP_DEMO';
SELECT @paramV2Id = Id, @v2Snapshot = ContentSnapshotJson
  FROM ParameterSetVersion
 WHERE ParameterSetId = @parameterSetId AND VersionCode = N'PS-DEMO-V2';

-- =====================================================================
-- 1) 由现 V2 快照派生出 V3 快照：
--    a. 替换 $.SolverStrategy.Setup 为新预算参数；
--    b. 剔除已摘除的 $.SolverStrategy.MaxIterations 死键（工程阈值归 1号位，见 2026-09-16 摘除交接）。
-- =====================================================================
SET @v3Snapshot = JSON_MODIFY(
    @v2Snapshot,
    N'$.SolverStrategy.Setup',
    JSON_QUERY(N'{"SetupSearchBudget":500,"SetupMaxNeighborhoodTries":50}'));

SET @v3Snapshot = JSON_MODIFY(@v3Snapshot, N'$.SolverStrategy.MaxIterations', NULL);

-- =====================================================================
-- 2) 新建参数集版本 PS-DEMO-V3（PUBLISHED，生效自当前时刻起）
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM ParameterSetVersion WHERE ParameterSetId = @parameterSetId AND VersionCode = N'PS-DEMO-V3')
BEGIN
    INSERT INTO ParameterSetVersion (
        ParameterSetId, VersionCode, Status,
        EffectiveFrom, EffectiveTo, PublishedAt, PublishedBy, ApprovedAt, ApprovedBy,
        CreatedAt, CreatedBy, Remarks, ContentSnapshotJson)
    VALUES (
        @parameterSetId, N'PS-DEMO-V3', N'PUBLISHED',
        GETDATE(), NULL,
        GETDATE(), N'seed', GETDATE(), N'seed',
        GETDATE(), N'seed',
        N'换型子块改读新预算参数 SetupSearchBudget=500 / SetupMaxNeighborhoodTries=50（替换旧 Dimensions/DefaultSetupMinutes/SetupLookAheadSize）',
        @v3Snapshot);
    SET @paramV3Id = SCOPE_IDENTITY();
END
ELSE
BEGIN
    SELECT @paramV3Id = Id FROM ParameterSetVersion WHERE ParameterSetId = @parameterSetId AND VersionCode = N'PS-DEMO-V3';
END

-- =====================================================================
-- 3) 旧 PS-DEMO-V2 生效窗口收口（保留 PUBLISHED 历史，仅关窗口）
-- =====================================================================
UPDATE ParameterSetVersion
   SET EffectiveTo = GETDATE()
 WHERE ParameterSetId = @parameterSetId
   AND VersionCode = N'PS-DEMO-V2'
   AND EffectiveTo IS NULL;

-- =====================================================================
-- 4) 旧 SP-DEMO-V2.0 退默认（先满足 UQ_StrategyProfileVersion_DefaultPublished），并关窗口
-- =====================================================================
UPDATE StrategyProfileVersion
   SET IsDefault = 0, EffectiveTo = GETDATE()
 WHERE StrategyProfileId = @strategyProfileId
   AND VersionCode = N'SP-DEMO-V2.0';

-- =====================================================================
-- 5) 新策略包版本 SP-DEMO-V3.0（同规则集 RS-DEMO-V2 + 新参数集 PS-DEMO-V3，IsDefault=1）
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM StrategyProfileVersion WHERE StrategyProfileId = @strategyProfileId AND VersionCode = N'SP-DEMO-V3.0')
BEGIN
    INSERT INTO StrategyProfileVersion (
        StrategyProfileId, VersionCode, RuleSetVersionId, ParameterSetVersionId, Status,
        EffectiveFrom, EffectiveTo, IsDefault, PublishedAt, PublishedBy, ApprovedAt, ApprovedBy,
        CreatedAt, CreatedBy)
    VALUES (
        @strategyProfileId, N'SP-DEMO-V3.0', @ruleSetV2Id, @paramV3Id, N'PUBLISHED',
        GETDATE(), NULL, 1,
        GETDATE(), N'seed', GETDATE(), N'seed',
        GETDATE(), N'seed');
END

-- =====================================================================
-- 6) 校验回显
-- =====================================================================
PRINT N'—— 策略包出新版本概览（PS-DEMO-V3 / SP-DEMO-V3.0）——';
SELECT s.StrategyProfileCode,
       sv.VersionCode   AS SpVersion,
       sv.IsDefault,
       pv.VersionCode   AS PsVersion,
       pv.Status        AS PsStatus,
       JSON_VALUE(pv.ContentSnapshotJson, N'$.SolverStrategy.Setup.SetupSearchBudget')        AS SetupSearchBudget,
       JSON_VALUE(pv.ContentSnapshotJson, N'$.SolverStrategy.Setup.SetupMaxNeighborhoodTries') AS SetupMaxNeighborhoodTries
FROM StrategyProfileVersion sv
INNER JOIN StrategyProfile s      ON s.Id  = sv.StrategyProfileId
INNER JOIN ParameterSetVersion pv ON pv.Id = sv.ParameterSetVersionId
WHERE s.StrategyProfileCode = N'SP_DEMO'
ORDER BY sv.VersionCode;