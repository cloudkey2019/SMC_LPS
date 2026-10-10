-- ============================================================
-- APS_数据库 v5.1.8 → v5.1.9 升级补丁：无设备小工序与人工有限产能
-- 文件：RoutingOperation 加 OperationPlanningMode 列 + ResourceType 值域说明
-- 数据库：SQL Server 2019+ / APS_Production（Dapper 访问）
-- 依据：冻结 DDL v5.1.8.2《APS_数据库表结构设计_DDL_v5.1.8.2_最终可执行版_Setup换型规则_无设备小工序与人工有限产能_用户输出联合冻结版.sql》真实物理调整 1/2
--       + 0号位《APS_V1_1号位_3号位问题统一回复意见_v1.0_20260922.md》五.1 / 五.2
--       + S1《APS_V1_无设备小工序与人工有限产能模型_DDL变更申请_v5.1.9终版_v2.0_20260922.md》
-- 归属：3号位（规则/参数治理）提报；落库执行归属见文末声明（红线 #6）
-- 说明：
--   - 本补丁不新增任何表（0号位 五.1/五.2 定案）；仅执行冻结 DDL v5.1.8.2 已定义的物理调整；
--   - 唯一性主防线 = 业务层（红线 #3：不依赖 DB 约束保证完整性）；DEFAULT/CHECK 为工艺数据约束，非主防线；
--   - 幂等：IF COL_LENGTH / IF NOT EXISTS 保护，可重复执行。
-- ============================================================

-- -----------------------------------------------
-- 变更 1：RoutingOperation 增加 OperationPlanningMode（逐字对齐冻结 DDL v5.1.8.2）
-- 三态：FINITE_RESOURCE（默认，需资源有限产能）/ UNCONSTRAINED（无约束）/ WAIT_ONLY（仅等待占位）
-- 默认 FINITE_RESOURCE：既有 RoutingOperation 行自动落默认值，无数据迁移
-- -----------------------------------------------
IF COL_LENGTH('RoutingOperation', 'OperationPlanningMode') IS NULL
BEGIN
    ALTER TABLE RoutingOperation
    ADD OperationPlanningMode NVARCHAR(32) NOT NULL
        CONSTRAINT DF_RoutingOperation_OperationPlanningMode
        DEFAULT ('FINITE_RESOURCE');
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE name = 'CHK_RoutingOperation_PlanningMode'
)
BEGIN
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
END
GO

-- -----------------------------------------------
-- 变更 2：ResourceType 支持 HUMAN（对齐冻结 DDL v5.1.8.2 真实物理调整 2）
-- 人工资源复用 Resource + ResourceCalendar + OperationResourceEligibility，不新增 Human 专用资源表；
-- 经核对冻结 DDL：Resource.ResourceType 为 NVARCHAR(50) NOT NULL 自由文本
--   （值域 MACHINE / LINE / MANUAL_STATION 系列注释，无 CHECK 约束）→ HUMAN 直接可用，本补丁无物理 DDL；
-- 随本补丁同步的文档动作：
--   - Resource 表 ResourceType 列注释补 HUMAN（值域说明）；
--   - ODS 契约视图 ext_MES_APS_Resource_View 值域文档补 HUMAN。
-- -----------------------------------------------
-- （无 DDL 语句——见上方说明；ResourceType 非约束列，HUMAN 作为合法值直接写入）

-- ============================================================
-- 落库执行归属声明（红线 #6）
-- 执行归属定案（2026-09-22 收口）：本补丁由 2号位 执行（红线 #6：DB 结构变更 2号位 专属，
--   其他号位提交变更申请；3号位 为提报方）。DDL 内容已由 0号位 统一回复意见 五.1/五.2 裁决。
-- 不再等待「确认①」（撤销）。执行：连接 APS_Production → 运行本脚本（幂等，可重入）。
--
-- OPM 数据来源（0号位 2026-09-23 裁决）：
--   MES/ODS 源侧无 OperationPlanningMode 列 → 撤销 ODS 视图透列 + sp_SyncRoutingData MERGE（原登记待办作废）；
--   主路径 = APS 治理侧直维护：3号位 S2/S3 治理 API → 4号位 配置页面 → 业务拍板值
--   → 写入 [RoutingOperation].[OperationPlanningMode]；OPM 不依赖 MES/ODS 供给。
-- ============================================================
