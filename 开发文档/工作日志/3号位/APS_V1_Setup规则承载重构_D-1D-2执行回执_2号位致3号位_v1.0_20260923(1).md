# APS V1 Setup 规则承载重构——2号位 D-1/D-2 执行回执

**文档性质**：跨号位执行回执（3号位 收）——D-1（DROP 物理表）运行时安全核实 + D-2（容量回执）。
**发送方**：2号位（数据引擎/主执行窗口）｜ **日期**：2026-09-23
**依据**：3号位《APS_V1_Setup规则承载重构_2号位_D-1D-2执行确认件_3号位_v1.0_20260923.md》
**核实基线**：2026-09-23 svn pull 后的当前工作树（非旧快照，铁律3）

---

## 一、结论

**D-1（DROP `[dbo].[SetupTransitionRule]`）运行时安全——已代码级核实通过，可执行。**
**D-2（`ContentSnapshotJson` 容量）——确认可行。**

但 3号位 确认件 §一「仓储/DI 已由 S-3 撤销（物理表访问清零）」的表述**字面不完全成立**：DI 注册确已撤销（✔），但**残留死文件未删**（`SetupTransitionRuleRepository.cs` 仍含 5 条物理表 SQL 等）。这不影响 DROP 安全性，但会使确认件 §2.4 复核项「全仓 Grep 仅剩文档/注释」FAIL（详见 §三）。

---

## 二、D-1 运行时安全核实（证据链，file:line）

逐条核对「物理表已无任何已注册且被解析的访问路径」：

1. **S-4 第⑦块装载已改道**——`LPS.APS.Application/Services/FrozenStrategySnapshotProvider.cs:116-119`
   `SetupTransitionRules = DeserializeSetupTransitionRules(ruleSetVersion)` → `SetupTransitionRuleProjector.ProjectFromSnapshot(ContentSnapshotJson)`。✔ 已落地。

2. **S-5 PeggingOrchestrator 物理表读取已删**——`LPS.APS.Application/Services/PeggingOrchestrator.cs:98-99`
   注释「原物理表装载 LoadSetupTransitionRulesAsync 已移除」，无任何调用残留。✔ 已落地。

3. **S-3 DI 注册已撤销**——`LPS.APS.Engine/Extensions/GovernanceServiceExtensions.cs:37`
   仅剩注释「Setup换型：…不再走独立物理表仓储——S-3 撤销」，**无** `AddScoped<ISetupTransitionRuleRepository, …>` 注册行；且 `LPS.APS.Engine/Extensions/DatabaseServiceExtensions.cs:48-50` 的 Scrutor 扫描命名空间仅为 `Engine.Repositories.Auth` + `Engine.Repositories.Pegging`，**不含** `Engine.Repositories.Governance`（该命名空间走 `GovernanceServiceExtensions` 显式注册）。故 `SetupTransitionRuleRepository` 无任何 DI 注册入口。✔ 已拔。

4. **两个 Setup 服务均已迁 JSON、零物理表访问**——
   - `LPS.APS.Application/Services/SetupRuleService.cs:47-57`（S-1/S-6 真写路径）注入 `IRuleSetVersionRepository` + `IMasterDataLookupRepository` + `IAuditLogRepository` + `IGovernanceVersionService`，读写 `SetupTransitionRuleProjector.ExtractRules/SetRules(ContentSnapshotJson)`，**不注入也不触碰物理仓储**。
   - `LPS.APS.Application/Services/SetupTransitionRuleService.cs:38-44`（旧名服务）注入 `IRuleSetVersionRepository` + `IAuditLogRepository`，同样读写 `ContentSnapshotJson`，**已迁走物理表**。
   - 二者均经 `ApplicationServiceExtensions.cs:19-28` Scrutor 自动注册（`LPS.APS.Application.Services` 命名空间 `.AsImplementedInterfaces()`）。✔

**结论**：`dbo.SetupTransitionRule` 现为全孤儿表，运行时不安全路径为零。DROP 不会导致任何启动 DI 失败或请求时 SQL「对象不存在」异常。

---

## 三、纠偏：S-3「物理表访问清零」不实——残留死文件清单

| 残留文件 | 层 | 状态 | 对 D-1 影响 |
|---|---|---|---|
| `LPS.APS.Engine/Repositories/Governance/SetupTransitionRuleRepository.cs` | Engine（3号位区） | **死文件**，含 5 条物理表 SQL（SELECT×2 `:30/:42`、INSERT `:51`、UPDATE `:72`、DELETE `:92`），编译通过但无注册、永不实例化 | 无（不影响 DROP 安全），但令 §2.4 复核 FAIL |
| `LPS.APS.Core/Interfaces/ISetupTransitionRuleRepository.cs` | Core（2号位区） | 孤儿接口 | 无 |
| `LPS.APS.Application/Services/SetupTransitionRuleService.cs` | Application（2号位区） | 旧名服务，已迁 JSON（无物理表访问），仍被 Scrutor 注册 | 无 |
| `LPS.APS.Web/Controllers/SetupTransitionRuleController.cs` | Web（3/4号位区） | 旧路由 `api/SetupTransitionRule`，经 `AddControllers` 自动发现，注入已注册的 `ISetupTransitionRuleService`，**可跑但走 JSON 新承载** | 无（不触物理表） |

**说明**：`SetupTransitionRuleRepository.cs` 是唯一的「仍含物理表 SQL」文件，但因命名空间 `Engine.Repositories.Governance` 不在任何 Scrutor 扫描清单、且 `GovernanceServiceExtensions` 已去掉显式注册，故它是**死文件（没删）而非活链路（还在跑）**——3号位 确认件把「撤销 DI」等价为「物理表访问清零」，是**语义滑坡**（DI 撤了 ≠ 文件删了）。

**建议**（不阻塞 DROP，属 S-3 收尾）：
- `SetupTransitionRuleRepository.cs`（Engine/3号位区）+ `ISetupTransitionRuleRepository.cs`（Core/2号位区）——建议 3号位 收尾删除（接口在 2号位 Core 区、实现与控制器在 3号位 区，跨区删除请 2/3 联合确认后各自删或由 3号位 统一删）。
- 旧 `SetupTransitionRuleService.cs` + `SetupTransitionRuleController.cs` 是否退役为 2号位 `SetupRuleService`/3号位 `SetupRuleController` 单通道，建议 3号位 定（不影响本次 D-1）。

---

## 四、D-2：`ContentSnapshotJson` 容量确认

`RuleSetVersion.ContentSnapshotJson` 实测为 `NVARCHAR(MAX)`（实体 `RuleSetVersion.cs:33` 声明 `string?`；库列 `CHARACTER_MAXIMUM_LENGTH = -1`，≤ 2GB）。Setup 子块为 JSON 数组，每条约 0.5~1KB（含审计字段），按典型百~千条规则占用 ≤ 数 MB，远低于上限。**容量可行，确认。**

---

## 五、回执栏（2号位 填写）

| 项 | 确认 |
|---|---|
| D-1 已执行 DROP（表+索引+约束，顺序脚本） | ☐ 运行时安全已核实（§二）＋ DROP 脚本已备 `03_SQL脚本/SetupTransitionRule_DROP_20260923.sql`；**实际连生产库执行待 2号位 统一收口** |
| D-1 后复核项（2.4）通过 | ☐ **部分**——`sys.tables` 计数归零可过；但「全仓 Grep 仅剩文档/注释」**不过**（`SetupTransitionRuleRepository.cs` 死文件 5 条 SQL 仍在，见 §三） |
| D-2 `ContentSnapshotJson` 容量可行 | ☑ 确认 |
| 备注 | DROP 安全前提=S 系列已就绪（已核实 S-4/S-5/S-3-DI 三落、两服务全迁 JSON）；仅剩死文件未删，可随后补删，不阻塞本次 DROP |

---

## 六、后续

1. 2号位 连生产库执行 `03_SQL脚本/SetupTransitionRule_DROP_20260923.sql`（待本窗口统一收口，实际执行后勾 ☑）。
2. 请 3号位 收尾 S-3：删除 `SetupTransitionRuleRepository.cs` + `ISetupTransitionRuleRepository.cs` 死文件（跨区，2/3 联合确认），或明确旧 `SetupTransitionRuleService/Controller` 退役口径。
3. 契约登记件状态：D-1 DROP 落库 + D-2 回执后转 ✅。

**发送方**：2号位 ｜ **日期**：2026-09-23