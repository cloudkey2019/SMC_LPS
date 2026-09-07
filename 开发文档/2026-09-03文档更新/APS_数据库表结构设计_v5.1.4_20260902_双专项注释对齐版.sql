-- APS数据库表结构设计 v5.1.4
-- 20260902 双专项注释对齐版
--
-- 替换关系：
-- 原：APS_数据库表结构设计_v5.1.4
-- 本次仅同步业务注释，不改变DDL结构。
--
-- 重要说明：
-- 1. 不新增表；
-- 2. 不新增字段；
-- 3. 不修改主键、外键；
-- 4. 本次调整仅用于统一Domain Resolve、
--    ProductionInstructionPositionSnapshot、
--    SupplyFact边界语义。

------------------------------------------------------------
-- 1. Order.DomainKey
------------------------------------------------------------

-- Order.DomainKey:
-- 表示当前ScheduleRun中APS执行Domain Resolve后的运行归域结果。
--
-- 不代表：
-- 1. ERP来源字段；
-- 2. ODS原始字段；
-- 3. ProductFamily.Code映射字段；
-- 4. 永久业务主数据。

------------------------------------------------------------
-- 2. ProductionInstructionPositionSnapshot
------------------------------------------------------------

-- 定位：
-- ProductionInstructionPositionSnapshot表示某次
-- ScheduleRun / PlanVersion中ProductionInstruction
-- 剩余数量的位置事实快照。
--
-- 关系：
--
-- ProductionInstruction Supply
--          |
--          +---- ProductionInstructionPositionSnapshot
--
-- Position不是独立Supply。

------------------------------------------------------------
-- 3. PositionType
------------------------------------------------------------

-- FIRST_STAGE_PENDING:
-- 首工序待开始
--
-- STAGE_WAITING:
-- Stage等待
--
-- XC:
-- 生产指示级XC位置
--
-- INTERPLANT_TRANSIT:
-- 生产指示跨厂途中位置
--
-- 注意：
-- INTERPLANT_TRANSIT属于Position状态，
-- 不属于Transit Supply。

------------------------------------------------------------
-- 4. Quantity约束
------------------------------------------------------------

-- Position.Quantity表示ERP ProductionInstruction
-- RemainingQty在不同Position中的互斥分布。
--
-- 必须满足：
--
-- SUM(Position.Quantity)
-- =
-- ERP PI RemainingQty
--
-- 禁止：
-- 1. Position重新计算PI剩余量；
-- 2. Transit重复累计数量；
-- 3. Received重复扣减。

------------------------------------------------------------
-- 5. SupplyFact_Pipeline
------------------------------------------------------------

-- SupplyFact_Pipeline当前V1定位：
-- 外部未来供给事实管道。
--
-- 包括：
-- 1. 采购在途；
-- 2. 未结PO剩余；
-- 3. 已到厂未入库。
--
-- 不作为Pipeline Supply：
-- 1. ProductionInstructionPositionSnapshot；
-- 2. PI XC；
-- 3. PI INTERPLANT_TRANSIT；
-- 4. SH Transit；
-- 5. SH Received。

------------------------------------------------------------
-- 6. DomainDefinition
------------------------------------------------------------

-- DomainDefinition属于APS治理配置。
--
-- 3号位负责维护；
-- ScheduleRun期间冻结配置语义；
-- 2号位执行Domain Resolve。

------------------------------------------------------------
-- END
------------------------------------------------------------
