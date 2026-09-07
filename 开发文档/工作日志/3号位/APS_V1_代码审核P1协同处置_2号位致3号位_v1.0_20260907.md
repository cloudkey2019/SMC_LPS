# 派单：代码审核 P1 协同处置（补源催落 / Auth 退出 / DP 治理 / 种子值）—— 2号位 → 3号位

> 收件：3号位（规则/治理）
> 发件：2号位（Pegging + Scheduling）
> 依据：PM《2号位代码审核报告_Commit_de1386d_20260906》P1-05 / P1-06 / P1-09 + 2号位 核实
> 日期：2026-09-07

---

> **术语说明（先看这段）**：本单里的「P0-xx / P1-xx」是 PM 对 2号位 commit 的代码审核分级编号——P0=冻结冲突必须关、P1=能力/接口未闭合、P2=治理项。**你只需要看每条标题那句白话和后面的回执表**；带编号只是方便你回头到《PM 代码审核报告》里对号入座，不用管分级本身。下面四条，有三条要你交付/收口，一条是催提交 SVN。

---

## 结论一句话

四条里三条是要 3号位 交付/收口的，一条是催落 SVN。逐条如下。

---

## 一、P1-05 Solver Strategy 补源 —— 请先提交 SVN（催交付，非新任务）

3号位 回执（2026-09-04）说明代码侧已补 `SolverStrategyBlock.AllowMerge/MaxIterations` + `SolverStrategyValidator.ValidateMaxIterations` + 单测 + 种子 SQL + 契约 v0.2。

但 2号位 查实：**SVN 服务器仍在 r13307，`FrozenStrategySnapshot.cs` 里没有这两个字段**——即 3号位 的改动还只在本地、未提交。

请 3号位：
1. 把上述 batch **提交 SVN**；
2. 2号位 已本地补了同 spec 的 DTO 两字段（为解投影编译），提交时双方同 spec，`svn update` 后 2号位 取 3号位 版本即可（内容等价）。

---

## 二、P1-09 Auth / 4→2 HTTP 正式退出

### 现状（已核实）

代码库仍正式注册 Auth 链：`LPS.APS.Web/Controllers/AuthController.cs`、`LPS.APS.Engine/Data/AuthDbContext.cs`、`LPS.APS.Engine/Services/Auth/AuthService.cs`、`MapControllers()`。

PM 报告 P1-09 裁定：**Auth 整体已移交 3号位，2号位 不得继续作为 Auth Owner / 正式 Controller 注册方**；长期链路应为「普通页面 4→5→2、治理 4→3→2」。

### 请 3号位 给三样

1. **你侧 Auth 正式入口/路由的落点**（4→3 治理链路的具体 Controller/端点）；
2. **2号位 退出清单**——哪些 Controller / DI 注册 / 表 / 迁移可删（AuthDbContext 若为共用 Auth 库 DbContext，删前须确认 3号位 已持有同等的 Auth 库迁移能力）；
3. **切换时序**：先 3号位 接好再 2号位 退，还是并行切。

---

## 三、P1-06 Demand Protection Release 治理口径

报告要求 2号位 拥有 DP 数量真相（创建/消费/释放/Lock 一致性/最终释放业务资格），链 = 4→5→2。现状：`ValidateDemandProtection` 是简化 lock 校验，5号位 Release 未实现。

请 3号位 给 **Release 的业务资格规则**：谁、在什么条件下可释放（如：仅 ACTIVE、非 Candidate、持有人匹配、已无未结算 Allocation……）。2号位 据此建 Application Service。5号位 侧的 Release 事实值会另发 5号位，这里只差**治理口径**。

---

## 四、种子参数值确认（P0-07 / P1-02 的数据源已就绪，只差"灌值"）

2号位 已核实这两项**不需要新 DTO/新字段**，源就在冻结六块里、归 3号位 治理参数：

| PM 项 | 数据源（已存在） | 2号位 要算的 |
|---|---|---|
| P0-07 占位 AvailableTime | `frozenSnapshot.Procurement.DefaultPurchaseLt`（`FrozenStrategySnapshot.cs:152`） | `DataCutoffTime + DefaultLtDays + 偏移` |
| P1-02 Planning Yield | `frozenSnapshot.Procurement.PlanningYields`（`FrozenStrategySnapshot.cs:158`） | `NetOutputQty → PlannedProcessQty` 反算 |

请 3号位 确认：**PS-DEMO-V1 / PS-DEMO-V2 种子里这两组参数是否已灌非空值？** 若为空，2号位 改完代码也等于没算，这俩 P0/P1 会"改而无用"。请给具体值或灌值排期。

---

## 回执表

| # | 请 3号位 | 回执 |
|---|---|---|
| 一 | SolverStrategy 补源 batch 提交 SVN | □ 已提交（revision=____） |
| 二-1 | Auth 你侧正式入口落点 | 答复：____ |
| 二-2 | 2号位 退出清单（可删项） | 答复：____ |
| 二-3 | 切换时序 | □ 先接后退　□ 并行 |
| 三 | DP Release 业务资格规则 | 答复：____ |
| 四 | DefaultPurchaseLt / PlanningYields 种子是否已灌值 | □ 已灌　□ 未灌（排期：____） |