# APS V1 RBAC 实施与验证手册（v1.2 §23.1 + DDL 角色码）

**生成日期**：2026-09-11
**依据**：审核报告 v1.2 §二十三.1（路由级 RBAC 重新启用）+ DDL v1.3 系统角色码落地（[3号位契约-种子数据补齐](3号位契约-种子数据补齐.md)）
**范围**：4号位前端 22 路由 × 4 DDL 角色 + 三道防御层（路由级 / 按钮级 / store scope 断言）
**状态**：✅ 闭环（ts:check / lint / build:pro / verify:rbac 四项全绿，88/88 一致；2026-09-20 同步：v1.4 +strategy-profile、v1.5 +setup 4 路由、09-20 +audit 审计日志页）

---

## 一、角色与权限码（v1.2 §23.1 + DDL v1.3）

### 1.1 系统角色（DDL v1.3 — 7 码，前端 mock 模式 4 码常用）

| DDL 角色码 | 中文标签 | 用途 | mock 演示 |
|---|---|---|---|
| `aps.admin.system` | 系统管理员 | 持全部权限码（后端 34；mock 37 含 strategy/setup 六码中后端未落部分）+ Global scope | ✅ |
| `aps.admin.aps` | 规则/参数管理员 | 规则/参数/策略/Setup edit + publish（v1.3 合并 RULE_ADMIN + RULE_PUBLISHER）| ✅ |
| `aps.planner` | PMC 计划员 | 19 码：plan/ctp/candidate/manual_eta/demand_protection.view + aps.setup.view + aps.audit.view | ✅ |
| `aps.viewer.management` | 查看员 | 11 码：仅 `*.view`（含 aps.setup.view） | ✅ |
| `aps.supervisor.workshop` | 车间主管 | dev 未 seed（mock 按 §二十五 场景分配 viewer 级）| viewer 级 mock |
| `aps.coordinator.material` | 物料协调 | dev 未 seed | viewer 级 mock |
| `aps.service.api` | API 服务账号 | dev 未 seed | viewer 级 mock |

**与 v1.1 别名差异**：v1.1 用 `VIEWER / PMC / RULE_ADMIN / RULE_PUBLISHER / SYSTEM_ADMIN` 5 个 mock 别名；v1.2 后端落地 7 个 DDL 系统角色码（aps.* 前缀），前端**对齐切换**。TypeScript 类型：[src/api/aps-v1/types/common.ts:179-225](../src/api/aps-v1/types/common.ts#L179-L225) `ROLE_KEYS` + `ROLE_LABELS`。

### 1.2 权限码（v1.2 §23.1 三段式 `aps.<module>.<action>`）

后端 PermissionCodes.cs 定义 34 码，前端按需使用，不强制枚举（编译期只检查路由 meta 中的字符串）。

| 模块 | 权限码 | 持有角色（dev seed）| 用途 |
|---|---|---|---|
| plan | `aps.plan.view` / `aps.plan.run` / `aps.plan.compare` / `aps.plan.export` | view=4 角色均持；run/compare/export=planner + admin.system | 只读页全员放行；运行排程=PMC/系统管理员 |
| ctp | `aps.ctp.view` / `aps.ctp.evaluate` | view=4 角色；evaluate=planner + admin.system | CTP 路由门控用 `evaluate`（写操作）|
| candidate | `aps.candidate.view` / `aps.candidate.confirm` / `aps.candidate.activate` | view=4 角色；confirm/activate=planner + admin.system | 路由门控用 `confirm` |
| reschedule | `aps.reschedule.local` / `aps.reschedule.manual` | planner + admin.system | Gantt 重排按钮用 `manual` |
| insert | `aps.insert.impact.evaluate` | planner + admin.system | — |
| rule | `aps.rule.view` / `aps.rule.edit` / `aps.rule.publish` | view=4 角色；edit/publish=admin.aps + admin.system | 路由 OR 门控 edit\|publish；publish 按钮用 `publish` |
| parameter | `aps.parameter.view` / `aps.parameter.edit` | view=4 角色；edit=admin.aps + admin.system | — |
| strategy | `aps.strategy.view` / `aps.strategy.edit` / `aps.strategy.publish` | view=4 角色；edit=admin.aps + admin.system | publish 码 admin.aps 实际未分配（seed 只给 admin.system），发布策略按钮仅系统管理员可见 |
| setup | `aps.setup.view` / `aps.setup.edit` / `aps.setup.publish` | view=4 角色；edit/publish=admin.aps + admin.system（**mock 预设先行**） | v1.5 Setup 专项；**后端三码未落地**（催办单 §十 修改点，3号位 排期 09-25），真实模式 403 期间 dev 演示走 mock |
| manual_eta | `aps.manual_eta.view` / `aps.manual_eta.edit` / `aps.manual_eta.cancel` | view=4 角色；edit/cancel=planner + admin.system | ManualEta 写按钮用 `edit` |
| demand_protection | `aps.demand_protection.view` / `aps.demand_protection.release` | view=4 角色；release=admin.system **仅** | DemandProtection 释放按钮用 `release` |
| mes | `aps.mes.view` / `aps.mes.dispatch` | view=4 角色；dispatch=admin.system **仅** | Run.vue 下发按钮用 `dispatch` |
| auth | `aps.auth.user.view/edit` / `aps.auth.role.view/edit` / `aps.auth.permission.assign` / `aps.auth.scope.assign` | admin.system **仅** | RBAC 管理 UI 路由门控用 `user.edit` |
| audit | `aps.audit.view` | 4 角色均持 | 审计日志页 `/aps/audit` 路由门控（09-20 页面已落地；与 rbac-* 四页仅 admin.system 不同——审计是监督能力，管理层 viewer 也可查看）|

> **设计要点（v1.2 §23.1 混合策略）**：
> - **路由级 + Domain 页**：用 DDL 角色码（与后端 `[Authorize(Roles=...)]` 一致）
> - **页面内 7 处写门控**：用权限码（与后端二次校验同码；不依赖角色，能表达 §二十五.3 「RULE_ADMIN 不能自动拥有发布权」）

### 1.3 4 角色 mock 预设

| DDL 角色码 | permissions 数量 | 业务范围 factoryCodes / productFamilyCodes / departmentCodes / domainKeys |
|---|---|---|
| `aps.viewer.management` | 11（*.view，含 aps.audit.view）| 全部空 = 未限制 |
| `aps.planner` | 19（09-20 +aps.audit.view）| F-SUZ-01 / INJECTION / INJECTION_DEPT / FAMILY_INJECTION |
| `aps.admin.aps` | 13（rule/parameter/strategy/setup edit + publish + audit.view；含 strategy.publish — verify 脚本与 auth.ts mock 预设的既有漂移，脚本注释已标记）| F-SUZ-01 / F-BJ-01 / MACHINING_DEPT / FAMILY_INJECTION+FAMILY_ASSEMBLY+BJ_FAMILY_INJECTION |
| `aps.admin.system` | 37（全部）| 全部空 = 未限制 |

详见 [src/store/modules/aps/auth.ts](../src/store/modules/aps/auth.ts) `MOCK_ROLE_PRESETS` + [src/store/modules/aps/scope.ts](../src/store/modules/aps/scope.ts) `MOCK_ROLE_SCOPE_PRESETS`。

---

## 二、22 路由 × 4 DDL 角色 完整矩阵

### 2.1 完整期望表（设计值）

| 路由 | 期望配置 | viewer | planner | admin.aps | admin.system |
|---|---|:-:|:-:|:-:|:-:|
| `/aps/rules` | 权限码 `aps.rule.edit\|aps.rule.publish` | ❌ 403 | ❌ 403 | ✅ 放行 | ✅ 放行 |
| `/aps/domain` | 角色 `aps.admin.system` | ❌ 403 | ❌ 403 | ❌ 403 | ✅ 放行 |
| `/aps/ctp` | 权限码 `aps.ctp.evaluate` | ❌ 403 | ✅ 放行 | ❌ 403 | ✅ 放行 |
| `/aps/candidate` | 权限码 `aps.candidate.confirm` | ❌ 403 | ✅ 放行 | ❌ 403 | ✅ 放行 |
| `/aps/manual-eta` | 角色 `aps.planner\|aps.admin.system` | ❌ 403 | ✅ 放行 | ❌ 403 | ✅ 放行 |
| `/aps/demand-protection` | 角色 `aps.planner\|aps.admin.system` | ❌ 403 | ✅ 放行 | ❌ 403 | ✅ 放行 |
| `/aps/overview` | 权限码 `aps.plan.view` | ✅ 放行 | ✅ 放行 | ✅ 放行 | ✅ 放行 |
| `/aps/order` | 权限码 `aps.plan.view` | ✅ 放行 | ✅ 放行 | ✅ 放行 | ✅ 放行 |
| `/aps/gantt` | 权限码 `aps.plan.view` | ✅ 放行 | ✅ 放行 | ✅ 放行 | ✅ 放行 |
| `/aps/explanation` | 权限码 `aps.plan.view` | ✅ 放行 | ✅ 放行 | ✅ 放行 | ✅ 放行 |
| `/aps/run` | 权限码 `aps.plan.view` | ✅ 放行 | ✅ 放行 | ✅ 放行 | ✅ 放行 |
| `/aps/pi` | 权限码 `aps.plan.view` | ✅ 放行 | ✅ 放行 | ✅ 放行 | ✅ 放行 |
| `/aps/strategy-profile` | 权限码 `aps.strategy.view\|edit\|publish`（OR） | ✅ 放行 | ❌ 403 | ✅ 放行 | ✅ 放行 |
| `/aps/setup/exact` | 权限码 `aps.setup.view\|edit\|publish`（OR） | ✅ 放行 | ✅ 放行 | ✅ 放行 | ✅ 放行 |
| `/aps/setup/default` | 权限码 `aps.setup.view\|edit\|publish`（OR） | ✅ 放行 | ✅ 放行 | ✅ 放行 | ✅ 放行 |
| `/aps/setup/uncovered` | 权限码 `aps.setup.view` | ✅ 放行 | ✅ 放行 | ✅ 放行 | ✅ 放行 |
| `/aps/setup/diff` | 权限码 `aps.setup.view\|publish`（OR） | ✅ 放行 | ✅ 放行 | ✅ 放行 | ✅ 放行 |
| `/aps/rbac-users` | 权限码 `aps.auth.user.edit` | ❌ 403 | ❌ 403 | ❌ 403 | ✅ 放行 |
| `/aps/rbac-roles` | 权限码 `aps.auth.user.edit` | ❌ 403 | ❌ 403 | ❌ 403 | ✅ 放行 |
| `/aps/rbac-permissions` | 权限码 `aps.auth.user.edit` | ❌ 403 | ❌ 403 | ❌ 403 | ✅ 放行 |
| `/aps/rbac-scopes` | 权限码 `aps.auth.user.edit` | ❌ 403 | ❌ 403 | ❌ 403 | ✅ 放行 |
| `/aps/audit` | 权限码 `aps.audit.view` | ✅ 放行 | ✅ 放行 | ✅ 放行 | ✅ 放行 |

> 总组合 **88**（22 路由 × 4 角色）。✅ 期望一致；❌ 期望被 `/403` 拦截。
> `/aps/audit` 与 rbac-* 四页（仅 admin.system）不同：审计是监督能力，4 角色均持 `aps.audit.view`（§1.2 audit 行）。

### 2.2 路由 meta 实现位置

[src/router/modules/aps.ts](../src/router/modules/aps.ts) — 22 路由全部已挂 `apsRequiredRoles` / `apsRequiredPermissions`（元数据定义）。

---

## 三、三道防御层

### 3.1 第 1 道 — 路由级守卫（粗粒度门控）

- **文件**：[src/router/guard.ts:65-87](../src/router/guard.ts#L65-L87) `setupApsRouteGuard`
- **触发**：路由 `name` 以 `Aps` 开头 + meta 含 `apsRequiredRoles` / `apsRequiredPermissions`
- **行为**：未通过 → 跳 `/403?from=<原路径>`
- **设计**：
  - OR 语义（任一命中即通过）
  - 角色字段与权限码字段**同时存在时为 AND 语义**（当前实现无此组合）

### 3.2 第 2 道 — 按钮级门控（细粒度 — 7 处全用权限码）

| 页面 | 判定（v1.2 权限码版）| 文件 |
|---|---|---|
| ManualEta | `canWrite = has('aps.manual_eta.edit')` | [src/views/Aps/ManualEta.vue:66-71](../src/views/Aps/ManualEta.vue#L66-L71) |
| DemandProtection | `canWrite = has('aps.demand_protection.release')`（仅 admin.system）| [src/views/Aps/DemandProtection.vue:90-97](../src/views/Aps/DemandProtection.vue#L90-L97) |
| Candidate（store）| `roleAllowedToConfirm = has('aps.candidate.confirm')` | [src/store/modules/aps/candidate.ts:62-70](../src/store/modules/aps/candidate.ts#L62-L70) |
| Rules（store）| `canWrite = has('aps.rule.edit')` / `canPublishAction = has('aps.rule.publish')` | [src/store/modules/aps/rules.ts:97-109](../src/store/modules/aps/rules.ts#L97-L109) |
| Run（MES 下发）| `canRecoverRun = has('aps.plan.run')` / `canDispatchMes = has('aps.mes.dispatch')`（仅 admin.system）| [src/views/Aps/Run.vue:205-226](../src/views/Aps/Run.vue#L205-L226) |
| Gantt（手动重排）| `canManualReschedule = has('aps.reschedule.manual')` | [src/views/Aps/Gantt.vue:765-768](../src/views/Aps/Gantt.vue#L765-L768) |
| Domain（特例 — 角色门控）| `canWrite = roles.includes('aps.admin.system')`（Domain 治理类，无特定权限码）| [src/views/Aps/Domain.vue:54-61](../src/views/Aps/Domain.vue#L54-L61) |

### 3.3 第 3 道 — Store scope 断言（写操作阻断）

详见 [[P1-14-15-权限场景测试矩阵] §三.3.2](P1-14-15-权限场景测试矩阵.md)。

---

## 四、验证步骤（按用户视角，分两个工具）

工具 1 是「离线静态验证」（我跑过，全绿）；工具 2 是「dev 端到端验证」（你需要跑）。

### 4.1 工具 1 — 离线静态矩阵（pnpm verify:rbac）✅ 已通过

**原理**：复刻 [src/router/guard.ts:41-64](../src/router/guard.ts#L41-L64) `isRouteAllowed` 逻辑 + 复刻 [src/store/modules/aps/auth.ts](../src/store/modules/aps/auth.ts) `MOCK_ROLE_PRESETS` + 复刻 [src/router/modules/aps.ts](../src/router/modules/aps.ts) 22 路由 meta，跑 4 角色 × 22 路由 = 88 组合。

**操作**：

```bash
cd frontNew
pnpm verify:rbac
```

**期望输出**（最后一行）：
```
总组合数：88    ✅ 一致：88    ❌ 不一致：0
```

进程退出码 = 0。任一组合不一致会立即红屏 + exit 1。

**何时跑**：
- 修改 [src/router/modules/aps.ts](../src/router/modules/aps.ts) 路由 meta 后
- 修改 [src/store/modules/aps/auth.ts](../src/store/modules/aps/auth.ts) `MOCK_ROLE_PRESETS` 后
- 修改 [scripts/verify-rbac.mjs](../scripts/verify-rbac.mjs) `EXPECTED` 表后
- 简单 PR 前一键 sanity check

---

### 4.2 工具 2 — dev 端到端验证（你需要跑）

**目标**：让浏览器**实际拦截**一次 403 跳转，确认守卫函数真的接住（不光是离线静态一致）。

#### 4.2.1 启动 dev server

```bash
cd frontNew
pnpm dev
```

终端看到：
```
  VITE v6.0.7  ready in xxx ms
  ➜  Local:   http://localhost:xxxx/
```

打开浏览器访问该地址（注意端口）。

#### 4.2.2 找到 mock 角色切换器

**准确位置** — 仅在 Gantt 页面顶部工具栏右侧：

1. 左侧菜单点 **「APS 排产」** → 展开
2. 点 **「甘特图/资源计划」**（图标是时间轴）
3. 进入 Gantt 页面后，看**顶部工具栏最右侧**
4. 会看到深色徽章显示当前角色（默认 **aps.planner**）
5. **点击徽章** → ElDropdown 弹出 4 个 DDL 角色：`aps.viewer.management` / `aps.planner` / `aps.admin.aps` / `aps.admin.system`

> 重要：**这是项目里唯一能切角色的地方**。不在用户菜单下，不在右上角头像。**只在 Gantt 页**。

#### 4.2.3 走 4 DDL 角色 × 22 路由矩阵（18 步抽查子集）

每一步 = 切角色 + 访问路由 + 期望现象。逐项过。

| # | 操作 | 期望 | 失败时 |
|---|---|---|---|
| 1 | 切到 `aps.viewer.management`，地址栏直接输 `/aps/rules` 回车 | 跳到 `/403?from=/aps/rules` | 没跳 → 守卫未触发 → 检查 [aps.ts:153](../src/router/modules/aps.ts#L153) |
| 2 | 切到 `aps.viewer.management`，地址栏输 `/aps/ctp` | 跳 `/403?from=/aps/ctp` | 同上 |
| 3 | 切到 `aps.viewer.management`，地址栏输 `/aps/manual-eta` | 跳 `/403?from=/aps/manual-eta` | 同上 |
| 4 | 切到 `aps.viewer.management`，地址栏输 `/aps/domain` | 跳 `/403?from=/aps/domain` | 同上 |
| 5 | 切到 `aps.viewer.management`，点左侧菜单「甘特图」 | 正常进入（只读页全员放行） | 跳 403 → 权限码联合类型错 |
| 6 | 切到 `aps.viewer.management`，访问 `/aps/demand-protection`，看「释放」按钮 | **disabled**（dev seed 仅 admin.system 持 `aps.demand_protection.release`） | 按钮可见 → canWrite 没用权限码 |
| 7 | 切到 `aps.planner`，地址栏输 `/aps/rules` | 跳 `/403?from=/aps/rules` | 没跳 → permissions 漏配 |
| 8 | 切到 `aps.planner`，地址栏输 `/aps/ctp` | 正常进入（持有 aps.ctp.evaluate） | 跳 403 → 权限码拼写错 |
| 9 | 切到 `aps.planner`，地址栏输 `/aps/manual-eta` | 正常进入（角色门控） | 跳 403 → 角色拼写错 |
| 10 | 切到 `aps.planner`，访问 `/aps/manual-eta`，看「新增」按钮 | 可点（持有 `aps.manual_eta.edit`） | disabled → canWrite 没用权限码 |
| 11 | 切到 `aps.planner`，访问 `/aps/demand-protection`，看「释放」按钮 | **disabled**（planner 不持 `aps.demand_protection.release`） | 按钮可见 → canWrite 没用权限码；旧别名 `PMC` 残留 |
| 12 | 切到 `aps.planner`，访问 `/aps/run`，看 MES「下发」按钮 | **disabled**（planner 不持 `aps.mes.dispatch`） | 同 11 |
| 13 | 切到 `aps.admin.aps`，地址栏输 `/aps/rules` | 正常进入（持有 rule.edit+publish） | 跳 403 → permissions 漏配 |
| 14 | 切到 `aps.admin.aps`，地址栏输 `/aps/domain` | 跳 `/403?from=/aps/domain`（仅 admin.system） | 没跳 → 角色门控失效 |
| 15 | 切到 `aps.admin.aps`，地址栏输 `/aps/manual-eta` | 跳 `/403?from=/aps/manual-eta`（admin.aps 不在 aps.planner\|admin.system 白名单） | 没跳 → 路由 meta 漏配 |
| 16 | 切到 `aps.admin.system`，地址栏输 `/aps/domain` | 正常进入 | 跳 403 → `MOCK_ROLE_PRESETS.aps.admin.system.roles` 漏配 |
| 17 | 切到 `aps.admin.system`，任意 16 路由 | 全员放行 | 任一跳 → `MOCK_ROLE_PRESETS.aps.admin.system.permissions` 漏配 |
| 18 | 切到 `aps.admin.system`，访问 `/aps/demand-protection`，看「释放」按钮 | 可点（admin.system 持 `aps.demand_protection.release`） | disabled → canWrite 没用权限码 |

**预期**：18 步全过。任一步红，去检查对应行的文件:行号列。

#### 4.2.4 控制台辅助

打开 DevTools Console（F12），每次越权跳转会有警告日志：

```
[APS RBAC] 路由 /aps/rules 拒绝: 路由要求权限 aps.rule.edit/aps.rule.publish，当前权限 aps.plan.view/aps.candidate.confirm/...
```

正常放行无日志。日志格式：[src/router/guard.ts:90](../src/router/guard.ts#L90)。

#### 4.2.5 验证后清理

dev server 可保留继续测试，**不需要停**。如要停：`Ctrl+C` 终止终端 `pnpm dev`。

---

## 五、与 Pkg-4 / 按钮级门控的衔接

| 场景 | 第 1 道（路由）| 第 2 道（按钮）| 第 3 道（store）|
|---|---|---|---|
| `aps.viewer.management` 访问 `/aps/candidate` | ❌ 跳 403 | — | — |
| `aps.planner` 访问 `/aps/candidate`，选 INJECTION Candidate 点「确认采用」 | ✅ 放行 | ✅ canActivate=true → 按钮可点 | ✅ assertScope 过 |
| `aps.planner` 访问 `/aps/candidate`，手动改 `canActivate=false` 的 WHATIF | ✅ 放行 | ❌ 按钮变「禁止激活」 | — |
| `aps.planner` 访问 `/aps/candidate`，强制加载 ASSEMBLY Candidate（绕过列表过滤）| ✅ 放行 | ✅ canActivate=true → 按钮可点 | ❌ `assertScope` 报错「业务范围越界（domainKeys）...」，不发 API |
| `aps.planner` 访问 `/aps/demand-protection`，点「释放」按钮 | ✅ 放行 | ❌ 按钮 disabled（planner 不持 `aps.demand_protection.release`）| — |
| `aps.planner` 访问 `/aps/run`，点 MES「下发」按钮 | ✅ 放行 | ❌ 按钮 disabled（planner 不持 `aps.mes.dispatch`）| — |

**三道层递进关系**：路由级最粗（「能不能进」）→ 按钮级中（「能不能点」）→ store 级最细（「能不能成交」）。任一道失败即拒绝，不会跳到下一道。

---

## 六、改动文件清单（按 Pkg-1 + P0 重构）

### 权限码 + DDL 角色码升级（2026-09-11）

| 文件 | 变更 |
|---|---|
| [src/api/aps-v1/types/common.ts](../src/api/aps-v1/types/common.ts) | `ROLE_KEYS` 扩为 7 个 DDL 角色码（aps.* 前缀）；新增 `ROLE_LABELS` 中文映射 |
| [src/store/modules/aps/auth.ts](../src/store/modules/aps/auth.ts) | `MOCK_ROLE_PRESETS` 重写为 4 个 DDL 角色 + 3 个 viewer 级 mock；新增 `has(perm)` 权限码方法；`isPmc/isRuleAdmin/isSystemAdmin/isViewer` 重新绑定 DDL 角色 |
| [src/store/modules/aps/scope.ts](../src/store/modules/aps/scope.ts) | `MOCK_ROLE_SCOPE_PRESETS` 键名改为 DDL 角色码 |
| [src/api/aps-v1/auth.ts](../src/api/aps-v1/auth.ts) | `mockLoginResponse.roles` 改用 DDL 角色码 |
| [src/api/aps-v1/rule.ts](../src/api/aps-v1/rule.ts) | `mockActor.roles` 改用 DDL 角色码（合并为 `aps.admin.aps`）|
| [src/router/modules/aps.ts](../src/router/modules/aps.ts) | 3 处 `apsRequiredRoles` 改用 DDL 角色码 |
| [src/store/modules/aps/candidate.ts](../src/store/modules/aps/candidate.ts) | `roleAllowedToConfirm` 改用 `aps.candidate.confirm` 权限码 |
| [src/store/modules/aps/rules.ts](../src/store/modules/aps/rules.ts) | `canWrite` 用 `aps.rule.edit`；`canPublishAction` 用 `aps.rule.publish` |
| [src/views/Aps/ManualEta.vue](../src/views/Aps/ManualEta.vue) | `canWrite` 用 `aps.manual_eta.edit` |
| [src/views/Aps/DemandProtection.vue](../src/views/Aps/DemandProtection.vue) | `canWrite` 用 `aps.demand_protection.release`（行为变化：planner 不再有释放按钮）|
| [src/views/Aps/Run.vue](../src/views/Aps/Run.vue) | `canRecoverRun` 用 `aps.plan.run`；`canDispatchMes` 用 `aps.mes.dispatch`（行为变化：planner 不再有下发按钮）|
| [src/views/Aps/Gantt.vue](../src/views/Aps/Gantt.vue) | `canManualReschedule` 用 `aps.reschedule.manual`；`ROLE_TAG` 键名改为 DDL 角色码 |
| [src/views/Aps/Domain.vue](../src/views/Aps/Domain.vue) | `canWrite` 用 `aps.admin.system` DDL 角色码（Domain 是治理类，无特定权限码，保留角色门控）|
| [scripts/verify-rbac.mjs](../scripts/verify-rbac.mjs) | `ROLE_PRESETS` 改用 DDL 角色码；`EXPECTED` 矩阵 4×16 = 64 组合 |

### 路由 meta 挂载（三波 + Pkg-8）

| 波次 | 路由 | meta 字段 | 文件:行 |
|---|---|---|---|
| 第一波 | `/aps/rules` | `apsRequiredPermissions: ['aps.rule.edit', 'aps.rule.publish']` | [aps.ts:153](../src/router/modules/aps.ts#L153) |
| 第一波 | `/aps/domain` | `apsRequiredRoles: ['aps.admin.system']` | [aps.ts:168](../src/router/modules/aps.ts#L168) |
| 第二波 | `/aps/ctp` | `apsRequiredPermissions: ['aps.ctp.evaluate']` | [aps.ts:122](../src/router/modules/aps.ts#L122) |
| 第二波 | `/aps/candidate` | `apsRequiredPermissions: ['aps.candidate.confirm']` | [aps.ts:136](../src/router/modules/aps.ts#L136) |
| 第二波 | `/aps/manual-eta` | `apsRequiredRoles: ['aps.planner', 'aps.admin.system']` | [aps.ts:199](../src/router/modules/aps.ts#L199) |
| 第二波 | `/aps/demand-protection` | `apsRequiredRoles: ['aps.planner', 'aps.admin.system']` | [aps.ts:215](../src/router/modules/aps.ts#L215) |
| 第三波 | 6 只读页（overview/order/gantt/explanation/run/pi）| `apsRequiredPermissions: ['aps.plan.view']` | [aps.ts](../src/router/modules/aps.ts) 多处 |
| Pkg-8 | 4 RBAC 管理 UI 页（users/roles/permissions/scopes）| `apsRequiredPermissions: ['aps.auth.user.edit']` | [aps.ts](../src/router/modules/aps.ts) |
| v1.4 | `/aps/strategy-profile` | `apsRequiredPermissions: ['aps.strategy.view', 'aps.strategy.edit', 'aps.strategy.publish']`（OR） | [aps.ts](../src/router/modules/aps.ts) |
| v1.5 | `/aps/setup/exact` / `/aps/setup/default` | `apsRequiredPermissions: ['aps.setup.view', 'aps.setup.edit', 'aps.setup.publish']`（OR） | [aps.ts](../src/router/modules/aps.ts) |
| v1.5 | `/aps/setup/uncovered` | `apsRequiredPermissions: ['aps.setup.view']` | [aps.ts](../src/router/modules/aps.ts) |
| v1.5 | `/aps/setup/diff` | `apsRequiredPermissions: ['aps.setup.view', 'aps.setup.publish']`（OR） | [aps.ts](../src/router/modules/aps.ts) |
| 09-20 | `/aps/audit` | `apsRequiredPermissions: ['aps.audit.view']`（4 角色均持，全员放行） | [aps.ts](../src/router/modules/aps.ts) |

### 验证工具（NEW）

| 文件 | 行数 | 作用 |
|---|---|---|
| [scripts/verify-rbac.mjs](../scripts/verify-rbac.mjs) | ~250 | 离线 88 组合 RBAC 矩阵验证（DDL 角色码版）|

---

## 七、为什么 v1.0 根因不复存在

[[feedback_rbac_aps_routes]] 记录了 v1.0 教训：「9 类路由禁 meta.permission/apsRequiredRoles」。

**根因**：原 `mock/role/index.mock.ts` 把 admin 角色注册到了**老版本路由树**（workbench/gantt/approval/versions/unfreeze/ctp/monitor/strategy/audit-log/explain），老权限码与新路由对不上 → 老权限码失效 + 新路由被错杀。

**v1.2 为什么不再怕**：
1. **路由来源变了**：4号位 16 类路由是 `apsV1Routes` **静态挂载**（[src/router/modules/aps.ts](../src/router/modules/aps.ts) 导出后合并进 `constantRouterMap`），**不走** `mock/role/index.mock.ts` 的动态注册
2. **角色数据独立**：[src/store/modules/aps/auth.ts](../src/store/modules/aps/auth.ts) `MOCK_ROLE_PRESETS` 与 mock 路由树**完全解耦**，单独维护 4 DDL 角色 → permissions/dataScope
3. **权限码定义统一**：[src/api/aps-v1/types/auth.ts](../src/api/aps-v1/types/auth.ts) `permissions` 是 `string[]`，与后端 34 码严格一致（前端用 `apsAuth.has(perm)` 比对，不强制枚举）

**所以**：v1.2 路由级 RBAC 是安全的，可以在第一波 / 第二波 / 第三波任意时刻挂 meta，并由 [scripts/verify-rbac.mjs](../scripts/verify-rbac.mjs) 一键验证。

---

## 八、§二十五.3 角色塔缩的处理

**v1.1 §二十五.3 要求**：「规则管理员（RULE_ADMIN）不能自动拥有规则发布权」（即「RULE_ADMIN ≠ RULE_PUBLISHER」）。

**v1.2 DDL 决策**：合并为单一 `aps.admin.aps` 角色（dev seed 下 rule_admin 和 rule_publisher 两个测试用户都挂这个角色），原角色层的不变量**无法再用角色码独立表达**。

**前端解决方案**：用权限码区分
- `aps.rule.edit` — 写 DRAFT 按钮：admin.aps + admin.system 都持
- `aps.rule.publish` — 发布按钮：admin.aps + admin.system 都持（dev seed 下两者都含 publish 码）
- 后端 `/api/rules/publish` 二次校验同码（[3号位契约-依赖与扩展项 §二.⑤](3号位契约-依赖与扩展项.md)）

**未来拆分**：如需进一步区分发布权，**只能在 3 号位新建独立角色码**（如 `aps.publisher.aps`），前端 1 行 `apsRequiredRoles` 即可调整。

---

## 九、阻塞项 / 下一轮

| 项 | 阻塞 | 解锁条件 |
|---|---|---|
| `GET /api/domain/definitions` 真实契约 | 3号位（DomainController 未实现）| 3 号位实现 DomainController；前端已 try/catch 回落 mock |
| `/api/candidate/confirm` 后端二次校验 | 3号位 | scope + 权限码 `aps.candidate.confirm` 双校验 |
| `/api/demand-protection/release` 后端 scope + 权限码校验 | 3号位 | `aps.demand_protection.release` 校验 |
| `/api/rules/publish` 后端角色 + 权限码校验 | 3号位 | `aps.rule.publish` 校验；RULE_ADMIN 场景演示（v1.2 DDL 已合并，需 3 号位拆角色）|
| `/api/mes/dispatch` 后端校验 | 3号位 | `aps.mes.dispatch` 校验 |
| ~~Pkg-8 审计日志页（4 号位文档第 17 节硬要求）~~ | ✅ 已闭环（2026-09-20） | `/aps/audit` 页落地 + `GET /api/rbac/audit-logs` curl 实测就绪 + verify-integration [I] 段 3 断言全绿 |
| Login/Logout 不写审计（§22.6 要求审计覆盖登录动作，U42 验收口径）| 3号位（AuthService 无 AuditLog 写入点）| 3号位 补写入点后，前端仅需 [types/audit.ts](../src/api/aps-v1/types/audit.ts) `AUDIT_ACTION_CODES` 追加枚举，Audit.vue 无需改动 |
| dev 库 planner / admin.aps 的 `aps.audit.view` 真实 RolePermission 绑定 | 3号位（RolePermission 无 seed，靠运行时配置）| 确认已绑；未绑则真实模式该两角色 `/aps/audit` 403（环境配置缺口，非前端 bug——mock 预设已按 §1.2 补齐）|
| Setup 维护 13 端点（`/api/governance/setup-rules/*` 等，v1.5 §11.1） | 3号位（09-20 首波代码路由/权限码偏离契约，见补发函 §七） | 09-25 合入 + Q4 路径裁决（结论 A 前端零改动 / 结论 B 0.5 人天）；`GROUP=setup node scripts/verify-integration.mjs` 12 断言全绿验收 |
| `aps.setup.*` 三码（常量 + seed + 角色绑定） | 3号位（催办单 §十 修改点） | 落地后真实模式 4 菜单 403 解除；dev 演示期间走 `VITE_USE_MOCK=true`（.env.base 临时开关） |

---

## 十、文档指针

- [[P1-14-15-权限场景测试矩阵]](P1-14-15-权限场景测试矩阵.md) — §二十五 7 项权限场景验证
- [[v1.2-完成报告]](v1.2-完成报告.md) — Pkg 整体进度 + P0 重构条目
- [[3号位契约-依赖与扩展项]](3号位契约-依赖与扩展项.md) — 后端需要落地的契约
- [[3号位契约-种子数据补齐]](3号位契约-种子数据补齐.md) — dev seed 4 用户 + 9 scope + 3 业务角色挂权限
- [[project_aps_v1_v1_2_domain_rebase]] — v1.2 整改地图（pkg 状态 + 命名规范）
