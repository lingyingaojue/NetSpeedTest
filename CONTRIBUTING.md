# 参与贡献 · Contributing

感谢你愿意参与 NetSpeedTest 的建设！本文说明开发环境、命令、文档同步规则与提交流程。英文摘要见文末 [English Summary](#english-summary)。

参与本项目即表示你同意遵守 [行为准则（CODE_OF_CONDUCT.md）](CODE_OF_CONDUCT.md)。安全漏洞请勿公开提交 issue，请按 [SECURITY.md](SECURITY.md) 的私密渠道报告。

## 行为准则

- 参与讨论、提交 issue 与 PR 时，遵守 [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)（Contributor Covenant 2.1 精简版）。
- 对事不对人：评审意见针对代码与行为，不做人身评价。
- 发现安全漏洞时不要公开披露，详见 [SECURITY.md](SECURITY.md)。

## 开发环境

| 项 | 要求 |
|:--|:--|
| 操作系统 | Windows 10 / 11（WPF 应用，仅 Windows 可构建运行） |
| SDK | [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)（8.0.x） |
| 其他 | Git；端到端脚本建议使用管理员权限的 PowerShell |

程序清单为 `requireAdministrator`，每次启动 `NetSpeedTest.exe` 都会弹出 UAC 授权框，这是预期行为（用于自动创建防火墙入站规则）。

## 构建与运行

```powershell
git clone https://github.com/lingyingaojue/NetSpeedTest.git
cd NetSpeedTest
dotnet restore
dotnet build
dotnet run --project src\NetSpeedTest\NetSpeedTest.csproj
```

需要调试日志（写入 `debug.log`）时：

```powershell
dotnet run --project src\NetSpeedTest\NetSpeedTest.csproj -- --debug
```

## 运行测试

```powershell
dotnet test tests\NetSpeedTest.Tests\NetSpeedTest.Tests.csproj
```

只跑某个测试类（例如版本一致性）：

```powershell
dotnet test tests\NetSpeedTest.Tests\NetSpeedTest.Tests.csproj --filter "FullyQualifiedName~VersionConsistencyTests"
```

CI 会在 `windows-latest` 上执行 restore / build / test 与文档自检，提交前请至少跑通与改动相关的测试。

## 发布单文件版

```powershell
dotnet publish src\NetSpeedTest\NetSpeedTest.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

产物为单个自包含 exe（约 170 MB），目标机器无需预装 .NET 运行时。

## 端到端验证脚本

`tools\qa` / `tools\verify` / `tools\docs` / `tools\screenshots` 下的脚本针对真实 Release 构建运行（需要管理员权限以放行 HTTP.sys 端口）：

| 脚本 | 用途 |
|:--|:--|
| `tools\qa\smoke-test.ps1` | 驱动真实程序与 Web API：令牌校验、安全响应头、输入校验、测速生命周期 |
| `tools\verify\verify-version.ps1` | 校验版本号贯穿 csproj → exe 文件属性 → Web 控制台 → 静态资源 |
| `tools\verify\verify-token.ps1` | 校验会话令牌在真实局域网请求下的强制生效 |
| `tools\verify\verify-extra.ps1` | 补充场景验证 |
| `tools\docs\verify-docs.ps1` | 统一文档自检：README、官网、社区规范与版本一致性（提交前必跑） |

## 文档同步规则

README（中/英）、官网 `docs/index.html`、`CHANGELOG.md` 与程序内文案共同构成对外事实；同一事实只在一处定义，其余引用。

改动下列内容时，必须在同一个 PR 内同步更新对应文档：

| 改动 | 必须同步 |
|:--|:--|
| 可调参数（范围 / 默认值） | `README.md`、`README.en.md`、官网参数表 |
| REST API 路由或语义 | `README.md`、`README.en.md`、官网接口小节 |
| 网络工具箱数量或名称 | `README.md`、`README.en.md`、官网工具箱 |
| 版本号 / 发版 | `CHANGELOG.md`、README 徽章与更新日志、官网版本胶囊、关于页 |
| 安全机制或安全默认值 | `SECURITY.md` 安全机制清单 |

发版与展示内容变更的完整流程见 [docs/design/DESIGN.md](docs/design/DESIGN.md) 第 11 节《发版检查清单》。

提交前必须执行文档自检：

```powershell
pwsh tools\docs\verify-docs.ps1
```

文档编码约定：UTF-8 无 BOM、Markdown 使用 CRLF、文件末尾保留单个换行；`.editorconfig` 与 `tools\docs\verify-docs.ps1` 会校验这些规则。

## 提交信息规范

格式：

```text
<type>(<scope>): <中文摘要>
```

- `<type>`：`feat` / `fix` / `docs` / `test` / `chore`
- `<scope>`：可选，表示影响范围，如 `web` / `toolbox` / `readme` / `ci`
- 摘要用中文，一句话说清「改了什么」，不超过 50 字，句末不加句号

示例：

```text
feat(toolbox): 工具箱新增 NAT 检测入口
fix(web): 修复非回环写操作漏校验会话令牌
docs(readme): 同步 README 中英参数表
test(api): 补充双向测速明细回归
chore(ci): 固定 .NET 8.0.x 构建环境
```

一个 commit 只包含一个逻辑改动，不要混入无关格式化或重命名。

## PR 流程

1. 从 `main` 切出分支，命名 `feat/<摘要>`、`fix/<摘要>`、`docs/<摘要>`。
2. 一个改动对应一个 commit；PR 与 commit 都要能独立理解与回滚。
3. 填写 [PR 模板](.github/PULL_REQUEST_TEMPLATE.md)：变更说明、关联 issue、验证方式、文档同步清单。
4. 附上测试证据：
   - `dotnet test tests\NetSpeedTest.Tests\NetSpeedTest.Tests.csproj` 通过（写明通过 / 失败 / 跳过数）；
   - `pwsh tools\docs\verify-docs.ps1` 0 失败；
   - 涉及测速、Web 控制台、防火墙等运行时行为时附真机验证截图或日志。
5. 确保 CI 全绿、至少一名维护者复核后合并；合并后删除分支。
6. 安全相关问题不要走公开 PR，按 [SECURITY.md](SECURITY.md) 私密报告。

## English Summary

- **Code of Conduct**: by participating you agree to [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md); report security issues privately via [SECURITY.md](SECURITY.md), never in a public issue.
- **Environment**: Windows 10/11 and the .NET 8 SDK (8.0.x); the app requires administrator rights and shows a UAC prompt at every start.
- **Build & run**: `dotnet restore` → `dotnet build` → `dotnet run --project src\NetSpeedTest\NetSpeedTest.csproj` (add `-- --debug` for `debug.log`).
- **Tests**: `dotnet test tests\NetSpeedTest.Tests\NetSpeedTest.Tests.csproj`; CI runs restore / build / test on `windows-latest` plus the docs check.
- **Publish**: `dotnet publish src\NetSpeedTest\NetSpeedTest.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`.
- **Docs sync**: changes to parameters, REST API, toolbox count or version must update `README.md`, `README.en.md`, the website (`docs/index.html`) and `CHANGELOG.md`, then run `pwsh tools\docs\verify-docs.ps1`.
- **Commits**: `<type>(<scope>): <Chinese summary>` with type in `feat|fix|docs|test|chore`; one logical change per commit.
- **Pull requests**: fill in the PR template, link the issue, attach test evidence, and make sure CI is green before merge.
