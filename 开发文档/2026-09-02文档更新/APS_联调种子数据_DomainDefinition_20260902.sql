-- =====================================================================
-- APS 联调种子数据：DomainDefinition（域定义，绑定现有库真实主数据）
-- 日期：2026-09-02（修订：种子数据从现有数据库真实主数据读取，去除合成演示主数据）
-- 作者：3号位（任务④d DomainDefinition 种子数据，3号位/PM 提供）
--
-- 用途：供 2号位 真实归域执行时读取（DomainDefinition 是 APS 稳定排程域的唯一治理来源）
--   · 真实归域：2号位 按 DomainKey 将订单/资源/计划版本归入稳定域并逐 Domain 串行执行
--   · B-1 白天候选运行：3号位 创建候选运行时的 DomainKey 亦取自本治理配置
--
-- 域设计：按 0号位 2026-09-01《Domain定义分域计算与运行边界业务裁决》§2.2 模板
--   （1 全厂级 FAMILY + 同一产品族跨 BJ/TJ 两个 FACTORY_FAMILY），
--   DomainKey 保留裁决字面示例键，绑定真实主数据（3号位/PM 与用户 2026-09-02 确认）：
--       FAMILY_X    = FAMILY         / ProductFamily=CYL（气缸，「三个量产工厂全部」）/ Factory=NULL
--       BJ_FAMILY_Y = FACTORY_FAMILY / ProductFamily=FRL（三联件，「包括BJ和TJ全部」）/ Factory=BJ（北京工厂）
--       TJ_FAMILY_Y = FACTORY_FAMILY / ProductFamily=FRL（三联件）/ Factory=TJ（天津工厂）
--
-- 目标库：APS_Production（仅数据 INSERT，无 DDL 变更，红线 #6 合规）
-- 数据来源：现有库真实主数据（ProductFamily / Factory），按 Code 只读查询取 Id，不 INSERT 主数据
-- 前置条件：DomainDefinition 表已由 2号位 按 v5.1.4 迁移创建（2026-09-02 实测已存在、为空）；
--           真实主数据 CYL / FRL / BJ / TJ 须已在库（脚本内置存在性校验，缺失则终止）。
-- 幂等性：按 DomainDefinition.DomainKey IF NOT EXISTS 守卫，可重复执行。
--
-- DomainName 为 3号位 拟定业务口径，正式命名请 PM 确认后由 3号位 更新本脚本重跑（幂等，重跑仅补新增）。
-- =====================================================================

SET NOCOUNT ON;

-- ---- 前置检查 1：DomainDefinition 物理表须已存在（v5.1.4 迁移前置）----
IF OBJECT_ID(N'dbo.DomainDefinition', N'U') IS NULL
BEGIN
    PRINT N'[前置检查失败] 表 dbo.DomainDefinition 不存在。';
    PRINT N'请 2号位 先按 APS_数据库表结构设计_v5.1.4 迁移建表后，再执行本种子脚本。本脚本已终止，未写入任何数据。';
    RETURN;
END

DECLARE @cylId INT;   -- 产品族 CYL（气缸）
DECLARE @frlId INT;   -- 产品族 FRL（三联件）
DECLARE @bjId  INT;   -- 工厂 BJ（北京工厂）
DECLARE @tjId  INT;   -- 工厂 TJ（天津工厂）

-- ---- 前置检查 2：从现有库真实主数据只读读取 Id（不 INSERT 主数据）----
SELECT @cylId = Id FROM ProductFamily WHERE Code = N'CYL';
SELECT @frlId = Id FROM ProductFamily WHERE Code = N'FRL';
SELECT @bjId  = Id FROM Factory      WHERE Code = N'BJ';
SELECT @tjId  = Id FROM Factory      WHERE Code = N'TJ';

IF @cylId IS NULL OR @frlId IS NULL OR @bjId IS NULL OR @tjId IS NULL
BEGIN
    PRINT N'[前置检查失败] 现有库真实主数据缺失，请先同步主数据后重跑：';
    PRINT N'  ProductFamily Code=CYL  ' + CASE WHEN @cylId IS NULL THEN N'缺失' ELSE N'OK' END;
    PRINT N'  ProductFamily Code=FRL  ' + CASE WHEN @frlId IS NULL THEN N'缺失' ELSE N'OK' END;
    PRINT N'  Factory      Code=BJ   ' + CASE WHEN @bjId  IS NULL THEN N'缺失' ELSE N'OK' END;
    PRINT N'  Factory      Code=TJ   ' + CASE WHEN @tjId  IS NULL THEN N'缺失' ELSE N'OK' END;
    PRINT N'本脚本已终止，未写入任何数据。';
    RETURN;
END

-- =====================================================================
-- 1) DomainDefinition（3 条稳定域，绑定真实主数据 Id）
-- =====================================================================
-- ---- FAMILY_X：全厂级单产品族域（FAMILY，绑定 CYL 气缸；FactoryId 必须 NULL，CK 强制）----
IF NOT EXISTS (SELECT 1 FROM DomainDefinition WHERE DomainKey = N'FAMILY_X')
BEGIN
    INSERT INTO DomainDefinition (DomainKey, DomainName, ScopeType, ProductFamilyId, FactoryId, IsActive, SortOrder, CreatedBy, CreatedAt, UpdatedBy, UpdatedAt)
    VALUES (N'FAMILY_X', N'气缸全厂域（联调）', N'FAMILY', @cylId, NULL, 1, 100, N'3号位-seed', GETDATE(), N'3号位-seed', GETDATE());
END

-- ---- BJ_FAMILY_Y：北京工厂×三联件 域（FACTORY_FAMILY，FactoryId 必须非 NULL，CK 强制）----
IF NOT EXISTS (SELECT 1 FROM DomainDefinition WHERE DomainKey = N'BJ_FAMILY_Y')
BEGIN
    INSERT INTO DomainDefinition (DomainKey, DomainName, ScopeType, ProductFamilyId, FactoryId, IsActive, SortOrder, CreatedBy, CreatedAt, UpdatedBy, UpdatedAt)
    VALUES (N'BJ_FAMILY_Y', N'北京厂三联件域（联调）', N'FACTORY_FAMILY', @frlId, @bjId, 1, 110, N'3号位-seed', GETDATE(), N'3号位-seed', GETDATE());
END

-- ---- TJ_FAMILY_Y：天津工厂×三联件 域（FACTORY_FAMILY）----
IF NOT EXISTS (SELECT 1 FROM DomainDefinition WHERE DomainKey = N'TJ_FAMILY_Y')
BEGIN
    INSERT INTO DomainDefinition (DomainKey, DomainName, ScopeType, ProductFamilyId, FactoryId, IsActive, SortOrder, CreatedBy, CreatedAt, UpdatedBy, UpdatedAt)
    VALUES (N'TJ_FAMILY_Y', N'天津厂三联件域（联调）', N'FACTORY_FAMILY', @frlId, @tjId, 1, 120, N'3号位-seed', GETDATE(), N'3号位-seed', GETDATE());
END

-- =====================================================================
-- 2) 验证查询：落库结果确认（可重复执行查看）
-- =====================================================================
SELECT d.Id, d.DomainKey, d.DomainName, d.ScopeType,
       pf.Code AS ProductFamilyCode, pf.Name AS ProductFamilyName,
       f.Code AS FactoryCode, f.Name AS FactoryName,
       d.IsActive, d.SortOrder
FROM DomainDefinition d
JOIN ProductFamily pf ON pf.Id = d.ProductFamilyId
LEFT JOIN Factory f    ON f.Id = d.FactoryId
ORDER BY d.SortOrder, d.DomainKey;
