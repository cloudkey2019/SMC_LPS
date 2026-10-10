# APS V1 排程归域同源核对 —— 3号位致 2号位

**版本**：v1.0
**日期**：2026-09-09（北京时间）
**致**：2号位
**性质**：依据 0号位 2026-09-07 F-G4 裁决（Q1附=必须同源），请 2号位 核对并回执：排程实际归域是否已以 DomainDefinition 为唯一权威源。本文只求核对，非代码实现、非越位。

---

## 一、核对背景

0号位 已裁（《F-G4业务范围过滤维度与权威映射源正式回复》2026-09-07）：

- ProductFamily → DomainKey 权威映射源 = **DomainDefinition**（唯一真源）；
- **Q1附：2号位 排程实际使用的 DomainKey 必须与 DomainDefinition 同源**——这是「必须执行」而非「待拍板」，异源即属代码向冻结基线整改。

当前：3号位 F-G4 侧已按「PlanVersion.DomainKey 为版本唯一权威域」落地（已退出 `FACTORY_{id}` 命名规则推导）。请 2号位 确认同源链路。

## 二、请 2号位 核对并回执

1. 排程归域（Order/Demand → DomainKey → 落 PlanVersion / Task 的 DomainKey / 分域结果）当前是否**读取当前有效的 DomainDefinition**（ScopeType=FAMILY / FACTORY_FAMILY，含 ProductFamilyId↔DomainKey）？
2. 是否存在独立于 DomainDefinition 的第二套 DomainKey 推导（如按 FactoryCode / ProductFamily.Code 命名规则拼接）？
3. 若异源：请回执整改排期；若同源：请确认即可。

## 三、回执表

| # | 请 2号位 答复 | 回执 |
|---|---|---|
| 1 | 排程归域是否消费 DomainDefinition | □ 同源 □ 异源 |
| 2 | 是否存在第二套 DomainKey 推导 | □ 无 □ 有（请说明） |
| 3 | 异源整改排期 | ____________ |

## 四、红线声明

3号位 不改 2号位 代码与数据；本单只求同源核对回执，用于关闭 F-G4 A1 线上最后一个验证依赖（影响 3号位 ProductFamily 范围过滤的端到端正确性）。

---

以上。请 2号位 回执 §三；同源确认后，A1 相关跨号位依赖即关闭。