# 3号位 致 4号位 — Setup create 500（CreatedAt 溢出）修复回执（方案 a 已落地 / 构建 0 错误）

> **发送人**：3号位（Governance 后端 / Setup 规则治理 Owner）
> **接收人**：4号位（前端）
> **抄送**：0号位（治理留档）
> **日期**：2026-09-20
> **触发**：《4号位-2026-09-20-Setup-create-500-CreatedAt溢出-给3号位.md》（§六 回执请求 1-3）
> **性质**：**修复回执**——§二 根因确认 + §三 方案 a 已落地，请 4号位 复跑闭环
> **效力**：以冻结文档为准

---

## 〇、一句话

**§二 500 根因确认，采纳 §三 方案 a：`SetupRuleService` 两条 create 路径（`CreateExactAsync` / `CreateDefaultAsync`）各补 1 行 `CreatedAt = DateTime.UtcNow` 已落地，构建 0 错误；更新路径与全库构造点已核查无同类残留。请 4号位 复跑 `GROUP=setup` 12 断言出数 → Setup 联调验收关闭。**

---

## 一、§二 500 根因确认（4号位 定位准确，全盘认可）

| 层 | 文件:行 | 确认 |
|---|---|---|
| ① 服务漏设 | `SetupRuleService.cs` `CreateExactAsync` / `CreateDefaultAsync` 初始化器 | **确认**：设 `CreatedBy` 漏设 `CreatedAt` |
| ② 实体非空 | `SetupTransitionRule.CreatedAt` 非空 `DateTime` | **确认**：漏设 = `0001-01-01`（合法值、永非 NULL） |
| ③ 仓储 ISNULL 兜底失效 | `SetupTransitionRuleRepository.cs:58` | **确认**：`ISNULL(@CreatedAt, SYSDATETIME())` 只认 SQL NULL，对 `0001-01-01` 不触发 → overflow |
| 旁证 | `UpdatedAt` 可空 → ISNULL 正常触发不溢出 | **确认**，反证根因唯一性 |

> 三层链推理与「非空 DateTime 实体 × ISNULL(NULL) 兜底」互相矛盾的结论**成立**；此为 3号位 服务层疏漏（补打戳即正），仓储 2号位 侧设计本身无误。

---

## 二、§三 方案 a 已落地（服务层 2 处各 +1 行）

`LPS.APS.Application/Services/SetupRuleService.cs`：

| 方法 | 行 | 改动 |
|---|---|---|
| `CreateExactAsync` | 初始化器内 | `+ CreatedAt = DateTime.UtcNow`（含注释说明根因） |
| `CreateDefaultAsync` | 初始化器内 | `+ CreatedAt = DateTime.UtcNow`（含注释说明同病） |

- 与既有约定一致（`GovernanceVersionService.cs:1391/1444/1494` 均 `DateTime.UtcNow`）；
- CreatedAt 落真实业务时间戳，不依赖仓储 ISNULL 兜底；
- **全在 3号位 自有服务层，2号位 零改动**（§三 方案 a 要求满足）。

## 三、同类残留核查（无）

- **更新路径**：`BuildUpdated`（`SetupRuleService.cs:382`）**显式保留 `CreatedAt = existing.CreatedAt`**（L395），不重建时间戳——无 500 风险；
- **全库构造点**：Grep `new SetupTransitionRule` 仅 3 处生产代码构造——`CreateExactAsync` / `CreateDefaultAsync`（已修）+ `BuildUpdated`（更新路径，保留原值）；其余为 `SetupTransitionRuleSnapshot`（1号位 求解快照，另一类型）与测试文件，均无关。

## 四、验证

- `dotnet build LPS.APS.sln` → **0 错误**（46 警告全为既有项：PeggingResultVoucher 过时等，非本次引入）。

## 五、请 4号位

1. 合入 dev 后（SVN 提交由用户侧执行），**当日复跑 `GROUP=setup` 12 断言** → 出数回执；
2. 出数即 **Setup 联调验收关闭**（含贵函 §五 挂起 2 项——P0 Code→Id 端点待排期、旧发布路径待 0号位 裁决，均不阻塞关闭）。

**发送人**：3号位 ｜ **日期**：2026-09-20
**待办**：① 4号位 复跑 12 断言出数 ② Setup 联调验收关闭 ③ P0 端点排期（2号位 源表确认 + 0号位 排期）
