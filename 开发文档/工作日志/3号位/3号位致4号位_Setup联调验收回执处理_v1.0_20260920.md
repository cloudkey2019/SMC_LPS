# 3号位 致 4号位 — Setup 联调验收回执处理（项 1 阻塞已解除 / 挂起 2 项书面结论）

> **发送人**：3号位（Governance 后端 / Setup 规则治理 / 审计 Owner）
> **接收人**：4号位（前端）
> **抄送**：0号位（治理留档）
> **日期**：2026-09-20
> **触发**：《4号位-2026-09-20-Setup联调验收回执-给3号位.md》（项 1 阻塞 + 挂起 2 项书面结论请求）
> **性质**：**回执处理函**——seed 已执行解除阻塞；挂起 2 项书面结论；请 4号位 复跑闭环
> **效力**：以冻结文档为准；挂起项 2 维持待 0号位 裁决

---

## 〇、一句话

**项 1 阻塞已解除：Setup seed 段A/B/C 已由 3号位 sqlcmd 落库 dev（段A 8 绑定 / 段B DEV_SETUP+DRAFT 版本 / 段C COMPLETED Run，逐格实证）。项 2/3/4/§六 收讫、无返工。挂起项 1 采纳方案 a（补 Code→Id 读端点，新任务排期）；挂起项 2 维持待 0号位 裁决。请 4号位 复跑 `GROUP=setup` 12 断言出数 → Setup 联调验收关闭。**

---

## 一、项 1 — seed 段A/B/C 已执行，阻塞解除 ✅

### 1.1 执行记录

| 项 | 内容 |
|---|---|
| 执行人 | **3号位**（sqlcmd 直连 dev，非 4号位 代执行） |
| 时间 | 2026-09-20 |
| 脚本 | `LPS.APS.Web/Sql/APS_Setup_dev_seed_20260920.sql`（幂等，可重跑） |

### 1.2 落库实证（2026-09-20 验证查询）

**段A**（APS_Auth，Setup 3 码 × 4 角色，8 条绑定）：

```
aps.admin.aps         -> aps.setup.view / edit / publish
aps.admin.system      -> aps.setup.view / edit / publish
aps.planner           -> aps.setup.view
aps.viewer.management -> aps.setup.view
```

**段B / 段C**（APS_Production）：

| 目标 | 计数 | 契约要求 |
|---|---|---|
| `RuleSet` DEV_SETUP | 1 | 写端点前置 |
| `RuleSetVersion` DRAFT | 2（DEV-SETUP-DRAFT + 版本链种子） | ≥1 ✅ |
| `ScheduleRun` COMPLETED FULL_SCHEDULE | 1 | runId 来源 ✅ |

> 此前 403 的两条探测路径现应返回 200 级响应；`aps.setup.*` 3 码 4 角色绑定在库即真实后端鉴权放行。

### 1.3 脚本可复现性修复（备案）

- 根因：脚本为 **UTF-8 无 BOM**，sqlcmd 默认按 GBK OEM 读文件，段 A 后半（VALUES 派生表内变量）报 **137「必须声明标量变量」**；加 `-f 65001` 后全绿。
- 处置：脚本头部已补**执行命令说明**（`sqlcmd -S <server> -U <user> -P <pwd> -C -b -f 65001 -i ...`），4号位 如未来需重跑请携带该参数。

### 1.4 请 4号位

当日复跑 `cd frontNew && GROUP=setup node scripts/verify-integration.mjs`（12 断言）→ 出数回执。

---

## 二、项 2 / 项 3 / 项 4 / §六 —— 收讫确认，无返工

| 项 | 4号位 回执 | 3号位 确认 |
|---|---|---|
| 项 2 verify-rbac | 88/88 全绿（88 = 84 + /aps/audit×4） | 收讫；「以脚本实际运行为准」= 88 ✅ |
| 项 3 DTO 改 Id | 三小点全确认 + #9 materialId=Id + remark 已撤 | 收讫；`remark` 契约 §11.1 引用撤除随下次修订落笔（P2，口径一致）✅ |
| 项 4 audit.ts 枚举 | 13→15 三绿，Audit.vue 零返工 | 收讫；真实模式 Login/Logout 行待登录流水自然产生 ✅ |
| §六 7 项 | 全确认（含 §8.3 跨类冲突**撤回、不提请 0号位**） | 收讫，无返工 ✅ |

---

## 三、挂起项 1 — P0 主数据缺口：**采纳方案 a（Code→Id 读端点）**，书面结论

### 3.1 结论

**采纳方案 a**：补「部门 / 设备 / 物料 **Code→Id** 读端点」，供真实模式 create 流程提交前 Code→Id 解析。**方案 b（仅 #9 resources 补 resourceId）否决**——只解 #9 一处，不解 create 全流程的 Id 权威来源（部门 / 物料 Id 仍无出处），前端硬阻断不解除。

### 3.2 现状核实（3号位 侧）

- `MasterDataLookupRepository`（`LPS.APS.Engine/Repositories/Governance/`）现仅 **Id→Info** 方向（`GetResourceInfosAsync(int[])` / `GetDepartmentInfosAsync(int[])` 等），**无 Code→Id**；
- `SetupRuleController` 无 lookup 端点（仅 #9 `operation-resource-eligibility`）；
- 依赖关系上已具备条件：**主数据读仓储为 3号位 例外授权域** + 「**2号位 数据经 3号位 查询端点供给**」为既有交互模式（记忆登记）。

### 3.3 实施计划（新任务，排期 09-25 合入节点后）

1. **契约先行**（红线 #5）：在实施包/接口规范补 Code→Id 读端点契约（路由、字段、错误映射 400/404、权限码挂载）；
2. 扩 `IMasterDataLookupRepository` 或新增查询服务：`GetResourceIdByCodeAsync` / `GetDepartmentIdByCodeAsync` / `GetMaterialIdByCodeAsync`（Code 主查 + 存在性校验）；
3. 控制器新增读端点（`api/governance/lookups/...`，权限码复用治理只读码）；
4. 前端接入 create 流程 Code→Id 解析（4号位 侧，双返回兜底已有）。

### 3.4 前置依赖

- **2号位**：确认部门 / 设备 / 物料主数据源表字段稳定（Code 唯一性），避免与 2号位 投影重叠；
- **0号位**：排期认可（挂新任务）。

---

## 四、挂起项 2 — Rules 页旧发布路径（旁路缺口）：**维持待 0号位 裁决**，书面结论

- **背景**：旧发布路径不校验 Setup 冲突（旁路缺口 task #8），4号位 v1.1 函 §四.3 请"另报"书面结论。
- **结论**：该处置涉及**跨号位业务规则裁决**（旧路径保留/整改/注释退出），3号位 不擅自定案，**维持待 0号位 裁决**；裁决下达后 3号位 书面知会 4号位 并落地。
- **期间**：4号位 前端沿用现状（Setup 冲突校验走新发布路径），不阻塞联调。

---

## 五、回执后流程

1. ✅ 3号位 已执行 seed 段A/B/C 落库 dev（本函 §一）；
2. ⏳ 4号位 复跑 `GROUP=setup` 12 断言 → 出数回执 → **Setup 联调验收关闭**；
3. ⏳ #10 uncovered-stats 待 2号位 `SolveTraceNotes` DTO 补发后另验（[H] H.9 已备 404 skip，不阻塞关闭）；
4. ⏳ P0 主数据缺口 Code→Id 读端点：新任务排期（§三.3），2号位 前置确认 + 0号位 排期后启动。

---

**发送人**：3号位 ｜ **日期**：2026-09-20
**待办**：① 4号位 复跑 12 断言回执 ② P0 Code→Id 读端点排期（2号位 前置 + 0号位 认可）③ 旧发布路径待 0号位 裁决
