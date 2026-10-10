# 3号位致4号位：20261009 Batch Policy 收口后端落码完成——前端同步指引 v1.0

| 项 | 值 |
|---|---|
| 日期 | 2026-10-09 |
| 发件 | 3号位（规则参数治理 owner / 后端） |
| 收件 | 4号位（前端 owner） |
| 主题 | 20261009 冻结候选（Batch Policy 收口 ①②③④）后端已落码并提交 SVN；请前端同步「不展示 4 字段」+ 新增业务校验 |
| 关联 | [4号位-2026-10-09-TaskSplitRuleConfig列表分页改造前端落地回执-给3号位.md]（v1.6 候选「不展示 4 字段」不抢先改）· [3号位致4号位_TaskSplitRuleConfig分页dev旧形态R1回执_v1.0_20261009.md] · 冻结文档 §13 · 字段说明 v5.1.10 / 3号位实施包 v1.9（候选已生效） |

---

## 一、结论：候选已生效，3号位 后端已按 ①②③④ 落码并提交 SVN

- 20261009 冻结候选（Batch Policy 收口）经 0号位 索引 v1.12 声明 + 正文 PASS，已按 3号位 复核回执对齐项 ①②③④ 落码。
- **SVN 已提交**（3号位 侧用户，2026-10-09）→ dev 重建后可复测分页探针（见第四节）。

---

## 二、后端落码清单（治理 API 行为变更）

| # | 变更 | 治理 API 效果 |
|---|---|---|
| ① | ⑧ 快照停投 4 字段 | 快照侧 `MaxOptimizationSplitCount/MaxBatchCandidates/BottleneckSplitStrategy/NonBottleneckStrategy` 恒 null（Solver 技术预算归 1号位 版本化安全默认；策略列仅追溯），主链不消费 |
| ② | 治理 DTO 移除 4 字段 | `GET /api/governance/task-split-rule-configs` 列表/详情**不再返回** 4 字段；`POST`/`PUT` 请求体**不再接受** 4 字段（多余字段忽略） |
| ③ | 校验补强 | `ProductionDepartmentId` **必填**（不再支持「NULL 部门 = Material 级默认」）；`PreferredBatchQty` **必填且 >0**、`Min ≤ Preferred ≤ Max`，违反返回 400 |
| ④ | NULL 部门不生效 | 既有 NULL 部门历史规则**不**默认为所有部门生效（不投快照）；治理写路径拒绝新增/更新 NULL 部门规则 |

---

## 三、前端需同步项（4号位）

> 原 v1.6 候选「不展示 4 字段」现按生效候选落实；前端此前「不抢先改」约定解除。

1. **types 移除 4 字段**：`types/taskSplitRuleConfig.ts` 的 `TaskSplitRuleConfig` / `SaveTaskSplitRuleConfigRequest` 删除 `MaxOptimizationSplitCount / MaxBatchCandidates / BottleneckSplitStrategy / NonBottleneckStrategy`（后端不再返回/接收）。
2. **表单/列表不展示 4 字段**：TaskSplitRuleConfig 配置页表单与列表列移除 4 字段。
3. **新增表单校验**（与后端 400 兜底一致）：
   - `ProductionDepartmentId` 必选（部门下拉必选，去掉「不限/Material 级默认」空选项）；
   - `PreferredBatchQty` 必填且 `> 0`；
   - `Min ≤ Preferred ≤ Max`（硬 Min/Max 已配时）。
4. **提示文案**：NULL 部门历史记录不默认为所有部门生效（如列表存在旧 NULL 部门行，展示为「历史兼容、不生效」或不展示）。
5. **mock 同步**：`taskSplitRuleConfig.ts` mock 路径移除 4 字段构造，保证 mock 与后端形状一致。

---

## 四、分页 dev 复测解锁（TSRC14j / 14k）

SVN 已提交 → **dev 重新拉取 + 重起本地后端**（或等共享 dev 部署管线重 build）后，可复测：

| # | 断言 | 预期 |
|---|---|---|
| TSRC14j | `GET ?pageIndex=1&pageSize=2` | `items ≤ 2`、`total ≥ items`、`page/pageSize` 字段在 |
| TSRC14k | `GET ?pageSize=9999` | 截断 200（`pageSize=200`） |

---

## 五、不在本次范围

- `ResourceGroupId / MOQ / EOQ` 兼容字段剔除：10-07 已闭环，无变化。
- 物理 DDL：本轮无变更（v5.1.8.4 仅注释），4 字段物理列保留历史。
- 1号位 Solver 技术预算（`MaxOptimizationSplitCount/MaxBatchCandidates`）的版本化安全默认：归 1号位 定界，前端无需展示。

---

## 六、待 4号位 确认

- 前端 5 项同步完成回执（TSRC 静态断言可复用现 `GROUP=t-tsrc` 脚本，按需补「4 字段不展示」静态断言）。
- 复测 TSRC14j/14k 结果。

---

## 七、增补 v1.1（2026-10-09）：A1「唯一有效期」语义——前端去重文案同步

**后端行为变更（已落码，build 0 错、治理集成测试全绿）**：同一 `(MaterialId + ProductionDepartmentId)` 由「整行永久去重」改为「**同一时点至多一条有效（IsActive=1）规则**」：

- 新规则生效时间窗与既有**有效**规则窗口**重叠 → 拒绝（422）**；
- 顺序**非重叠**窗口 → **允许**（规则按生效窗生效；旧规则停用/过期后可重建）；
- NULL 时间边界视为无界（EffectiveFrom NULL=不限开始、EffectiveTo NULL=永久有效）；边界相切不视为重叠。

**前端需同步 3 项**：

1. **去重提示文案**：后端 422 错误消息由「已存在相同组合，不允许重复」改为「生效时间窗重叠，违反唯一有效期（同一时点至多一条有效规则）」——前端若对 422 消息做关键字匹配/自定义提示，请直接透传后端 `message`。
2. **表单校验引导**：配置有效期（EffectiveFrom/EffectiveTo）时提示「同一物料+部门同一时点至多一条有效规则」；**勿加「同键不可新增」硬拦截**——顺序非重叠窗口现为合法场景，重叠由后端 422 兜底。
3. **列表语义**：同键多条顺序生效记录合法，列表勿假设「一键一行」；如展示「唯一」概念，限定为「仅有效（IsActive=1）规则同一时点唯一」。

**TSRC 断言影响**：若现有断言「重复创建必须失败」——**仍成立**（无界窗口必重叠）；若含「停用后再次创建仍被拒」——**已不成立**（现允许重建，属预期变更）。
