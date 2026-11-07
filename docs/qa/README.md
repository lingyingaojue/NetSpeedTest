# 历史 QA 证据归档

> 本目录归档 NetSpeedTest v1.4.3 的 QA 收口证据，仅作历史追溯，不参与构建、测试与发布流程。

## 来源与日期

| 项 | 内容 |
|:--|:--|
| 原始位置 | `qa-results/netspeedtest-coreqa/`（仓库根目录，迁移前） |
| 被测版本 | v1.4.3（报告标题记为 NetSpeedTest v1.4.3） |
| 首轮执行 | 2026-09-30 |
| 修复收口 | 2026-10-01 |
| 迁移日期 | 2026-11-07（仓库展示改版，`qa-results/netspeedtest-coreqa/` → `docs/qa/`） |
| 文件数量 | 归档证据 42 个（不含本索引 `README.md`）：其中 39 个入库，`config-backup/` 与 `deliver-real.log` 共 3 个运行时产物由 `.gitignore` 忽略 |
| 测试结论 | 9 个缺陷全部修复并回归通过；自动化测试 297 通过 / 0 失败 / 0 跳过；端到端冒烟 32/32 PASS；发布判断 conditional_go |

## 目录内容

| 文件 / 目录 | 类型 | 说明 |
|:--|:--|:--|
| [QA收口报告.md](QA收口报告.md) | Markdown | 测试报告正文：结论、环境、执行总览、缺陷定级与发布判断 |
| [测试用例与追踪矩阵.md](测试用例与追踪矩阵.md) | Markdown | 41 个业务用例的追踪矩阵与证据索引 |
| [缺陷修复方案.md](缺陷修复方案.md) | Markdown | 9 个缺陷的根因分析与逐项修法 |
| `plan-fragments/` | Markdown | 按测速核心 / 工具箱 / Web 服务拆分的修复计划片段（3 个） |
| `evidence/` | 文本 / 日志 | 9 个缺陷的取证快照，含 `smoke-debug.log` 原始调试日志 |
| `qa-final-297.trx`、`qa-final.trx`、`qa-full.trx` | TRX | `dotnet test` 运行结果（收口终版与中间版本） |
| `qa-run.json` | JSON | 全量测试运行明细导出 |
| `build-final.log`、`build-release.log` | 日志 | Debug / Release 构建输出 |
| `test-final.log`、`test-full.log` | 日志 | 单元测试运行摘要 |
| `smoke-test.log`、`smoke-batch1.log`、`smoke-final.log` | 日志 | 端到端冒烟脚本输出（首轮 / 批次 1 / 收口） |
| `real-speedtest.log`、`real-speedtest-batch1.log`、`real-speedtest.ps1` | 日志 / 脚本 | 真实联网测速记录与驱动脚本 |
| `realcurve-*.json`、`realhistory*.json`、`realsummary.json` | JSON | 真机测速曲线、历史快照与汇总 |
| `final-e2e.log`、`final-e2e.ps1` | 日志 / 脚本 | 修复后端到端断言（11/11 PASS） |
| `mtu-ttl1.txt`、`mtu-ttl30.txt` | 文本 | MTU 探测 TTL 修复前后的对照输出 |
| `config-backup/`、`deliver-real.log` | 运行时备份 | 联调前的本地配置备份与交付日志；由 `.gitignore` 忽略，不入库 |

## 用途与约定

- **历史追溯**：复核 v1.4.3 的测试结论、缺陷修复与发布判断时，从这里查原始证据。
- **不参与构建**：本目录不参与 `dotnet build` / `dotnet test` / CI 与文档自检，也不作为当前版本的质量依据；最新质量状态请看当前 Release 与 [CHANGELOG.md](../../CHANGELOG.md)。
- **路径说明**：归档文档与脚本中的 `qa-results\...` 路径是迁移前的历史位置，现已统一迁移到 `docs/qa/`；如需复跑脚本，请按当前目录调整路径。
- **只读归档**：请勿修改归档内的证据文件；新的 QA 证据应另建目录，不覆盖本归档。

## 相关链接

- [QA 收口报告](QA收口报告.md)
- [测试用例与追踪矩阵](测试用例与追踪矩阵.md)
- [缺陷修复方案](缺陷修复方案.md)
- [项目更新日志](../../CHANGELOG.md)
