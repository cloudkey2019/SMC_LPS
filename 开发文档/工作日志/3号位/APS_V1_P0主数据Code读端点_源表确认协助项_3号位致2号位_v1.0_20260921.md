# P0 主数据 Code 读端点 — 源表唯一性确认协助项（3号位 致 2号位）

> ⚠️ **【已撤销 2026-09-21】**——P0 方案 a 已按用户裁决交付：3号位 **直接读源表**（不经 2号位/0号位），`api/governance/lookups/departments|resources|materials` 三端点已落、build 0 错 0 警。**本协助项不再需要 2号位 回执**，请忽略。详见 Setup 记忆 + SetupRuleController 端点实现。

> **收件**：2号位（数据引擎 Owner）
> **抄送**：0号位（PM，排期）、4号位（前端，经 3号位 中转）
> **日期**：2026-09-21
> **依据**：《Setup联调验收回执处理》挂起项① P0 主数据缺口 = 方案 a（3号位 补 Code→Id 读端点）；《3号位致4号位_合并催办回执_v1.0_20260921.md》R2（09-25 后排期维持）
> **性质**：**协助项（数据契约确认）**——纯只读数据问询，无代码改动、无 DDL 变更（红线 #6 不受影响）
> **术语约定**：英文契约词一律「中文名称（英文）」；代码引 `文件:行`

---

## 〇、一句话

3号位 将补 **主数据 Code→Id 读端点**（Setup 三 Dialog 真实数据、撤 4号位 mock 硬编码）；开工前需 2号位 确认 3 张源表的 **Code 唯一性 + 状态过滤口径**——本函为唯一阻塞，2号位 回执后 3号位 即起草端点契约（红线 #5 契约先行）。

---

## 一、背景（为什么需要）

| 项 | 内容 |
|---|---|
| 缺口 | 现 `MasterDataLookupRepository` 仅 **Id→Info** 反向无 Code→Id（`LPS.APS.Engine/Repositories/Governance/MasterDataLookupRepository.cs`：`GetDepartmentCodeAsync`/`GetResourceCodeAsync`/`GetMaterialCodeAsync`/`GetResourceInfosAsync`/`GetDepartmentInfosAsync`） |
| 场景 | Setup 三 Dialog（部门/设备/物料下拉）真实数据 + #9 materialId 语义（= Id）——4号位 需按 **Code 选值 → 后端落 Id** |
| 方案 | P0 方案 a（0号位 已认可，新任务待排期）——3号位 补 Code→Id 读端点，走「2号位 数据经 3号位 查询端点供给」模式 |
| 排期 | R2 已定：09-25 合入节点后启动（本函为前置准备，不占 09-25 后工作量） |

## 二、请 2号位 确认（3 张源表数据契约）

现有 Id→Info 查询按**冻结 DDL v5.1.2** 对齐，列名：`ProductionDepartment.DeptCode` / `Resource.ResourceCode` / `Material.MaterialCode`。请逐项回执：

| # | 源表 / Code 列 | 请确认 ①：Code 是否唯一 | 请确认 ②：状态过滤列（如有） |
|---|---|---|---|
| 1 | `ProductionDepartment` / `DeptCode` | 是否唯一？（重码率 = 0？） | 是否有 `IsActive`/停用/删除标记需过滤？ |
| 2 | `Resource` / `ResourceCode` | 是否唯一？ | 同上 |
| 3 | `Material` / `MaterialCode` | **是否唯一？**（疑有重码：多工厂/多版本/历史软删——红线 #4「映射 API 返回列表而非单对象」据此立规） | 同上 |

## 三、返回口径建议（3号位 提议，2号位 认可或修订）

1. **部门 / 设备**：Code 唯一 → 读端点返回 **列表**，Code 匹配命中 1 条时前端可直接取 Id（后端仍返回列表，不为单值开例外）；
2. **物料**：按红线 #4，**始终返回列表**——Code 重码时由前端展示候选（版本/工厂后缀）或后端按优先级（生效中 > 历史）首条——优先级判定规则 2号位 若已有现成口径请直接引用，否则由 3号位 在契约中定义；
3. **状态过滤**：若 ② 有过滤列，读端点统一 `WHERE <状态列> = 1`（活动）返回；无过滤列则全量返回（前端自行区分）。

## 四、请 2号位 回执

- 格式：任一书面形式，逐表回答 §二 ①② 即可（可选附 §三 认可/修订意见）；
- 3号位 收讫后：起草 `api/governance/lookups/...` 端点契约（URL / 查询参数含模糊搜索与分页 / 响应 DTO 字段 code+id+显示名）→ 发 0号位 排期 + 4号位 接 Setup 三 Dialog；
- 本函不产生代码改动请求，纯数据契约确认，无需 2号位 动码。

---

**发送人**：3号位 ｜ **日期**：2026-09-21
**待办**：① 2号位 回执 §二①② ② 收讫后 3号位 起草 lookups 端点契约（红线 #5）③ 0号位 排期 + 4号位 接
