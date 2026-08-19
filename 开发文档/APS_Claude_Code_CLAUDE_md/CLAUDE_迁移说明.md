# CLAUDE.md 迁移说明：从 Windsurf 规则到 Claude Code 规则

## 迁移结论

旧 `.windsurf.zip` 中有可借鉴内容，但不建议原样搬迁。

本次已经将仍然有效的内容提炼进新的 CLAUDE.md 文件组，包括：

1. 架构红线
2. 各号位职责边界
3. 权限、审计、审批、数据范围规则
4. 库存五层架构
5. MaterialCode 编码规则
6. ext_ 视图位置规则
7. 日常 SVN 开发流程
8. 代码审查规则
9. 测试与提交规范

## 不再迁移的内容

以下内容不再作为 Claude Code 的执行依据：

- `.windsurf/rules.md` 路径依赖
- `.windsurf/contracts/*.yml` 作为最高优先级契约
- `.windsurf/workflows/*.md` 作为工具工作流入口
- Windsurf 专属提示词
- 旧版本文档引用
- 与当前 Batch 3 口径冲突的内容

## 后续维护原则

以后团队只维护：

- 根目录 `CLAUDE.md`
- 各模块局部 `CLAUDE.md`
- 正式设计文档与 DDL / API / 契约

`.windsurf` 可以保留归档，但不应再作为 Claude Code 日常执行规则。
