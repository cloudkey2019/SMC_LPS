# Claude.md文档目录说明

## 一、根目录用的文件

### 1）`CLAUDE.md`

**放置位置：**

<pre class="overflow-visible! px-0!" data-start="82" data-end="112"><div class="relative w-full mt-4 mb-1"><div class=""><div class="relative"><div class="h-full min-h-0 min-w-0"><div class="h-full min-h-0 min-w-0"><div class="border border-token-border-light border-radius-3xl corner-superellipse/1.1 rounded-3xl"><div class="h-full w-full border-radius-3xl bg-token-bg-elevated-secondary corner-superellipse/1.1 overflow-clip rounded-3xl lxnfua_clipPathFallback"><div class="pointer-events-none absolute end-1.5 top-1 z-2 md:end-2 md:top-1"></div><div class="relative"><div class="pe-11 pt-3"><div class="relative z-0 flex max-w-full"><div id="code-block-viewer" dir="ltr" class="q9tKkq_viewer cm-editor z-10 light:cm-light dark:cm-light flex h-full w-full flex-col items-stretch ͼd ͼr"><div class="cm-scroller"><pre class="cm-content q9tKkq_readonly m-0"><code><span>APS-Code/CLAUDE.md</span></code></pre></div></div></div></div></div></div></div></div></div><div class=""><div class=""></div></div></div></div></div></pre>

**给谁用：**

全体成员、所有 Claude Code 会话都要读。

**作用：**

这是整个项目的“总宪法”，里面写的是：

- 项目背景
- 当前文档基线
- 技术栈
- 1～5号位职责边界
- Batch 3 核心口径
- `ScheduleRun / PlanVersion / RunType` 规则
- 阶段二骨架表只建表、不实装业务逻辑
- 数据库、API、开发方式、禁止事项

**最重要。必须放在代码仓库根目录。**

---

## 二、各号位目录下用的文件

### 2）`src/APS.Core/CLAUDE.md`

**给 1号位用。**

对应排程计算域。

主要约束：

- 只做内存排程计算
- 禁止 DB / 文件 / 网络 I/O
- 禁止直接写数据库
- 可以产出 `ExplanationFactDraft`
- 不允许直接写 `ScheduleExplanationFact`

---

### 3）`src/APS.Engine/CLAUDE.md`

**给 2号位用。**

对应稳定引擎、数据加载、批量持久化。

主要约束：

- 接收 3号位创建的 `ScheduleRunId`
- 注入 `ScheduleContext`
- 回填 `ScheduleRun.Status / OutputPlanVersionId`
- 持久化 `Task / Pegging / ScheduleExplanationFact / Summary`
- 负责三张 Summary 的异步生成
- 不写业务规则，不写排程算法

---

### 4）`src/APS.Orchestrator/CLAUDE.md`

**给 3号位用。**

对应调度编排、API、Hangfire。

主要约束：

- 创建 `ScheduleRun` 初始记录
- 实现 `POST /api/scheduling/runs`
- 实现 PlanVersion 激活 API
- 实现 Summary 查询 API
- 调用 2号位服务执行排程
- 不写排程算法，不写业务规则

---

### 5）`src/APS.WebUI/CLAUDE.md`

**给 4号位用。**

对应前端开发。

主要约束：

- 只调用 API
- 不直接访问数据库
- 不实现排程逻辑
- 使用新的 `/api/scheduling/runs`
- 页面要区分 `ACTIVE / CANDIDATE`
- 阶段一不做仿真业务页面

---

### 6）`src/APS.BusinessRules/CLAUDE.md`

**给 5号位用。**

对应业务规则引擎。

主要约束：

- 只写业务规则和凭证
- 不直接落库
- 不写框架代码
- 不修改 1号位算法和 2号位持久化逻辑
- 可以辅助判断 `ReasonCode`
- 不自行发明 `StageCode`

---

## 三、数据库和文档目录用的文件

### 7）`database/CLAUDE.md`

**给 2号位、数据库脚本维护人员、以后让 Claude Code 改 SQL 时使用。**

对应：

<pre class="overflow-visible! px-0!" data-start="1474" data-end="1495"><div class="relative w-full mt-4 mb-1"><div class=""><div class="relative"><div class="h-full min-h-0 min-w-0"><div class="h-full min-h-0 min-w-0"><div class="border border-token-border-light border-radius-3xl corner-superellipse/1.1 rounded-3xl"><div class="h-full w-full border-radius-3xl bg-token-bg-elevated-secondary corner-superellipse/1.1 overflow-clip rounded-3xl lxnfua_clipPathFallback"><div class="pointer-events-none absolute end-1.5 top-1 z-2 md:end-2 md:top-1"></div><div class="relative"><div class="pe-11 pt-3"><div class="relative z-0 flex max-w-full"><div id="code-block-viewer" dir="ltr" class="q9tKkq_viewer cm-editor z-10 light:cm-light dark:cm-light flex h-full w-full flex-col items-stretch ͼd ͼr"><div class="cm-scroller"><pre class="cm-content q9tKkq_readonly m-0"><code><span>database/</span></code></pre></div></div></div></div></div></div></div></div></div><div class=""><div class=""></div></div></div></div></div></pre>

主要约束：

- DDL 修改必须有 up 脚本
- 必须有 rollback 脚本
- 必须有执行前检查 SQL
- 必须有执行后验证 SQL
- 禁止直接改已执行历史脚本
- 禁止 DDL 与字段说明、POCO、API DTO 不一致
- 明确 Batch 3 DDL 执行顺序

---

### 8）`docs/CLAUDE.md`

**给文档维护人员、以后让 Claude Code 修改设计文档时使用。**

对应：

<pre class="overflow-visible! px-0!" data-start="1714" data-end="1731"><div class="relative w-full mt-4 mb-1"><div class=""><div class="relative"><div class="h-full min-h-0 min-w-0"><div class="h-full min-h-0 min-w-0"><div class="border border-token-border-light border-radius-3xl corner-superellipse/1.1 rounded-3xl"><div class="h-full w-full border-radius-3xl bg-token-bg-elevated-secondary corner-superellipse/1.1 overflow-clip rounded-3xl lxnfua_clipPathFallback"><div class="pointer-events-none absolute end-1.5 top-1 z-2 md:end-2 md:top-1"></div><div class="relative"><div class="pe-11 pt-3"><div class="relative z-0 flex max-w-full"><div id="code-block-viewer" dir="ltr" class="q9tKkq_viewer cm-editor z-10 light:cm-light dark:cm-light flex h-full w-full flex-col items-stretch ͼd ͼr"><div class="cm-scroller"><pre class="cm-content q9tKkq_readonly m-0"><code><span>docs/</span></code></pre></div></div></div></div></div></div></div></div></div><div class=""><div class=""></div></div></div></div></div></pre>

主要约束：

- 不推翻已收敛主链
- 不混淆历史说明和当前口径
- 不保留“当前以旧版本为准”的残留句子
- 不把 `ScheduleRun` 写成 `PlanVersion`
- 不把阶段二骨架表写成阶段一必须实装
- 文档修改后必须检查版本号、引用关系、旧口径残留

---

## 四、说明文件

### 9）`README_使用说明.md`

**给人看的，不是 Claude Code 的核心规则。**

作用是告诉团队：

- 这些 `CLAUDE.md` 应该放在哪里
- 怎么第一次让 Claude Code 检查仓库
- 如何使用根目录规则和局部规则

建议也放在仓库根目录，方便团队查看。

---

### 10）`CLAUDE_迁移说明.md`

**给你和团队负责人看的。**

作用是说明：

- 哪些内容是从原 Windsurf 规则里提炼过来的
- 哪些 Windsurf 专属内容没有迁移
- 以后以 `CLAUDE.md` 为主，不再以 `.windsurf` 为主

这个文件不是 Claude Code 必须执行的规则，可以放在根目录或 `docs/` 目录中。

---

## 五、实际放置结构

建议解压后形成这个结构：

<pre class="overflow-visible! px-0!" data-start="2269" data-end="2919"><div class="relative w-full mt-4 mb-1"><div class=""><div class="relative"><div class="h-full min-h-0 min-w-0"><div class="h-full min-h-0 min-w-0"><div class="border border-token-border-light border-radius-3xl corner-superellipse/1.1 rounded-3xl"><div class="h-full w-full border-radius-3xl bg-token-bg-elevated-secondary corner-superellipse/1.1 overflow-clip rounded-3xl lxnfua_clipPathFallback"><div class="pointer-events-none absolute end-1.5 top-1 z-2 md:end-2 md:top-1"></div><div class="relative"><div class="pe-11 pt-3"><div class="relative z-0 flex max-w-full"><div id="code-block-viewer" dir="ltr" class="q9tKkq_viewer cm-editor z-10 light:cm-light dark:cm-light flex h-full w-full flex-col items-stretch ͼd ͼr"><div class="cm-scroller"><pre class="cm-content q9tKkq_readonly m-0"><code><span>APS-Code/</span><br><span>├── CLAUDE.md                         ← 根目录总规则，全员共用</span><br><span>├── README_使用说明.md                 ← 给人看的使用说明</span><br><span>├── CLAUDE_迁移说明.md                 ← 从 Windsurf 迁移到 Claude Code 的说明</span><br><span>├── src/</span><br><span>│   ├── APS.Core/</span><br><span>│   │   └── CLAUDE.md                 ← 1号位</span><br><span>│   ├── APS.Engine/</span><br><span>│   │   └── CLAUDE.md                 ← 2号位</span><br><span>│   ├── APS.Orchestrator/</span><br><span>│   │   └── CLAUDE.md                 ← 3号位</span><br><span>│   ├── APS.WebUI/</span><br><span>│   │   └── CLAUDE.md                 ← 4号位</span><br><span>│   └── APS.BusinessRules/</span><br><span>│       └── CLAUDE.md                 ← 5号位</span><br><span>├── database/</span><br><span>│   └── CLAUDE.md                     ← 数据库 / DDL / SP</span><br><span>└── docs/</span><br><span>    └── CLAUDE.md                     ← 文档维护</span></code></pre></div></div></div></div></div></div></div></div></div><div class=""><div class=""></div></div></div></div></div></pre>

---

## 六、团队怎么用

### 全体成员

都必须受根目录：

<pre class="overflow-visible! px-0!" data-start="2958" data-end="2988"><div class="relative w-full mt-4 mb-1"><div class=""><div class="relative"><div class="h-full min-h-0 min-w-0"><div class="h-full min-h-0 min-w-0"><div class="border border-token-border-light border-radius-3xl corner-superellipse/1.1 rounded-3xl"><div class="h-full w-full border-radius-3xl bg-token-bg-elevated-secondary corner-superellipse/1.1 overflow-clip rounded-3xl lxnfua_clipPathFallback"><div class="pointer-events-none absolute end-1.5 top-1 z-2 md:end-2 md:top-1"></div><div class="relative"><div class="pe-11 pt-3"><div class="relative z-0 flex max-w-full"><div id="code-block-viewer" dir="ltr" class="q9tKkq_viewer cm-editor z-10 light:cm-light dark:cm-light flex h-full w-full flex-col items-stretch ͼd ͼr"><div class="cm-scroller"><pre class="cm-content q9tKkq_readonly m-0"><code><span>APS-Code/CLAUDE.md</span></code></pre></div></div></div></div></div></div></div></div></div><div class=""><div class=""></div></div></div></div></div></pre>

约束。

### 各号位

进入自己负责的目录后，Claude Code 会同时受到：

<pre class="overflow-visible! px-0!" data-start="3035" data-end="3077"><div class="relative w-full mt-4 mb-1"><div class=""><div class="relative"><div class="h-full min-h-0 min-w-0"><div class="h-full min-h-0 min-w-0"><div class="border border-token-border-light border-radius-3xl corner-superellipse/1.1 rounded-3xl"><div class="h-full w-full border-radius-3xl bg-token-bg-elevated-secondary corner-superellipse/1.1 overflow-clip rounded-3xl lxnfua_clipPathFallback"><div class="pointer-events-none absolute end-1.5 top-1 z-2 md:end-2 md:top-1"></div><div class="relative"><div class="pe-11 pt-3"><div class="relative z-0 flex max-w-full"><div id="code-block-viewer" dir="ltr" class="q9tKkq_viewer cm-editor z-10 light:cm-light dark:cm-light flex h-full w-full flex-col items-stretch ͼd ͼr"><div class="cm-scroller"><pre class="cm-content q9tKkq_readonly m-0"><code><span>根目录 CLAUDE.md</span><br><span>+</span><br><span>当前目录 CLAUDE.md</span></code></pre></div></div></div></div></div></div></div></div></div><div class=""><div class=""></div></div></div></div></div></pre>

约束。

比如 1号位在 `src/APS.Core/` 下开发时，Claude Code 应同时遵守：

<pre class="overflow-visible! px-0!" data-start="3133" data-end="3186"><div class="relative w-full mt-4 mb-1"><div class=""><div class="relative"><div class="h-full min-h-0 min-w-0"><div class="h-full min-h-0 min-w-0"><div class="border border-token-border-light border-radius-3xl corner-superellipse/1.1 rounded-3xl"><div class="h-full w-full border-radius-3xl bg-token-bg-elevated-secondary corner-superellipse/1.1 overflow-clip rounded-3xl lxnfua_clipPathFallback"><div class="pointer-events-none absolute end-1.5 top-1 z-2 md:end-2 md:top-1"></div><div class="relative"><div class="pe-11 pt-3"><div class="relative z-0 flex max-w-full"><div id="code-block-viewer" dir="ltr" class="q9tKkq_viewer cm-editor z-10 light:cm-light dark:cm-light flex h-full w-full flex-col items-stretch ͼd ͼr"><div class="cm-scroller"><pre class="cm-content q9tKkq_readonly m-0"><code><span>APS-Code/CLAUDE.md</span><br><span>src/APS.Core/CLAUDE.md</span></code></pre></div></div></div></div></div></div></div></div></div><div class=""><div class=""></div></div></div></div></div></pre>

### 数据库修改

让 Claude Code 在：

<pre class="overflow-visible! px-0!" data-start="3217" data-end="3238"><div class="relative w-full mt-4 mb-1"><div class=""><div class="relative"><div class="h-full min-h-0 min-w-0"><div class="h-full min-h-0 min-w-0"><div class="border border-token-border-light border-radius-3xl corner-superellipse/1.1 rounded-3xl"><div class="h-full w-full border-radius-3xl bg-token-bg-elevated-secondary corner-superellipse/1.1 overflow-clip rounded-3xl lxnfua_clipPathFallback"><div class="pointer-events-none absolute end-1.5 top-1 z-2 md:end-2 md:top-1"></div><div class="relative"><div class="pe-11 pt-3"><div class="relative z-0 flex max-w-full"><div id="code-block-viewer" dir="ltr" class="q9tKkq_viewer cm-editor z-10 light:cm-light dark:cm-light flex h-full w-full flex-col items-stretch ͼd ͼr"><div class="cm-scroller"><pre class="cm-content q9tKkq_readonly m-0"><code><span>database/</span></code></pre></div></div></div></div></div></div></div></div></div><div class=""><div class=""></div></div></div></div></div></pre>

目录下工作，读取：

<pre class="overflow-visible! px-0!" data-start="3251" data-end="3281"><div class="relative w-full mt-4 mb-1"><div class=""><div class="relative"><div class="h-full min-h-0 min-w-0"><div class="h-full min-h-0 min-w-0"><div class="border border-token-border-light border-radius-3xl corner-superellipse/1.1 rounded-3xl"><div class="h-full w-full border-radius-3xl bg-token-bg-elevated-secondary corner-superellipse/1.1 overflow-clip rounded-3xl lxnfua_clipPathFallback"><div class="pointer-events-none absolute end-1.5 top-1 z-2 md:end-2 md:top-1"></div><div class="relative"><div class="pe-11 pt-3"><div class="relative z-0 flex max-w-full"><div id="code-block-viewer" dir="ltr" class="q9tKkq_viewer cm-editor z-10 light:cm-light dark:cm-light flex h-full w-full flex-col items-stretch ͼd ͼr"><div class="cm-scroller"><pre class="cm-content q9tKkq_readonly m-0"><code><span>database/CLAUDE.md</span></code></pre></div></div></div></div></div></div></div></div></div><div class=""><div class=""></div></div></div></div></div></pre>

### 文档修改

让 Claude Code 在：

<pre class="overflow-visible! px-0!" data-start="3311" data-end="3328"><div class="relative w-full mt-4 mb-1"><div class=""><div class="relative"><div class="h-full min-h-0 min-w-0"><div class="h-full min-h-0 min-w-0"><div class="border border-token-border-light border-radius-3xl corner-superellipse/1.1 rounded-3xl"><div class="h-full w-full border-radius-3xl bg-token-bg-elevated-secondary corner-superellipse/1.1 overflow-clip rounded-3xl lxnfua_clipPathFallback"><div class="pointer-events-none absolute end-1.5 top-1 z-2 md:end-2 md:top-1"></div><div class="relative"><div class="pe-11 pt-3"><div class="relative z-0 flex max-w-full"><div id="code-block-viewer" dir="ltr" class="q9tKkq_viewer cm-editor z-10 light:cm-light dark:cm-light flex h-full w-full flex-col items-stretch ͼd ͼr"><div class="cm-scroller"><pre class="cm-content q9tKkq_readonly m-0"><code><span>docs/</span></code></pre></div></div></div></div></div></div></div></div></div><div class=""><div class=""></div></div></div></div></div></pre>

目录下工作，读取：

<pre class="overflow-visible! px-0!" data-start="3341" data-end="3367"><div class="relative w-full mt-4 mb-1"><div class=""><div class="relative"><div class="h-full min-h-0 min-w-0"><div class="h-full min-h-0 min-w-0"><div class="border border-token-border-light border-radius-3xl corner-superellipse/1.1 rounded-3xl"><div class="h-full w-full border-radius-3xl bg-token-bg-elevated-secondary corner-superellipse/1.1 overflow-clip rounded-3xl lxnfua_clipPathFallback"><div class="pointer-events-none absolute end-1.5 top-1 z-2 md:end-2 md:top-1"></div><div class="relative"><div class="pe-11 pt-3"><div class="relative z-0 flex max-w-full"><div id="code-block-viewer" dir="ltr" class="q9tKkq_viewer cm-editor z-10 light:cm-light dark:cm-light flex h-full w-full flex-col items-stretch ͼd ͼr"><div class="cm-scroller"><pre class="cm-content q9tKkq_readonly m-0"><code><span>docs/CLAUDE.md</span></code></pre></div></div></div></div></div></div></div></div></div><div class=""><div class=""></div></div></div></div></div></pre>

---

## 七、最简单的执行方式

你可以让团队这样做：

1. 把压缩包解压到 APS 代码仓库根目录。
2. 保持目录结构不变。
3. 先让 Claude Code 做一次检查，不要直接改代码：

<pre class="overflow-visible! px-0!" data-start="3472" data-end="3559"><div class="relative w-full mt-4 mb-1"><div class=""><div class="relative"><div class="h-full min-h-0 min-w-0"><div class="h-full min-h-0 min-w-0"><div class="border border-token-border-light border-radius-3xl corner-superellipse/1.1 rounded-3xl"><div class="h-full w-full border-radius-3xl bg-token-bg-elevated-secondary corner-superellipse/1.1 overflow-clip rounded-3xl lxnfua_clipPathFallback"><div class="pointer-events-none absolute end-1.5 top-1 z-2 md:end-2 md:top-1"></div><div class="relative"><div class="pe-11 pt-3"><div class="relative z-0 flex max-w-full"><div id="code-block-viewer" dir="ltr" class="q9tKkq_viewer cm-editor z-10 light:cm-light dark:cm-light flex h-full w-full flex-col items-stretch ͼd ͼr"><div class="cm-scroller"><pre class="cm-content q9tKkq_readonly m-0"><code><span>请读取当前仓库根目录和子目录中的 CLAUDE.md，检查当前仓库结构、文档引用、代码目录是否符合规则。先只输出检查结果和风险清单，不要修改任何文件。</span></code></pre></div></div></div></div></div></div></div></div></div><div class=""><div class=""></div></div></div></div></div></pre>

一句话总结：

> **根目录 `CLAUDE.md` 是全项目总规则；各 `src/xxx/CLAUDE.md` 是各号位局部规则；`database/CLAUDE.md` 管 SQL；`docs/CLAUDE.md` 管文档；README 和迁移说明是给人看的。**