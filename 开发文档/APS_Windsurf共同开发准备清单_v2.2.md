# APS Windsurf 共同开发准备清单 v2.3

**版本**：v2.3  
**日期**：2026-05-13  
**项目名称**：Lean APS V1.0  
**团队规模**：5人开发团队（1-5号位，0号位不参与编码）  
**技术栈**：C# + .NET 8.0 + SQL Server 2019 + Vue 3 + TypeScript  
**版本控制**：SVN（内部服务器）  
**开发模式**：Windsurf AI 辅助编程 + 同地协作

**v2.3 更新说明**（2026-05-13 Batch 3 排程运行编排 + 读模型表 + 階段二骨架表，对齐 DDL v5.0.25 / 总表 v3.17）：
- 🆕 新增 **§八。Batch 3 开发任务清单**：9 张表 DDL + API 实现任务
- 📌 文档引用更新：DDL v5.0.24 → v5.0.25；防腐层 v1.20；字段说明 v5.0.25；内部契约 v2.11；API规范 v2.4

**v2.2 更新说明**（2026-03-23）：
- ✅ 更新必读文档清单（新增开发规则、内部契约等）
- ✅ 新增6条架构红线（库存五层架构、MaterialCode编码规则等）
- ✅ 更新 `.windsurf/rules.md` 内容（补充最新架构红线）
- ✅ 更新 `.windsurf/contracts/internal-contracts.yml` 内容（补充库存和主数据类型）
- ✅ 新增 MaterialSupplyContext 同步逻辑说明
- ✅ 更新 2号位职责（新增 MaterialSupplyContext 维护）

---

## 📋 一、共同准备事项（所有人必做）

### 1.1 文档准备与确认 ✅

**必读文档清单**（所有人必须通读至少1遍）：

**第一优先级（开发规则与契约）**：
1. `.windsurf/rules.md` - 统一开发规则（架构红线）⭐ **最优先**
2. `APS_内部核心域契约与插件规范_v2.5.md` - 内部契约（Markdown，可读性强）⭐ **v2.2新增**
3. `.windsurf/contracts/internal-contracts.yml` - 内部契约（YAML，结构化）⭐ **v2.2新增**
4. `开发规则变更通知_2026-03-23.md` - 最新变更通知 ⭐ **v2.2新增**

**第二优先级（架构设计）**：
5. `APS_总体方案设计_v2.md` - 总体方案
6. `APS_数据架构与防腐层设计方案_v5.0.md` - 数据架构（当前版 v1.20）
7. `APS_分域计算设计方案_v1.0.md` - 分域计算

**第三优先级（数据库设计）**：
8. `APS_数据库字段说明文档_v5.0.md` - 字段说明（当前版 v5.0.25）
9. `APS_数据库表结构设计_v5.0.sql` - DDL脚本（当前版 v5.0.25）

**第四优先级（接口规范）**：
10. `APS_应用层API接口规范_v2.3.md` - API规范（文件名 v2.3，内部版本 v2.4）
11. `APS_集成接口设计_v1.12.md` - 集成接口（文件名 v1.12，内部版本 v1.17）

**参考文档**：
12. `APS_需求澄清文档_v3.md` - 需求澄清
13. `APS 核心排产全流程走查 (完整版).md` - 流程走查
14. `APS_各类基础数据分层承接与演变总表_定稿版_v3.md` - 数据演进全景图
15. `Lean APS - 研发职责与执行任务包 (2).md` - 各号位任务包

**新成员学习路径**（推荐阅读顺序）：
1. **第1天上午**：先读 `.windsurf/rules.md` → 理解架构红线（30分钟）⭐ **v2.2调整**
2. **第1天下午**：再读《内部契约v2.2》→ 理解接口契约（1小时）⭐ **v2.2调整**
3. **第2天上午**：再读《总体方案设计v2》→ 理解整体架构（30分钟）
4. **第2天下午**：再读《数据架构与防腐层设计v1.0》→ 理解数据流向（1小时）
5. **第3天**：深入《数据库字段说明 v5.0.25》→ 数据库设计细节（1小时）
6. **第4天**：深入《核心排产流程走查》→ 排程流程详解（1小时）
7. **第5天**：根据自己的号位，深入阅读相关技术文档

**确认清单**：
- [ ] 每个人已完成各自负责文档的通读（至少1遍）
- [ ] 每个人已理解自己的职责边界和架构红线
- [ ] 每个人已理解项目的整体架构和技术栈
- [ ] 所有人已确认对业务逻辑有基本理解
- [ ] 所有人已理解最新的6条架构红线 ⭐ **v2.2新增**

---

### 1.2 Windsurf 环境配置（统一标准）

**基础配置**（所有人必须）：

1. **安装 Windsurf IDE**
   - 下载最新版本
   - 配置 AI 模型（推荐 Claude 3.5 Sonnet）

2. **配置工作区**
   ```bash
   # 检出 SVN 代码仓库到本地
   svn checkout <SVN仓库地址> d:\CascadeProjects\APS-Code
   
   # 在 Windsurf 中打开工作区
   # File -> Open Workspace -> 选择 d:\CascadeProjects\APS-Code
   ```

3. **配置 .windsurf 目录**
   
   **目录结构**：⚠️ **更新日期：2026-03-31**
   ```
   .windsurf/
   ├── docs/                          # 文档索引（供AI快速参考）
   │   └── README.md                  # 核心文档索引
   ├── contracts/                     # 契约定义
   │   ├── api-contracts.yml          # API契约（OpenAPI 3.0）⭐ 已创建
   │   └── internal-contracts.yml     # 内部接口契约 ⭐ v2.2已更新
   ├── templates/                     # 代码与文档模板
   │   ├── code/                      # 各号位代码模板+测试模板
   │   └── README.md                  # 模板使用说明
   ├── workflows/                     # 工作流定义
   │   ├── daily-dev.md               # 日常开发流程
   │   ├── code-review.md             # 代码审查流程
   │   ├── conflict-resolution.md     # 冲突解决流程
   │   ├── ai-prompts-guide.md        # 各号位AI协作提示词指南 ⭐ 已创建
   │   └── README.md                  # 工作流说明
   └── rules.md                       # 统一开发规则 ⭐ v2.2已更新
   ```
   
   **必须创建的文件**：
   - `rules.md`：统一开发规则（见 1.5 节）⭐ **v2.2已更新**
   - `contracts/internal-contracts.yml`：内部契约定义 ⭐ **v2.2已更新**
   - `contracts/api-contracts.yml`：API契约定义 ⭐ **已创建**
   - `workflows/daily-dev.md`：日常开发流程 ⭐ **已创建**
   - `workflows/ai-prompts-guide.md`：AI协作提示词指南 ⭐ **已创建**
   - `templates/code/`：各号位代码模板 ⭐ **已创建**

**确认清单**：
- [ ] 所有人已安装 Windsurf IDE
- [ ] 所有人已配置好工作区
- [ ] 所有人已创建 `.windsurf` 目录结构
- [ ] 已创建/更新契约文件（internal-contracts.yml v2.0）⭐ **v2.2新增**
- [ ] 已创建/更新规则文件（rules.md）⭐ **v2.2新增**
- [ ] 已创建工作流文件（workflows/*.md）

---

### 1.3 开发环境搭建（统一标准）

**后端环境**（1/2/3/5号位）：

```bash
# 1. 安装 .NET 8.0 SDK
winget install Microsoft.DotNet.SDK.8

# 2. 安装 SQL Server 2019 Developer Edition
# 下载地址：https://www.microsoft.com/sql-server/sql-server-downloads

# 3. 安装 SQL Server Management Studio (SSMS)
# 下载地址：https://aka.ms/ssmsfullsetup

# 4. 验证环境
dotnet --version  # 应显示 8.0.x
```

**前端环境**（4号位）：

```bash
# 1. 安装 Node.js 20 LTS
winget install OpenJS.NodeJS.LTS

# 2. 安装 pnpm（推荐）
npm install -g pnpm

# 3. 验证环境
node --version  # 应显示 v20.x.x
pnpm --version  # 应显示 8.x.x
```

**确认清单**：
- [ ] 后端开发人员已安装 .NET 8.0 SDK
- [ ] 后端开发人员已安装 SQL Server 2019
- [ ] 前端开发人员已安装 Node.js 20 LTS
- [ ] 所有人已验证环境配置正确

---

### 1.4 SVN 仓库规划与分支策略

**仓库结构**：

```
APS-Code/
├── .windsurf/                    # Windsurf 配置目录
│   ├── docs/                     # 设计文档
│   ├── rules.md                  # 统一开发规则
│   ├── contracts/                # 契约定义
│   └── workflows/                # 工作流定义
├── src/
│   ├── APS.Core/                 # 核心领域（1号位）
│   ├── APS.Engine/               # 稳定引擎（2号位）
│   ├── APS.Orchestrator/         # 调度编排（3号位）
│   ├── APS.WebUI/                # 前端界面（4号位）
│   ├── APS.BusinessRules/        # 业务规则（5号位）
│   └── APS.Shared/               # 共享库
├── database/
│   ├── schema/                   # 数据库表结构
│   ├── views/                    # 视图定义
│   └── procedures/               # 存储过程
├── tests/
│   ├── APS.Core.Tests/
│   ├── APS.Engine.Tests/
│   └── APS.Integration.Tests/
└── docs/                         # 项目文档
```

**SVN 目录结构**（标准 trunk/branches/tags 结构）：

```
svn://server/APS/
├── trunk/                              # 主干，稳定代码
├── branches/                           # 分支目录
│   ├── develop/                        # 开发主分支
│   ├── feature-1-core-engine/          # 1号位功能分支
│   ├── feature-2-stable-framework/     # 2号位功能分支
│   ├── feature-3-orchestrator/         # 3号位功能分支
│   ├── feature-4-webui/                # 4号位功能分支
│   └── feature-5-business-rules/       # 5号位功能分支
└── tags/                               # 标签目录（版本发布）
    ├── v1.0.0/
    └── v1.1.0/
```

**分支命名规范**：
- 功能分支：`feature-<号位>-<功能描述>`
- 修复分支：`bugfix-<问题描述>`
- 紧急修复：`hotfix-<问题描述>`

**SVN 工作流程**：
1. **检出代码**：`svn checkout svn://server/APS/branches/develop d:\APS-Code`
2. **创建分支**（由2号位统一创建）：`svn copy svn://server/APS/branches/develop svn://server/APS/branches/feature-1-core-engine -m "创建1号位功能分支"`
3. **切换分支**：`svn switch svn://server/APS/branches/feature-1-core-engine`
4. **提交代码**：`svn commit -m "提交信息"`
5. **更新代码**：`svn update`
6. **合并分支**（由2号位统一执行）：`svn merge svn://server/APS/branches/feature-1-core-engine`

**确认清单**：
- [ ] SVN 仓库已创建并初始化（trunk/branches/tags 结构）
- [ ] 所有人已检出代码到本地
- [ ] 2号位已创建各号位的功能分支
- [ ] 所有人已理解 SVN 工作流程

---

### 1.5 统一开发规则文件（.windsurf/rules.md）⭐ v2.2已更新

**创建统一规则文件**（所有人必须遵守）：

```markdown
# APS Windsurf 统一开发规则 v2.2

**更新日期**：2026-03-23  
**版本**：v2.2

## 🚫 架构红线（所有人必须遵守）

### 红线1：严守职责边界
- 1号位：只写计算域代码，严禁 I/O 操作
- 2号位：只写框架代码，不写业务逻辑
- 3号位：只写编排代码，不写计算逻辑
- 4号位：只写前端代码，不写后端逻辑
- 5号位：只写业务规则，不写框架代码

### 红线2：严禁跨界修改
- 严禁修改其他号位负责的代码
- 如需修改，必须通过 Pull Request 并经过对方审核

### 红线3：严格遵守契约
- 所有接口必须严格遵守契约定义
- 严禁私自修改接口签名或字段
- 如需修改契约，必须先更新契约文档并通知相关方

### 红线4：数据库修改流程
- 所有数据库修改必须由 2号位统一执行
- 其他人不得直接修改数据库结构
- 如需修改，提交变更申请给 2号位

### 红线5：代码提交规范
- 每次提交前必须先更新代码（svn update）
- 提交信息必须清晰描述改动内容
- 严禁提交未经测试的代码
- 提交前必须解决所有冲突

### 红线6：不要让数据库替你"擦屁股"
- SqlBulkCopy 前必须 .Distinct() 去重
- 不允许依赖数据库约束来防呆
- 应用层必须保证数据干净

### 红线7：抛弃"一物一码"的单线条思维
- 物料映射 API 返回列表，不是单对象
- 禁止 .First() 瞎拿第一条
- 必须结合优先级判定表选择合适的映射

## 🚨 架构红线（v2.2 新增）

### 红线8：库存五层架构必须严格遵守 ⭐ v2.2新增
- Layer 1（事实层）：保留物理主键（MasterID+Warehouse, MES_ID+Location）
- Layer 2（候选供给池）：首次统一到 MaterialCode
- Layer 3（规则筛选层）：产品族级别的仓库范围和来源规则
- Layer 4（可用库存）：规则筛选后的排程可用库存
- Layer 5（内存消费层）：排程引擎内存中的库存消费
- ❌ 禁止跨层访问，必须逐层演进

### 红线9：MaterialCode 编码规则必须严格遵守 ⭐ v2.2新增
- 格式：`{类型前缀}-{物料型号}-{版本号(可选)}`
- 类型前缀：RAW-（原材料）、FG-（成品）、WIP-（半成品）、ASSY-（装配件）（v5.0.1变更 2026-04-02：取消MES-，增加ASSY-）
- ❌ 禁止在 MaterialCode 中写入：MTO/MTS、订单号、客户特征、仓库信息、责任部门信息
- 版本号：默认不启用，仅在工程变更导致物料业务身份不兼容时启用

### 红线10：物料供给上下文必须下沉到仓库级别 ⭐ v2.2新增
- Material 表：只存储物料本体属性（MaterialCode、MaterialName、Spec、MaterialType、UOM）
- MaterialSupplyContext 表：存储仓库级供给上下文（SupplyMode、ProductionDeptCode、LeadTimeDays、SafetyStock等）
- ❌ 禁止在 Material 表中存储仓库级参数
- ✅ 必须支持同一物料在不同仓库有不同的供给方式和参数

### 红线11：库存事实表必须使用物理主键 ⭐ v2.2新增
- InventoryFact_ERP：主键为 `MasterID + Warehouse`（物理主键）
- InventoryFact_MES：主键为 `MES_ID + Location`（物理主键）
- MaterialCode 为辅助字段，通过 MaterialMapping 桥接获得
- ❌ 禁止使用 MaterialCode 作为库存事实表的主键

### 红线12：ext_视图必须统一到 APS_Production 库 ⭐ v2.2新增
- ext_ERP_Master_View：在 APS_Production 库创建，访问 ODS 库的 ERP_Master_View
- ext_MES_Material_View：在 APS_Production 库创建，访问 ODS 库的 MES_Material_View
- ext_ERP_Inventory_View：在 APS_Production 库创建，访问 ODS 库的 ERP_Inventory_View
- ext_MES_Inventory_View：在 APS_Production 库创建，访问 ODS 库的 MES_Inventory_View
- ❌ 禁止在 ODS 库创建 ext_视图

### 红线13：sp_SyncMaterialMapping 必须同步三张表 ⭐ v2.2新增
- 步骤1-2：同步 MaterialMapping 表（ERP 和 MES 双源）
- 步骤3：统计失效物料数量
- 步骤4：同步 Material 表（物料本体属性）
- 步骤5：同步 MaterialSupplyContext 表（仓库级供给上下文）⭐ **v2.2新增**
- 步骤6：记录日志
- ❌ 禁止遗漏 MaterialSupplyContext 的同步

## 📝 Windsurf 使用规范

### 规范1：先输出计划，再执行
- 每次改动前，先让 Windsurf 输出计划
- 计划包括：文件清单 + 改动点 + 风险点 + 验证方式
- 确认计划无误后，再执行改动

### 规范2：小步快跑
- 每次只做一个小功能
- 改完立即运行测试/构建
- 确保每次提交都是可运行的

### 规范3：失败必须修复
- 任何编译/测试失败必须在本轮内修复
- 不允许提交有错误的代码
- 如无法修复，回滚代码并寻求帮助

### 规范4：严格遵守契约
- 不允许"凭空新增字段/随意改口径"
- 所有字段以契约为准（contracts/internal-contracts.yml）
- 如需新增字段，必须先更新契约文档

## 🔧 代码规范

### C# 代码规范
- 使用 C# 11 语法
- 遵循 Microsoft C# 编码规范
- 热点路径禁用 LINQ（1号位）
- 使用 `struct`、`ref` 优化内存（1号位）

### TypeScript 代码规范
- 使用 TypeScript 5.0+
- 遵循 Airbnb TypeScript 规范
- 使用 Composition API（Vue 3）
- 使用 `<script setup>` 语法

### SQL 代码规范
- 使用 SQL Server 2019+ 语法
- 所有表名使用 PascalCase
- 所有字段名使用 PascalCase
- 索引命名：IX_<表名>_<字段名>

## 🧪 测试规范

### 单元测试
- 每个公共方法必须有单元测试
- 测试覆盖率要求 > 80%
- 使用 xUnit 框架

### 集成测试
- 关键流程必须有集成测试
- 测试数据使用 Mock 数据
- 不依赖真实外部系统

## 📦 提交规范

### Commit Message 格式
```
<type>(<scope>): <subject>

<body>

<footer>
```

**Type 类型**：
- feat: 新功能
- fix: 修复 bug
- docs: 文档更新
- style: 代码格式调整
- refactor: 重构
- test: 测试相关
- chore: 构建/工具相关

**示例**：
```
feat(core): 实现 TimeWindow 值类型结构

- 添加 TimeWindow struct
- 实现 Overlaps 方法
- 添加单元测试

Refs: #123
```
```

**确认清单**：
- [ ] 已创建/更新 `.windsurf/rules.md` 文件 ⭐ **v2.2已更新**
- [ ] 所有人已阅读并理解统一规则（包括新增的6条红线）⭐ **v2.2新增**
- [ ] 所有人已配置 Windsurf 使用此规则文件

---

### 1.6 契约文件配置（.windsurf/contracts/）⭐ v2.2已更新

**为什么需要契约文件？**

契约文件是团队协作的**金科玉律**，定义了：
1. **API 接口契约**：3号位和4号位之间的HTTP API规范
2. **内部接口契约**：1/2/3/5号位之间的C#接口规范

使用 YAML 格式的好处：
- ✅ Windsurf 可以直接读取和验证
- ✅ 可以自动生成代码和文档
- ✅ 可以集成到 CI/CD 流程

---

#### 1.6.1 内部接口契约（internal-contracts.yml）⭐ v2.2已更新

**文件路径**：`.windsurf/contracts/internal-contracts.yml`

**v2.2 更新内容**：
- ✅ 新增库存五层架构相关类型（InventorySupplyCandidate、InventoryBalance等）
- ✅ 新增 MaterialSupplyContext 类型
- ✅ 更新 Material 类型（新增 Spec 字段，标记废弃字段）
- ✅ 更新 MaterialMapping 类型（新增 Spec、ERP_Warehouse、MES_Location 字段）
- ✅ 新增 IInventoryFilterRule 接口

**使用方式**：
- 2号位：定义接口契约
- 1/5号位：根据契约实现接口
- Windsurf：读取契约验证实现

**确认清单**：
- [ ] 已创建/更新 `internal-contracts.yml` 文件（v2.0）⭐ **v2.2已更新**
- [ ] 1/2/5号位已理解内部接口契约
- [ ] 所有人承诺严格遵守契约，不私自修改

---

#### 1.6.2 API 契约文件（api-contracts.yml）

**文件路径**：`.windsurf/contracts/api-contracts.yml`

**使用方式**：
- 3号位：根据契约实现 API
- 4号位：根据契约调用 API
- Windsurf：读取契约自动生成代码

**确认清单**：
- [ ] 已创建 `api-contracts.yml` 文件
- [ ] 3号位和4号位已理解 API 契约

---

### 1.7 工作流文件配置（.windsurf/workflows/）

**为什么需要工作流文件？**

工作流文件定义了**标准操作流程**，让 Windsurf 可以：
- ✅ 自动引导开发人员完成日常任务
- ✅ 确保每个人都遵循统一的流程
- ✅ 减少沟通成本和错误

**确认清单**：
- [ ] 已创建 `workflows/daily-dev.md` 文件
- [ ] 已创建 `workflows/code-review.md` 文件
- [ ] 已创建 `workflows/conflict-resolution.md` 文件
- [ ] 所有人已理解工作流程
- [ ] 所有人已在 Windsurf 中测试工作流

---

## 📋 二、各号位专属准备事项

### 2.1 1号位（计算域核心开发）

**专属文档**：
- [ ] 精读《APS 核心排产全流程走查》中的"阶段3：排程计算"部分
- [ ] 精读《研发职责与执行任务包》中的"1号位"章节
- [ ] 理解 TimeWindow、IntervalTree 等数据结构

**专属配置**：
- [ ] 安装 BenchmarkDotNet（性能测试）
- [ ] 安装 dotMemory（内存分析）
- [ ] 配置 Visual Studio Profiler

**专属技能**：
- [ ] 熟悉 C# struct、ref 语义
- [ ] 熟悉 ObjectPool 模式
- [ ] 熟悉 GC 优化技巧

**架构红线**：
- ⛔ 严禁 I/O 操作（数据库、文件、网络）
- ⛔ 严禁 LINQ（热点路径）
- ⛔ 严禁隐式内存分配

---

### 2.2 2号位（技术负责人 + 稳定引擎）⭐ v2.2已更新

**专属文档**：
- [ ] 精读所有架构设计文档
- [ ] 精读《APS_数据库表结构设计_v5.0.sql》（当前版 v5.0.25）
- [ ] 精读《APS_数据架构与防腐层设计方案_v5.0.md》（当前版 v1.20）
- [ ] 精读《开发规则变更通知_2026-03-23.md》⭐ **v2.2新增**
- [ ] 精读《MaterialSupplyContext补充更新报告_2026-03-23.md》⭐ **v2.2新增**

**专属职责**：
- [ ] 负责数据库初始化（执行 DDL 脚本）
- [ ] 负责代码审查（特别是 5号位的业务插件）
- [ ] 负责技术选型与风险评估
- [ ] 负责性能监控框架搭建
- [ ] 负责 MaterialSupplyContext 表的维护 ⭐ **v2.2新增**
- [ ] 负责 sp_SyncMaterialMapping 存储过程的维护 ⭐ **v2.2新增**

**专属配置**：
- [ ] 安装 SQL Server Profiler
- [ ] 配置数据库连接池监控
- [ ] 配置 Application Insights（可选）

**架构红线**：
- ⛔ 框架代码不写业务逻辑
- ⛔ 提供插槽接口，由 5号位实现
- ⛔ APS 绝对拒绝凭空造单
- ⛔ sp_SyncMaterialMapping 必须同步三张表（MaterialMapping、Material、MaterialSupplyContext）⭐ **v2.2新增**

---

### 2.3 3号位（调度编排器）

**专属文档**：
- [ ] 精读《APS_应用层API接口规范_v2.3.md》
- [ ] 精读《APS_内部核心域契约与插件规范_v2.5.md》（v2.2）⭐ **v2.2更新**
- [ ] 理解分域并发编排逻辑

**专属职责**：
- [ ] 负责 HTTP API 开发
- [ ] 负责分域并发调度
- [ ] 负责与前端对接

**专属配置**：
- [ ] 安装 Postman 或 Apifox（API 测试）
- [ ] 配置 Swagger UI
- [ ] 配置日志框架（Serilog）

**架构红线**：
- ⛔ 只写编排代码，不写计算逻辑
- ⛔ 严格遵守 API 契约
- ⛔ 不允许私自修改接口签名

---

### 2.4 4号位（前端界面）

**专属文档**：
- [ ] 精读《APS_应用层API接口规范_v2.3.md》
- [ ] 理解甘特图交互需求
- [ ] 理解订单管理界面需求

**专属职责**：
- [ ] 负责 Vue 3 前端开发
- [ ] 负责甘特图集成（DHTMLX Gantt）
- [ ] 负责与后端 API 对接

**专属配置**：
```bash
# 创建前端项目
cd src
pnpm create vite APS.WebUI --template vue-ts
cd APS.WebUI
pnpm install

# 安装依赖
pnpm add vue-router@4 pinia element-plus
pnpm add axios dayjs
pnpm add dhtmlx-gantt
pnpm add -D @types/dhtmlx-gantt
```

**架构红线**：
- ⛔ 只写前端代码，不写后端逻辑
- ⛔ 严格遵守 API 契约
- ⛔ 不允许直接访问数据库

---

### 2.5 5号位（业务规则引擎）

**专属文档**：
- [ ] 精读《APS 核心排产全流程走查》
- [ ] 精读《研发职责与执行任务包》中的"5号位"章节
- [ ] 精读《APS_内部核心域契约与插件规范_v2.5.md》（v2.2）⭐ **v2.2更新**

**专属职责**：
- [ ] 实现业务规则插件（IPeggingRule、ILotSizingRule 等）
- [ ] 实现库存扣减逻辑
- [ ] 实现优先级计算逻辑
- [ ] 实现 IInventoryFilterRule 接口（库存筛选规则）⭐ **v2.2新增**

**专属配置**：
- [ ] 理解插件接口定义
- [ ] 准备业务规则测试数据

**架构红线**：
- ⛔ 只写业务规则，不写框架代码
- ⛔ 严格实现接口契约
- ⛔ 不允许修改框架代码

---

## 📋 三、协作演练方案（3轮）

### 3.1 第一轮演练：Hello World（1天）

**目标**：验证环境配置和基础协作流程

**任务**：
1. **2号位**：创建解决方案和项目结构
2. **1号位**：在 APS.Core 中创建 `TimeWindow.cs`
3. **3号位**：在 APS.Orchestrator 中创建 `HealthController.cs`
4. **4号位**：创建前端项目并调用健康检查接口
5. **5号位**：在 APS.BusinessRules 中创建 `IPeggingRule.cs`

**验收标准**：
- [ ] 所有项目编译通过
- [ ] 后端 API 可以正常启动
- [ ] 前端可以调用健康检查接口
- [ ] 所有人已提交代码到各自分支

---

### 3.2 第二轮演练：接口对接（2天）

**目标**：验证接口契约和跨模块协作

**任务**：
1. **2号位**：创建数据库并执行 DDL 脚本
2. **3号位**：实现订单查询 API（GET /api/orders）
3. **4号位**：实现订单列表页面
4. **1号位**：实现 TimeWindow 的 Overlaps 方法
5. **5号位**：实现简单的优先级计算规则

**验收标准**：
- [ ] 数据库已创建并初始化
- [ ] 订单查询 API 可以返回数据
- [ ] 前端可以显示订单列表
- [ ] 单元测试通过

---

### 3.3 第三轮演练：端到端流程（3天）

**目标**：验证完整的业务流程

**任务**：
1. **2号位**：实现 BOM 遍历框架
2. **3号位**：实现排程触发 API（POST /api/planning/schedule）
3. **1号位**：实现简单的排程算法
4. **5号位**：实现库存扣减规则
5. **4号位**：实现排程结果展示页面

**验收标准**：
- [ ] 可以触发排程计算
- [ ] 排程结果可以保存到数据库
- [ ] 前端可以展示排程结果
- [ ] 端到端流程跑通

---

## 📋 四、开发前必须确认的5大事项

### 4.1 架构理解确认

**确认方式**：每个人向 0号位口述

- [ ] 1号位：能清晰描述计算域的职责边界和架构红线
- [ ] 2号位：能清晰描述稳定引擎的框架设计和插槽接口
- [ ] 3号位：能清晰描述调度编排的分域并发逻辑
- [ ] 4号位：能清晰描述前端架构和与后端的交互方式
- [ ] 5号位：能清晰描述业务规则插件的实现方式
- [ ] 所有人：能清晰描述库存五层架构和 MaterialCode 编码规则 ⭐ **v2.2新增**

---

### 4.2 契约理解确认

**确认方式**：书面测试

- [ ] 3/4号位：能准确描述 API 契约中的接口签名和参数类型
- [ ] 1/2/5号位：能准确描述内部接口契约中的接口定义
- [ ] 所有人：能准确描述 MaterialSupplyContext 的字段定义 ⭐ **v2.2新增**

---

### 4.3 工具链验证

**确认方式**：实际操作

- [ ] 所有人：能成功编译项目
- [ ] 所有人：能成功运行测试
- [ ] 所有人：能成功提交代码到 SVN
- [ ] 所有人：能成功使用 Windsurf 读取契约文件

---

### 4.4 沟通机制确认

**确认方式**：团队讨论

- [ ] 确定每日站会时间（建议每天上午9:00）
- [ ] 确定代码审查流程（Pull Request + 审查人）
- [ ] 确定冲突解决机制（优先保留他人代码，协商解决）
- [ ] 确定紧急问题升级路径（2号位 → 0号位）

---

### 4.5 风险评估确认

**确认方式**：风险清单

- [ ] 识别技术风险（性能、并发、数据一致性等）
- [ ] 识别协作风险（沟通不畅、职责不清等）
- [ ] 识别进度风险（任务延期、资源不足等）
- [ ] 制定风险应对措施

---

## 📋 五、开发启动检查清单（最终确认）

### 5.1 文档准备 ✅

- [ ] 所有人已通读必读文档清单（至少1遍）
- [ ] 所有人已理解架构红线（包括 v2.2 新增的6条）⭐ **v2.2新增**
- [ ] 所有人已理解自己的职责边界
- [ ] 所有人已理解契约定义（API 契约和内部契约）

### 5.2 环境配置 ✅

- [ ] 所有人已安装 Windsurf IDE
- [ ] 所有人已配置好开发环境（.NET 8.0 / Node.js 20）
- [ ] 所有人已检出代码到本地
- [ ] 所有人已创建 `.windsurf` 目录结构

### 5.3 契约文件 ✅

- [ ] 已创建/更新 `.windsurf/rules.md` 文件（v2.2）⭐ **v2.2已更新**
- [ ] 已创建/更新 `.windsurf/contracts/internal-contracts.yml` 文件（v2.0）⭐ **v2.2已更新**
- [ ] 已创建 `.windsurf/contracts/api-contracts.yml` 文件
- [ ] 已创建工作流文件（workflows/*.md）

### 5.4 数据库准备 ✅

- [ ] 2号位已创建数据库（APS_Production、ODS等）
- [ ] 2号位已执行 DDL 脚本（APS_数据库表结构设计_v5.0.sql，当前版 v5.0.25）
- [ ] 2号位已创建 ext_视图（统一到 APS_Production 库）⭐ **v2.2新增**
- [ ] 2号位已创建 sp_SyncMaterialMapping 存储过程（包含 MaterialSupplyContext 同步）⭐ **v2.2新增**

### 5.5 协作演练 ✅

- [ ] 第一轮演练（Hello World）已完成
- [ ] 第二轮演练（接口对接）已完成
- [ ] 第三轮演练（端到端流程）已完成

---

## 📋 六、常见问题与解决方案

### 6.1 Windsurf 相关

**Q1：Windsurf 不读取契约文件怎么办？**
A1：确保契约文件路径正确（`.windsurf/contracts/`），并在提示词中明确指定文件路径。

**Q2：Windsurf 生成的代码不符合契约怎么办？**
A2：在提示词中明确要求验证契约，例如："请检查代码是否符合 `.windsurf/contracts/internal-contracts.yml` 中的定义"。

### 6.2 SVN 相关

**Q1：SVN 冲突如何解决？**
A1：参考 `.windsurf/workflows/conflict-resolution.md` 工作流，优先保留他人代码，协商解决。

**Q2：如何回滚代码？**
A2：使用 `svn merge -r HEAD:r<版本号> .` 回滚到指定版本。

### 6.3 架构相关

**Q1：如何判断是否违反架构红线？**
A1：参考 `.windsurf/rules.md` 中的架构红线清单，不确定时咨询 2号位。

**Q2：MaterialSupplyContext 表的同步逻辑在哪里？**⭐ **v2.2新增**
A2：在 `sp_SyncMaterialMapping` 存储过程的第5步，详见《APS_数据架构与防腐层设计方案_v5.0.md》（当前版 v1.20）第935-977行（行号以当前文档为准）。

---

## 📋 七、快速参考索引

### 7.1 文档快速查找

- 想了解开发规则？→ `.windsurf/rules.md`
- 想了解内部契约（可读）？→ `APS_内部核心域契约与插件规范_v2.5.md`（v2.2）
- 想了解内部契约（结构化）？→ `.windsurf/contracts/internal-contracts.yml`（v2.0）
- 想了解最新变更？→ `开发规则变更通知_2026-03-23.md`
- 想了解总体架构？→ `APS_总体方案设计_v2.md`
- 想了解数据架构？→ `APS_数据架构与防腐层设计方案_v5.0.md`（当前版 v1.20）
- 想了解数据库设计？→ `APS_数据库字段说明文档_v5.0.md`（当前版 v5.0.25）
- 想了解 MaterialSupplyContext？→ `MaterialSupplyContext补充更新报告_2026-03-23.md` ⭐ **v2.2新增**

### 7.2 关键概念快速查找

- 库存五层架构：`APS_内部核心域契约与插件规范_v2.5.md` 第十二章
- MaterialCode 编码规则：`APS_内部核心域契约与插件规范_v2.5.md` 第十三章
- MaterialSupplyContext 设计：`APS_内部核心域契约与插件规范_v2.5.md` 第十四章
- sp_SyncMaterialMapping 逻辑：`APS_数据架构与防腐层设计方案_v5.0.md`（当前版 v1.20）

---

## 📋 八、Batch 3 开发任务清单（v2.3 新增，2026-05-13）

对齐：DDL v5.0.25 / 总表 v3.17 / 防腐层 v1.20 / 内部契约 v2.11 / API规范 v2.4

### 8.1 DDL 实施任务（阶段一即用）

| # | 表名 | 责任号位 | 冈时（阐段） | 骨架/全量 |
|---|---|---|---|---|
| 3.1 | `ScheduleRun` | 2号位 | 阶段一 | 全量 |
| 3.2 | `PlanVersion` 追列 `ScheduleRunId` | 2号位 | 阶段一 | ALTER |
| 3.3 | `ScheduleExplanationFact` | 2号位 | 阶段一 | 全量 |
| 3.4 | `OrderScheduleSummary` | 2号位 | 阶段一 | 全量 |
| 3.5 | `ResourceLoadSummary` | 2号位 | 阶段一 | 全量 |
| 3.6 | `PlanKpiSummary` | 2号位 | 阶段一 | 全量 |
| 3.7 | `Scenario` | 3号位 | 阶段二（骨架） | 骨架 |
| 3.8 | `SimulationRun` | 3号位 | 阶段二（骨架） | 骨架 |
| 3.9 | `ScenarioObjectiveScore` | 3号位 | 阶段二（骨架） | 骨架 |

> DDL 脚本已写入 `APS_数据库表结构设计_v5.0.sql` Batch 3 區块（最后部分）。执行时按编号顺序：3.1 → 3.2（ALTER）→ 3.3-3.9。

### 8.2 后端 API 实现任务（阶段一即用）

| # | 端点 | 责任号位 | 依赖 |
|---|---|---|---|
| A1 | `POST /api/scheduling/runs` 触发 ScheduleRun | 3号位 | DDL 3.1 |
| A2 | `GET /api/scheduling/runs` 历史列表 | 3号位 | DDL 3.1 |
| A3 | `POST /api/scheduling/plan-versions/{id}/activate` | 3号位 | DDL 3.2 |
| A4 | `GET .../kpi` PlanKpiSummary 查询 | 3号位 | DDL 3.6 |
| A5 | `GET .../order-summary` 订单摘要查询 | 3号位 | DDL 3.4 |
| A6 | `GET .../resource-load` 资源负荷查询 | 3号位 | DDL 3.5 |

> API 字段规范见 `APS_应用层API接口规范_v2.3.md`（内部版本 v2.4） §六。一 / §六。二。

### 8.3 2号位排程内核路径任务

| # | 任务 | 说明 |
|---|---|---|
| B1 | 接收 3号位创建的 `ScheduleRunId`，注入 `ScheduleContext` | **不负责创建** `ScheduleRun` 初始记录（创建由 3号位负责）；接收后将 `ScheduleRunId` 传入排程内核 |
| B2 | `ScheduleRun.Status` + `OutputPlanVersionId` 回填 | 排程完成后与 Task/Pegging 同批次 |
| B3 | `ExplanationFactDraft` 收集 + 批量落 `ScheduleExplanationFact` | 禁止 1号位直接写 DB |
| B4 | `OrderScheduleSummary` 异步后处理生成 | Task/Pegging 落库后触发 |
| B5 | `ResourceLoadSummary` 异步后处理生成 | 同上 |
| B6 | `PlanKpiSummary` 异步后处理生成 | 同上 |

### 8.4 阶段二骨架表注意事项

- `Scenario` / `SimulationRun` / `ScenarioObjectiveScore` 阶段一仅建表，**不写入任何数据**。
- Windsurf 生成代码时禁止对这三张表实装任何业务逻辑，仅允许创建读写方法骨架。

---

**文档结束**

**交付时间**：2026-05-13  
**适用项目**：Lean APS V1.0  
**维护责任人**：2号位（技术负责人）  
**文档版本**：v2.3（含 Batch 3 开发任务清单）

**变更说明文档**：`APS_Windsurf共同开发准备清单_v2.2_变更说明.md`（仅说明 v2.1 → v2.2 的变更内容）
