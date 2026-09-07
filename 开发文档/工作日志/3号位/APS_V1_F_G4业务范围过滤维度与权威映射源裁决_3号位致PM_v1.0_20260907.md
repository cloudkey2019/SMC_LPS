# 派单：F-G4 业务范围过滤维度与权威映射源裁决 —— 3号位 → 0号位（PM）

> 收件：0号位（PM，拍板权限过滤维度 + 权威映射源）
> 发件：3号位（应用编排 / 运行生命周期治理 / 认证权限）
> 抄送：2号位（Pegging + Scheduling）、5号位（ODS / ERP 同步侧事实）
> 依据：F-G4（业务范围权限过滤 §7.2）"待对齐"项 + MSC Q1/Q2 同源映射桥
> 日期：2026-09-07
> 性质：请求口径裁决，非代码实现请求；红线——不越位，宽放动作待口径回执后再落地。

---

## 结论一句话

F-G4 已完成的 **Domain / Factory 过滤是安全且正确的**；剩下 **ProductFamily（产品族）/ Department（生产部门）** 两个维度无法在"版本列表"层落盘表达。卡点不是代码，而是「权威映射源」与「维度范围」两个**业务口径**。其中 **Department 分支与 MSC Q1/Q2 是同一座"部门码↔域"映射桥**，建议并案一次性拍板，避免 PM 为同一座桥拍两次。

---

## 一、现状（先对齐现状，非 P0）

F-G4 业务范围过滤已上线（2026-09-04），`ScheduleQueryService.ResolveAllowedDomainKeys` 现状：

| 权限维度（DataScopeContext） | 版本列表映射 | 当前行为 |
|---|---|---|
| Global | 全放行 | ✅ done |
| Domain | 直用 DomainKey | ✅ done |
| Factory | 转 `FACTORY_{id}`（G2-b 档① 单工厂域） | ✅ done |
| **ProductFamily** | —（无映射） | ⛔ fail-closed 拒绝全部 |
| **Department** | —（无映射） | ⛔ fail-closed 拒绝全部 |

即：某用户若**只被授权了产品族或生产部门**（无 Domain/Factory 授权），其版本列表当前返回空。这是刻意为之的 **fail-closed 安全默认**，非 bug，也**不阻塞端到端转绿（P2）**，故**不紧急**。

---

## 二、请 PM 拍板两件事

### Q1：ProductFamily → DomainKey 的权威映射源，以谁为真源？

情景：从"某产品族"反查"属于哪些域"再过滤版本列表，需要一个**权威的「产品族→域」映射**。

| 选项 | 含义 | 后续 |
|---|---|---|
| **A（推荐）** | 以 3号位 `DomainDefinition`（E-1 治理表，ScopeType=FAMILY / FACTORY_FAMILY，含 ProductFamilyId↔DomainKey）为 V1 唯一真源 | 3号位 立即自驱放宽 ProductFamily 维度，零跨号位等待 |
| B | 真源在 ERP/ODS（5号位），APS 侧无该口径 | 转 5号位/2号位 补同步/映射表，3号位 只消费 |
| C | 其他（请说明） | — |

> 选 A 的前提：确认 2号位 排程实际打的 DomainKey 与 `DomainDefinition` 同源（否则两边口径不一致，过滤会漏/错）。此点请 PM 一并裁定或责成 2号位 回执。

### Q2：Department 是否作为「版本列表」过滤维度？其「部门→域」映射桥归属？

情景：`DataScopeContext` 有 Department 维度，但 Domain 裁决 §2.3 已定 **"Department 不是域维度"**，且全库无"部门→域"落盘映射——三处打架。

| 选项 | 含义 | 后续 |
|---|---|---|
| **A（推荐）** | V1 **不支持按部门过滤版本列表**；Department 权限维度只在 5号位 业务端点（概览/订单查询/PeggingTrace 等）生效 | 版本列表维持 reject-all，3号位 零改动收口 |
| B | 需要按部门过滤版本列表 | 必须建「部门→域」桥——**即 MSC Q1 那座"公司部门码↔生产单元"映射桥**，桥建成后 3号位 再接 |
| C | 其他（请说明） | — |

---

## 三、并案建议

- **Q2 若选 B**，本单直接**并入 MSC Q1/Q2 一并拍板**，不要为"部门↔域"这座桥在 MSC 与 F-G4 两处分别拍板。
- **Q2 选 A、Q1 选 A**，则本单在 3号位 内即可收口（Q1-A 自驱放宽 ProductFamily、Q2-A 维持 Department reject-all 并补一段注释固化），**无需 0号位 再投入**——仅需 PM 一句"同意 A/A"即可。

---

## 四、红线声明（非请求项）

3号位 不下发代码实现、不越位改 2号位/5号位 数据；本单只求**口径**。宽放动作（Q1 映射 / Q2 维度）待 PM 回执后，由对应 Owner 落地。

---

## 回执表

| # | 请 PM 答复 | 回执 |
|---|---|---|
| Q1 | ProductFamily → DomainKey 权威映射源 | □ A（DomainDefinition 真源）□ B（ERP/ODS）□ C：____ |
| Q1-附 | 排程 DomainKey 与 DomainDefinition 是否同源（责成 2号位 回执） | □ 同源 □ 异源（以____为准） |
| Q2 | Department 是否作为版本列表过滤维度 | □ A（不支持，端点在 5号位）□ B（支持，并入 MSC Q1 建桥）□ C：____ |
| 并案 | Q2 若 B，是否并入 MSC Q1/Q2 一并拍板 | □ 是 □ 否 |