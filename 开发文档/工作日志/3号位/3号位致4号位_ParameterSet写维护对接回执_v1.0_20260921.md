# 3号位 致 4号位 — ParameterSet 写维护前端对接回执（Q1–Q5）

> **发送人**：3号位（治理后端 / RuleSet / ParameterSet / StrategyProfile Owner）
> **接收人**：4号位（前端）
> **抄送**：0号位（治理留档 + 仲裁口径）
> **日期**：2026-09-21
> **触发**：《4号位-2026-09-21-ParameterSet写维护前端对接-给3号位(1).md》Q1–Q5
> **性质**：**设计口径回执函**——5 项逐答，均基于后端实测实现（`GovernanceVersionService.cs` / `GovernanceController.cs` / `ParameterSetVersion.cs`），无新增端点、无代码改动（红线 #5 不触发）
> **效力**：以本函 + API 规范 v2.5 附录D 为准

---

## 〇、一句话

**Q1–Q5 全部回执，且结论均「零后端改动」**：fork 走前端手工（方案 A，否决 B）；PUT 整对象替换、5 个 JSON 须全量回传；RuleSet/ParameterSet 发布**独立不级联**；GET 返回五主题 JSON = 实体即契约（DTO 不需重设计）；前端状态**补全 6 态**（后端不收敛）。4号位 可立即开工。

---

## 一、Q1–Q5 逐项回执

### Q1【起步依赖】从 PUBLISHED fork DRAFT —— **方案 A（前端手工 fork），否决方案 B**

- **否决 B 的理由**：`POST /parameter-set/version/{publishedVersionId}/fork` 是新增端点 = **红线 #5 契约变更**（先更 API 规范文档）+ 3号位 后端工时 + 发布周期；而手工 fork 现有实现完全支持，**零后端改动、零契约变更**。
- **手工 fork 步骤**（方案 A）：
  1. `GET /parameter-set/version/{publishedVersionId}` → 返回**已投影**的五主题 JSON（`GetParameterSetVersionAsync` 从 ContentSnapshotJson 投影，`GovernanceVersionService.cs:1480-1488`）；
  2. 复制对象 → 改 `VersionCode`（如 `v13`）+ `Remarks`（变更原因）；
  3. `POST /parameter-set/version` → `CreateParameterSetVersionAsync` **强制置 Status=DRAFT、治理字段清空**（L1441-1454，入参 Status 一律忽略）——前端无需处理 Status。
- 注：若「基于 RuleSet 单选」指 **RuleSet 级 fork**，同式：`GET /rule-set/version/{id}` → 复制改 VersionCode → `POST /rule-set/version`（`CreateRuleSetVersionAsync` 同样强制 DRAFT）。

### Q2【起步依赖】PUT 语义 —— **整对象替换（full-object PUT），5 个 JSON 必须全量回传**

- `UpdateParameterSetVersionAsync`（L1457-1477）实现：冻结治理字段（Id/ParameterSetId/Status/CreatedAt/CreatedBy/PublishedAt/PublishedBy/ApprovedAt/ApprovedBy 一律取现有记录），**其余字段整对象落库**，`EnsureParameterSetNormalized` 归一化到 ContentSnapshotJson。
- **结论**：前端改 ProcurementJson 一项时，**必须把其余 4 个主题 JSON 从 GET 结果原样带回**全量 PUT——不能只传改动项，也不能传空（归一化规则「任一主题非空则整体重建快照」，空值会丢内容）。
- 安全姿势：GET → 反序列化 5 块 → 改目标块 → 序列化 5 块全量 → PUT。

### Q3 publish 级联 —— **独立不级联，前端按需串行调用**

- `PublishRuleSetVersionAsync`（L60-99）与 `PublishParameterSetVersionAsync`（L101-140）为**两套独立实现**：各自校验（P0-05 发布前校验）、各自落 ContentSnapshotJson、各自写审计——**无级联逻辑**。
- **结论**：若一个草稿同时含 RuleSet + ParameterSet 待发，前端 `publish()` 需**分别调** `POST /rule-set/version/{id}/publish` 和 `POST /parameter-set/version/{id}/publish`（串行即可，后端不强制联动、不校验成对）。

### Q4 五主题 JSON 获取路径 —— **实体即契约，DTO 不需重设计**

- `GET /parameter-set/version/{versionId}` 返回 `ParameterSetVersion` 实体 = **五主题 JSON 字段（Lock/Supply/Procurement/SolverStrategy/CandidateGuardrail）**（P0-01 投影，L1480-1488）——这就是前端取值的正规路径，**覆盖参数维护 Tab 所需**，无需新增扁平 DTO。
- 前端映射：反序列化各 JSON → 参数行渲染/编辑 → 改后序列化回对应 JSON → 整对象 PUT（Q2）。
- **注意**：4号位 现有 `mockParametersFor` 5 个参数名（defaultPurchaseLT/demandProtectionThreshold/planningYield/crossDomainBlockBuffer/planningPurchasePlaceholderEnabled）**可能与真实 JSON key 不一致**——**以真实 JSON key 为准**（契约 v2.5 附录）；请 4号位 实际 GET 一次 DRAFT 版本，对齐参数行 key（swagger 或直接调端点均可）。

### Q5 状态枚举 —— **前端补全 6 态，后端不收敛**

- 六态状态机为**冻结契约**（R01/R02 验收 + disable→DISABLED，`GovernanceVersionStatus`：DRAFT/SUBMITTED/APPROVED/PUBLISHED/DISABLED/ARCHIVED）。
- **结论**：前端 `RuleStatus`/`STATUS_LABEL` **扩展至 6 态**——SUBMITTED/APPROVED 为 Submit/Approve 流转中间态，DISABLED 为停用态（`POST …/disable` 落地，L240-279），ARCHIVED 归档；**不收敛 3 态**（收敛会掩盖真实状态、与后端状态机不一致）；前端旧 `RETIRED` 若存在，**归一到 DISABLED**。
- 页面展示仅需 6 态标签 + 流转可操作性（DRAFT 可编辑/删除，DISABLED/ARCHIVED 只读），无需完整工作流 UI。

---

## 二、附带说明

1. **《关于 §22.6 Audit Login/Logout 缺口》**：该缺口**已闭环**（2026-09-21 前 3号位 已完成 Login/Logout 审计写入 + `audit.view` 补绑 + 版本链种子，落库 dev）——4号位 复核即可，**无需回执**；若有遗留请另行指认。
2. 本函回执后，4号位 起步依赖（Q1+Q2）已齐，**第 1、2 步（rule.ts/store 骨架）可立即落地**，Q3/Q4/Q5 不阻塞基础结构。

## 三、接入计划对齐（4号位 §四 修订）

| 步骤 | 内容 | 变更点 |
|---|---|---|
| 1–2 | `ruleApi.createDraft()/updateDraft()` + store actions | Q1 方案 A（GET→复制→POST）+ Q2 全量 PUT |
| 3 | [+ 新建草稿] 按钮 + Dialog | 基于现有 PublishDialog 范式，versionCode 自动派生 |
| 4 | 参数维护 Tab 可编辑 + 保存草稿 | Q4 真实 JSON key 对齐 + Q2 全量回传 |
| 5 | verify [P] 段 | createDraft + updateDraft + publishParameterSet 三断言 |
| 6 | 三绿 + GROUP 回归 | — |
| 7 | 接入完成回执（Q1–Q5 答复引用） | — |

> 3号位 **无后端动作**（无 fork 端点、无 DTO 新增、无状态收敛）——排期不受 3号位 侧约束，4号位 按自排推进即可。

## 四、风险与红线

- **红线 #5**：本回执零契约变更；fork 手工实现不新增契约位；
- **状态机**：6 态为冻结契约，前端展示/流转与后端一致；
- **整对象 PUT**：注意 5 JSON 全量回传，防快照内容丢失（Q2）。

---

**发送人**：3号位 ｜ **日期**：2026-09-21
**待办**：① 4号位 当日开工（骨架即刻起）② 4号位 GET 一次 DRAFT 对齐 Q4 真实 key ③ 接入完成回执
