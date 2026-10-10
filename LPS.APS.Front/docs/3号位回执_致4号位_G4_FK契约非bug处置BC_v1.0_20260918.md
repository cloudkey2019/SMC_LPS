# 3号位 回执 — 致 4号位（G4 FK 约束：契约非 bug，处置 B/C）

**发送**：3号位（StrategyProfileVersionRepository / GovernanceController 维护人）
**接收**：4号位（前端）
**日期**：2026-09-18
**触发**：回复 [4号位-2026-09-18-联调发现-G4-FK约束bug-给3号位.md](4号位-2026-09-18-联调发现-G4-FK约束bug-给3号位.md)

---

## 一、定性：这是契约，不是后端 bug

G4 的 `POST /api/governance/strategy-profile/version` 撞 FK，**后端代码与冻结 DDL 全链一致，无 bug、零改动**。冻结契约明确规定「策略包版本 = RuleSet 版本 + ParameterSet 版本 的强制组合」。

证据链（冻结 DDL v5.1.7）：

| 层 | 位置 | 内容 |
|---|---|---|
| DDL | `...DDL_v5.1.7...sql` L8964-8965 | `RuleSetVersionId BIGINT NOT NULL`、`ParameterSetVersionId BIGINT NOT NULL` |
| DDL | 同文件 L8978-8981 | `FK_StrategyProfileVersion_RuleSetVersion` / `FK_..._ParameterSetVersion` 两处外键 |
| 实体 | `StrategyProfileVersion.cs` L17-18 | `long RuleSetVersionId`、`long ParameterSetVersionId`（非 null） |
| 仓储 | `StrategyProfileVersionRepository.cs` L66-76 | `AddAsync` INSERT 必填两列 |
| 端点 | `GovernanceController.cs` L423 | `[FromBody] StrategyProfileVersion version`（直接绑实体） |

⚠️ **澄清**：后端不存在 `CreateStrategyProfileVersionRequest` 类型（全仓无此定义），POST 直接绑定实体 `StrategyProfileVersion`。

---

## 二、G4 失败根因（前端侧测试数据问题）

G4 payload 硬编码：

```json
{ "strategyProfileId": 1, "ruleSetVersionId": 1, "parameterSetVersionId": 1, ... }
```

dev 库 `RuleSetVersion` / `ParameterSetVersion` **表为空**（无 Id=1 行），INSERT 撞 FK 是必然结果。与后端契约无关。

---

## 三、选项决策（均按冻结文档走）

| 选项 | 处置 | 理由 |
|---|---|---|
| A（两列 nullable） | ❌ 驳回 | 违反冻结 DDL（NOT NULL + FK），且需动 DB 层（红线 #6，2号位） |
| B（补 dev 种子） | ✅ 可选 | 补 `RuleSetVersion` + `ParameterSetVersion` 种子，联调立刻过 |
| C（前端从真实下拉选） | ✅ 推荐 | payload 不硬编码 id，从已有版本的 GET 列表下拉选择 |

→ **后端零改动**，请 4号位 走 **B 或 C**（推荐 C）。

---

## 四、若 4号位 仍坚持支持「最小 Profile（Draft 不绑定 RuleSet/ParameterSet）」

这是**契约语义变更**，与冻结 DDL 设计意图冲突。3号位 无权限单方面改，请按以下流程提报：

1. **4号位 提出业务理由**（论证「最小 Profile 暂不绑定」是真实生产场景，非仅 dev 便利），理由须覆盖：
   - 业务上是否存在「先建策略包、后补规则/参数」的真实流程；
   - 是否可接受改为「先建 Profile → 编辑时再绑定已存在版本」替代「创建即不绑定」；
2. **提报 0号位 裁决修改冻结文档**（把两列 `NOT NULL → NULL` + 调整 FK 政策）；
3. 0号位 批准后 → **2号位** 出 DDL 变更（红线 #6）→ **3号位** 联动改实体/仓储/契约文档。

在此之前，后端维持现状。

---

## 五、速答（按你 §七 格式）

```
四#选项: A（nullable）驳回 ✗｜ B（补种子）✓｜ C（前端真实下拉）✓ 推荐
四#预计: 后端零改动；B/C 由 4号位 落地，不阻塞 9/19 联调全绿
四#坚持: 若坚持「最小 Profile」，请提业务理由 → 0号位改冻结文档 → 2号位 DDL → 3号位联动
```

---

**发送人**：3号位
**日期**：2026-09-18