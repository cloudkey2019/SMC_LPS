# APS V1 Setup 规则承载重构——2号位 D-1/D-2 执行回执

**文档性质**：跨号位执行回执（3号位 收）——D-1（DROP 物理表 + 旧仓储链清理）执行闭环 + D-2（容量回执）。
**发送方**：2号位（数据引擎/主执行窗口）｜ **日期**：2026-09-23
**依据**：3号位《APS_V1_Setup规则承载重构_2号位_D-1D-2执行确认件_3号位_v1.0_20260923.md》
**核实基线**：2026-09-23 svn pull 后的当前工作树（非旧快照，铁律3）

---

## 一、结论

**D-1（DROP `[dbo].[SetupTransitionRule]`）——已执行闭环。**
**D-2（`ContentSnapshotJson` 容量）——确认可行。**

执行前 2号位 逐条代码核实：物理表已无任何「已注册且被解析」的访问路径（S-4 装载改道 / S-5 读删 / S-3 DI 撤 / 两服务全迁 JSON），DROP 运行时安全（§二）。执行动作（§五）：开发库表已删、物理表旧仓储链 2 文件已删并 SVN 提交 r13444、`dotnet build` 0 错误。

---

## 二、D-1 运行时安全核实（执行前，file:line）

1. **S-4 第⑦块装载已改道**——`LPS.APS.Application/Services/FrozenStrategySnapshotProvider.cs:116-119`
   `SetupTransitionRules = DeserializeSetupTransitionRules(ruleSetVersion)` → `SetupTransitionRuleProjector.ProjectFromSnapshot(ContentSnapshotJson)`。✔
2. **S-5 PeggingOrchestrator 物理表读取已删**——`LPS.APS.Application/Services/PeggingOrchestrator.cs:98-99`
   注释「原物理表装载 LoadSetupTransitionRulesAsync 已移除」，无调用残留。✔
3. **S-3 DI 注册已撤销**——`LPS.APS.Engine/Extensions/GovernanceServiceExtensions.cs:37` 仅剩注释、无 `AddScoped<ISetupTransitionRuleRepository,…>`；`DatabaseServiceExtensions.cs:48-50` 的 Scrutor 扫描命名空间仅 `Engine.Repositories.Auth`+`Pegging`，不含 `Engine.Repositories.Governance`。✔
4. **两服务全迁 JSON、零物理表访问**——`SetupRuleService.cs:47-57`（S-1/S-6 真路径）与旧名 `SetupTransitionRuleService.cs:38-44` 均写 `ContentSnapshotJson`，不注入物理仓储。✔

---

## 三、残留清理（已执行 + 遗留）

| 对象 | 处置 |
|---|---|
| `dbo.SetupTransitionRule`（表） | ✅ 已 DROP（开发库 `APS_Production`，`sys.tables WHERE name='SetupTransitionRule'` 复检=0） |
| `LPS.APS.Engine/Repositories/Governance/SetupTransitionRuleRepository.cs` | ✅ 已 `svn delete`（含 5 条物理表 SQL，唯一真访问点） |
| `LPS.APS.Core/Interfaces/ISetupTransitionRuleRepository.cs` | ✅ 已 `svn delete` |
| `LPS.APS.Application/Services/SetupTransitionRuleService.cs` | ⏳ 遗留（已迁 JSON、仍被 Scrutor 注册；旧 API，退役待前端切通道） |
| `LPS.APS.Web/Controllers/SetupTransitionRuleController.cs` | ⏳ 遗留（旧路由 `api/SetupTransitionRule`，退役同上） |

**SVN 提交**：`SetupTransitionRuleRepository.cs` + `ISetupTransitionRuleRepository.cs` 删除提交 **r13444**。

**2.4 复核结果**：全仓 Grep `[dbo].[SetupTransitionRule]` / `SetupTransitionRuleRepository` / `ISetupTransitionRuleRepository` **仅剩测试文件中的注释**（「S-3：…已撤销」），物理表访问清零、索引/约束/FK 随表一并 DROP。**通过。**

**遗留说明**：旧 `SetupTransitionRuleService.cs`（Application）+ `SetupTransitionRuleController.cs`（Web）已不再触物理表（读写 `ContentSnapshotJson`），属「旧 API 退役」而非物理表残留；是否删除待 3号位/4号位 确认前端已切 `SetupRule`（`api/SetupRule`）通道后统一退役。

---

## 四、D-2：`ContentSnapshotJson` 容量确认

`RuleSetVersion.ContentSnapshotJson` 实测 `NVARCHAR(MAX)`（≤2GB），Setup 子块为 JSON 数组（每条 ~0.5~1KB），按百~千条规则占用 ≤ 数 MB，远低于上限。**确认可行。**

---

## 五、回执栏（2号位 填写）

| 项 | 确认 |
|---|---|
| D-1 已执行 DROP（表+索引+约束） | ☑ 已执行（开发库 `APS_Production`，`DROP TABLE IF EXISTS`，复检归零） |
| D-1 后复核项（2.4）通过 | ☑ 通过（全仓物理表 SQL 清零，仅剩注释；旧仓储/接口已删 r13444） |
| D-2 `ContentSnapshotJson` 容量可行 | ☑ 确认 |

---

## 六、后续

1. 旧 `SetupTransitionRuleService.cs` / `SetupTransitionRuleController.cs` 退役 → 待 3号位/4号位 确认前端切 `SetupRule` 通道后删（不阻塞本次 D-1，已记台账）。
2. 契约登记件状态：**转 ✅**（D-1 执行 + D-2 回执闭环）。

**发送方**：2号位 ｜ **日期**：2026-09-23