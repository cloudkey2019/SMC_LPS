-- =====================================================================
-- APS 联调种子数据：策略包（1 套，2 代版本）
-- 日期：2026-09-01
-- 作者：3号位（清单二-7 联调种子数据准备）
--
-- 用途：供 4号位 联调直接取数
--   · Diff 页      —— 两代版本（V1 基线 / V2 当前）内容差异
--   · 当前版本页    —— published-version 便捷查询解析出唯一生效 V2（生效窗口过滤）
--   · 候选/版本页   —— 策略包版本列表（含两代 PUBLISHED）
--
-- 目标库：APS_Production（仅数据 INSERT，无 DDL 变更，红线 #6 合规）
-- Schema：v5.1.2 冻结对齐版（含 ContentSnapshotJson 列，方案 A 落点）
-- 幂等性：按唯一编码（RuleSetCode / ParameterSetCode / StrategyProfileCode）
--         及 (SetId, VersionCode) IF NOT EXISTS 守卫，可重复执行
--
-- 六块分布（契约 §6.10.5）：
--   ① DemandPriority        → RuleSetVersion.ContentSnapshotJson
--   ② Lock ③ Supply ④ Procurement ⑤ SolverStrategy ⑥ CandidateGuardrail
--                            → ParameterSetVersion.ContentSnapshotJson
-- 序列化约定：系统默认 STJ（无 JsonStringEnumConverter），属性名 PascalCase、
--   枚举值序列化为数字（见 FrozenStrategySnapshot.cs / GovernanceVersionService.JsonOptions）。
-- =====================================================================

SET NOCOUNT ON;

DECLARE @ruleSetId         BIGINT;
DECLARE @parameterSetId    BIGINT;
DECLARE @strategyProfileId BIGINT;
DECLARE @ruleSetV1Id       BIGINT;
DECLARE @ruleSetV2Id       BIGINT;
DECLARE @paramV1Id         BIGINT;
DECLARE @paramV2Id         BIGINT;

-- =====================================================================
-- 1) 规则集主表（联调演示规则集）
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM RuleSet WHERE RuleSetCode = N'RS_DEMO')
BEGIN
    INSERT INTO RuleSet (RuleSetCode, RuleSetName, Description, IsActive, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
    VALUES (N'RS_DEMO', N'联调演示规则集', N'供4号位联调取数（Diff/当前版本/候选页），清单二-7 种子', 1, GETDATE(), N'seed', NULL, NULL);
    SET @ruleSetId = SCOPE_IDENTITY();
END
ELSE
BEGIN
    SELECT @ruleSetId = Id FROM RuleSet WHERE RuleSetCode = N'RS_DEMO';
END

-- ---- RS-DEMO-V1（基线，生效窗口已过期：仅作 Diff 基准，不影响当前版本解析）----
IF NOT EXISTS (SELECT 1 FROM RuleSetVersion WHERE RuleSetId = @ruleSetId AND VersionCode = N'RS-DEMO-V1')
BEGIN
    INSERT INTO RuleSetVersion (
        RuleSetId, VersionCode, Status,
        EffectiveFrom, EffectiveTo, PublishedAt, PublishedBy, ApprovedAt, ApprovedBy,
        CreatedAt, CreatedBy, Remarks, ContentSnapshotJson)
    VALUES (
        @ruleSetId, N'RS-DEMO-V1', N'PUBLISHED',
        DATEADD(day, -60, GETDATE()), DATEADD(day, -30, GETDATE()),
        DATEADD(day, -30, GETDATE()), N'seed', DATEADD(day, -60, GETDATE()), N'seed',
        DATEADD(day, -60, GETDATE()), N'seed', N'基线：延期销售订单优先（冻结示例口径）',
        N'{"DemandPriority":{"Segments":[' +
        N'{"SegmentOrder":1,"SegmentName":"延期销售订单","IsEnabled":true,' +
        N'"MatchConditions":[{"Field":1,"Operator":0,"Value":"Delayed"},{"Field":3,"Operator":0,"Value":"SO"}],' +
        N'"SortFields":[{"Field":6,"Direction":0},{"Field":2,"Direction":1},{"Field":7,"Direction":0}],' +
        N'"StableTieBreakFields":["OrderId"]},' +
        N'{"SegmentOrder":2,"SegmentName":"常规订单","IsEnabled":true,' +
        N'"MatchConditions":[],' +
        N'"SortFields":[{"Field":6,"Direction":0}],' +
        N'"StableTieBreakFields":["OrderId"]}' +
        N']}}');
    SET @ruleSetV1Id = SCOPE_IDENTITY();
END
ELSE
BEGIN
    SELECT @ruleSetV1Id = Id FROM RuleSetVersion WHERE RuleSetId = @ruleSetId AND VersionCode = N'RS-DEMO-V1';
END

-- ---- RS-DEMO-V2（当前生效，窗口自 -30 天起无上限）----
-- 与 V1 差异：新增"VIP 订单"Segment（CustomerTier=VIP）→ Diff 可展示段增
IF NOT EXISTS (SELECT 1 FROM RuleSetVersion WHERE RuleSetId = @ruleSetId AND VersionCode = N'RS-DEMO-V2')
BEGIN
    INSERT INTO RuleSetVersion (
        RuleSetId, VersionCode, Status,
        EffectiveFrom, EffectiveTo, PublishedAt, PublishedBy, ApprovedAt, ApprovedBy,
        CreatedAt, CreatedBy, Remarks, ContentSnapshotJson)
    VALUES (
        @ruleSetId, N'RS-DEMO-V2', N'PUBLISHED',
        DATEADD(day, -30, GETDATE()), NULL,
        DATEADD(day, -30, GETDATE()), N'seed', DATEADD(day, -30, GETDATE()), N'seed',
        DATEADD(day, -30, GETDATE()), N'seed', N'当前：新增 VIP 订单优先段',
        N'{"DemandPriority":{"Segments":[' +
        N'{"SegmentOrder":1,"SegmentName":"延期销售订单","IsEnabled":true,' +
        N'"MatchConditions":[{"Field":1,"Operator":0,"Value":"Delayed"},{"Field":3,"Operator":0,"Value":"SO"}],' +
        N'"SortFields":[{"Field":6,"Direction":0},{"Field":2,"Direction":1},{"Field":7,"Direction":0}],' +
        N'"StableTieBreakFields":["OrderId"]},' +
        N'{"SegmentOrder":2,"SegmentName":"VIP订单","IsEnabled":true,' +
        N'"MatchConditions":[{"Field":2,"Operator":0,"Value":"VIP"}],' +
        N'"SortFields":[{"Field":6,"Direction":0}],' +
        N'"StableTieBreakFields":["OrderId"]},' +
        N'{"SegmentOrder":3,"SegmentName":"常规订单","IsEnabled":true,' +
        N'"MatchConditions":[],' +
        N'"SortFields":[{"Field":6,"Direction":0}],' +
        N'"StableTieBreakFields":["OrderId"]}' +
        N']}}');
    SET @ruleSetV2Id = SCOPE_IDENTITY();
END
ELSE
BEGIN
    SELECT @ruleSetV2Id = Id FROM RuleSetVersion WHERE RuleSetId = @ruleSetId AND VersionCode = N'RS-DEMO-V2';
END

-- =====================================================================
-- 2) 参数集主表（联调演示参数集）
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM ParameterSet WHERE ParameterSetCode = N'PS_DEMO')
BEGIN
    INSERT INTO ParameterSet (ParameterSetCode, ParameterSetName, Description, IsActive, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
    VALUES (N'PS_DEMO', N'联调演示参数集', N'供4号位联调取数（Diff/当前版本/候选页），清单二-7 种子', 1, GETDATE(), N'seed', NULL, NULL);
    SET @parameterSetId = SCOPE_IDENTITY();
END
ELSE
BEGIN
    SELECT @parameterSetId = Id FROM ParameterSet WHERE ParameterSetCode = N'PS_DEMO';
END

-- ---- PS-DEMO-V1（基线，五子块真实样例）----
IF NOT EXISTS (SELECT 1 FROM ParameterSetVersion WHERE ParameterSetId = @parameterSetId AND VersionCode = N'PS-DEMO-V1')
BEGIN
    INSERT INTO ParameterSetVersion (
        ParameterSetId, VersionCode, Status,
        EffectiveFrom, EffectiveTo, PublishedAt, PublishedBy, ApprovedAt, ApprovedBy,
        CreatedAt, CreatedBy, Remarks, ContentSnapshotJson)
    VALUES (
        @parameterSetId, N'PS-DEMO-V1', N'PUBLISHED',
        DATEADD(day, -60, GETDATE()), DATEADD(day, -30, GETDATE()),
        DATEADD(day, -30, GETDATE()), N'seed', DATEADD(day, -60, GETDATE()), N'seed',
        DATEADD(day, -60, GETDATE()), N'seed', N'基线：五子块默认参数',
        N'{"Lock":{"Trigger":{"UseRemainingTimeThreshold":true,"RemainingTimeThresholdHours":24.0,"ProtectDelayed":true,"ProtectVipTier":true,"VipTierValue":"VIP","ProtectedOrderTypes":["SO"],"AllowPmcManualProtection":true},"Sticky":{"RequireReleaseRecord":true,"ProtectUntilCompletion":true,"ProtectUntilSupplyInvalid":false}},' +
        N'"Supply":{"Inventory":{"IsEnabled":true,"WarehousePriority":["WH01","WH02"],"RequireFactoryContext":true,"RequireProductFamilyContext":false},"PiSort":{"SortBy":0,"UseStablePiNoTieBreak":true}},' +
        N'"Procurement":{"WarehousePriority":["WH01","WH02"],' +
        N'"DefaultPurchaseLt":[{"WarehouseCode":"WH01","DefaultLtDays":3.0},{"WarehouseCode":"WH02","DefaultLtDays":5.0}],' +
        N'"OverdueMargin":{"MarginPercent":0.10,"MinimumExtraDays":2},' +
        N'"ArrivalToUsableOffsets":[{"WarehouseCode":"WH01","OffsetHours":4.0},{"WarehouseCode":"WH02","OffsetHours":6.0}],' +
        N'"PlanningYields":[{"MaterialId":"MAT001","YieldPercent":95.0}]},' +
        N'"SolverStrategy":{"Mode":0,"BottleneckMode":0,"OnTimeTarget":{"TargetPercent":90,"IsPrimaryObjective":true},' +
        N'"Split":{"MaxOptimizationSplitCount":3,"LimitMandatorySplit":false,"MinBatchQty":1.0},' +
        N'"Setup":{"Dimensions":["Mold","Tool"],"DefaultSetupMinutes":30.0,"SetupLookAheadSize":5},' +
        N'"StageOverlap":{"AllowOverlap":false,"TransferBatchQty":100.0,"ThresholdQty":50.0,"ThresholdPercent":0.5},' +
        N'"AllowMerge":false},' +
        N'"CandidateGuardrail":{"NormalMs":60000,"SoftMs":90000,"LocalHardMs":180000,"ImpactedTaskWarningPercent":30,"MaxRepairAttempts":5,"MaxPropagationRounds":10,"ResourceTopN":5,"SplitAlternatives":3,"WarnOnlyOnMaxImpacted":true}}');
    SET @paramV1Id = SCOPE_IDENTITY();
END
ELSE
BEGIN
    SELECT @paramV1Id = Id FROM ParameterSetVersion WHERE ParameterSetId = @parameterSetId AND VersionCode = N'PS-DEMO-V1';
END

-- ---- PS-DEMO-V2（当前生效；与 V1 差异：OnTimeTarget 90→95、WH03 入库、MAT002 良率、Guardrail 45s）----
IF NOT EXISTS (SELECT 1 FROM ParameterSetVersion WHERE ParameterSetId = @parameterSetId AND VersionCode = N'PS-DEMO-V2')
BEGIN
    INSERT INTO ParameterSetVersion (
        ParameterSetId, VersionCode, Status,
        EffectiveFrom, EffectiveTo, PublishedAt, PublishedBy, ApprovedAt, ApprovedBy,
        CreatedAt, CreatedBy, Remarks, ContentSnapshotJson)
    VALUES (
        @parameterSetId, N'PS-DEMO-V2', N'PUBLISHED',
        DATEADD(day, -30, GETDATE()), NULL,
        DATEADD(day, -30, GETDATE()), N'seed', DATEADD(day, -30, GETDATE()), N'seed',
        DATEADD(day, -30, GETDATE()), N'seed', N'当前：OnTime 目标 95%、新增 WH03/良率',
        N'{"Lock":{"Trigger":{"UseRemainingTimeThreshold":true,"RemainingTimeThresholdHours":24.0,"ProtectDelayed":true,"ProtectVipTier":true,"VipTierValue":"VIP","ProtectedOrderTypes":["SO"],"AllowPmcManualProtection":true},"Sticky":{"RequireReleaseRecord":true,"ProtectUntilCompletion":true,"ProtectUntilSupplyInvalid":false}},' +
        N'"Supply":{"Inventory":{"IsEnabled":true,"WarehousePriority":["WH01","WH02","WH03"],"RequireFactoryContext":true,"RequireProductFamilyContext":false},"PiSort":{"SortBy":0,"UseStablePiNoTieBreak":true}},' +
        N'"Procurement":{"WarehousePriority":["WH01","WH02","WH03"],' +
        N'"DefaultPurchaseLt":[{"WarehouseCode":"WH01","DefaultLtDays":3.0},{"WarehouseCode":"WH02","DefaultLtDays":5.0},{"WarehouseCode":"WH03","DefaultLtDays":7.0}],' +
        N'"OverdueMargin":{"MarginPercent":0.10,"MinimumExtraDays":2},' +
        N'"ArrivalToUsableOffsets":[{"WarehouseCode":"WH01","OffsetHours":4.0},{"WarehouseCode":"WH02","OffsetHours":6.0},{"WarehouseCode":"WH03","OffsetHours":8.0}],' +
        N'"PlanningYields":[{"MaterialId":"MAT001","YieldPercent":95.0},{"MaterialId":"MAT002","YieldPercent":90.0}]},' +
        N'"SolverStrategy":{"Mode":0,"BottleneckMode":0,"OnTimeTarget":{"TargetPercent":95,"IsPrimaryObjective":true},' +
        N'"Split":{"MaxOptimizationSplitCount":3,"LimitMandatorySplit":false,"MinBatchQty":1.0},' +
        N'"Setup":{"SetupSearchBudget":500,"SetupMaxNeighborhoodTries":50},' +
        N'"StageOverlap":{"AllowOverlap":false,"TransferBatchQty":100.0,"ThresholdQty":50.0,"ThresholdPercent":0.5},' +
        N'"AllowMerge":false},' +
        N'"CandidateGuardrail":{"NormalMs":45000,"SoftMs":75000,"LocalHardMs":150000,"ImpactedTaskWarningPercent":30,"MaxRepairAttempts":5,"MaxPropagationRounds":10,"ResourceTopN":5,"SplitAlternatives":3,"WarnOnlyOnMaxImpacted":true}}');
    SET @paramV2Id = SCOPE_IDENTITY();
END
ELSE
BEGIN
    SELECT @paramV2Id = Id FROM ParameterSetVersion WHERE ParameterSetId = @parameterSetId AND VersionCode = N'PS-DEMO-V2';
END

-- =====================================================================
-- 3) 策略包主表（联调演示策略包，FULL_SCHEDULE）
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM StrategyProfile WHERE StrategyProfileCode = N'SP_DEMO')
BEGIN
    INSERT INTO StrategyProfile (StrategyProfileCode, StrategyProfileName, Description, RunType, IsActive, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
    VALUES (N'SP_DEMO', N'联调演示策略包', N'供4号位联调取数（Diff/当前版本/候选页），清单二-7 种子', N'FULL_SCHEDULE', 1, GETDATE(), N'seed', NULL, NULL);
    SET @strategyProfileId = SCOPE_IDENTITY();
END
ELSE
BEGIN
    SELECT @strategyProfileId = Id FROM StrategyProfile WHERE StrategyProfileCode = N'SP_DEMO';
END

-- ---- SP-DEMO-V1.0（基线策略包，非默认：仅作 Diff 基准）----
IF NOT EXISTS (SELECT 1 FROM StrategyProfileVersion WHERE StrategyProfileId = @strategyProfileId AND VersionCode = N'SP-DEMO-V1.0')
BEGIN
    INSERT INTO StrategyProfileVersion (
        StrategyProfileId, VersionCode, RuleSetVersionId, ParameterSetVersionId, Status,
        EffectiveFrom, EffectiveTo, IsDefault, PublishedAt, PublishedBy, ApprovedAt, ApprovedBy,
        CreatedAt, CreatedBy)
    VALUES (
        @strategyProfileId, N'SP-DEMO-V1.0', @ruleSetV1Id, @paramV1Id, N'PUBLISHED',
        DATEADD(day, -60, GETDATE()), DATEADD(day, -30, GETDATE()), 0,
        DATEADD(day, -30, GETDATE()), N'seed', DATEADD(day, -60, GETDATE()), N'seed',
        DATEADD(day, -60, GETDATE()), N'seed');
END

-- ---- SP-DEMO-V2.0（当前默认策略包：唯一 IsDefault=1 的 PUBLISHED，UQ_StrategyProfileVersion_DefaultPublished 约束）----
IF NOT EXISTS (SELECT 1 FROM StrategyProfileVersion WHERE StrategyProfileId = @strategyProfileId AND VersionCode = N'SP-DEMO-V2.0')
BEGIN
    INSERT INTO StrategyProfileVersion (
        StrategyProfileId, VersionCode, RuleSetVersionId, ParameterSetVersionId, Status,
        EffectiveFrom, EffectiveTo, IsDefault, PublishedAt, PublishedBy, ApprovedAt, ApprovedBy,
        CreatedAt, CreatedBy)
    VALUES (
        @strategyProfileId, N'SP-DEMO-V2.0', @ruleSetV2Id, @paramV2Id, N'PUBLISHED',
        DATEADD(day, -30, GETDATE()), NULL, 1,
        DATEADD(day, -30, GETDATE()), N'seed', DATEADD(day, -30, GETDATE()), N'seed',
        DATEADD(day, -30, GETDATE()), N'seed');
END

-- =====================================================================
-- 4) 校验回显：种子数据概览
-- =====================================================================
PRINT N'—— 联调种子数据概览（策略包）——';
SELECT s.StrategyProfileCode, sv.VersionCode AS SpVersion, sv.IsDefault,
       r.RuleSetCode, rv.VersionCode AS RsVersion, rv.Status AS RsStatus,
       p.ParameterSetCode, pv.VersionCode AS PsVersion, pv.Status AS PsStatus
FROM StrategyProfileVersion sv
INNER JOIN StrategyProfile s      ON s.Id = sv.StrategyProfileId
INNER JOIN RuleSetVersion rv      ON rv.Id = sv.RuleSetVersionId
INNER JOIN RuleSet r              ON r.Id = rv.RuleSetId
INNER JOIN ParameterSetVersion pv ON pv.Id = sv.ParameterSetVersionId
INNER JOIN ParameterSet p         ON p.Id = pv.ParameterSetId
WHERE s.StrategyProfileCode = N'SP_DEMO'
ORDER BY sv.VersionCode;
