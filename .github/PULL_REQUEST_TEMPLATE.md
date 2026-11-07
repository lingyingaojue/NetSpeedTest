## 变更说明

<!-- 一句话说明改了什么、为什么改；必要时补充背景、影响范围与实现取舍。 -->

## 关联 issue

<!-- 如 Fixes #123 / Closes #456；没有关联 issue 可写「无」。 -->

## 验证方式

- [ ] `dotnet test tests\NetSpeedTest.Tests\NetSpeedTest.Tests.csproj` 通过（通过 / 失败 / 跳过：__ / __ / __）
- [ ] `pwsh tools\docs\verify-docs.ps1` 0 失败
- [ ] 真机验证（测速 / Web 控制台 / 工具箱 / 防火墙等，附截图或日志）
- [ ] CI 全绿

<!-- 涉及 Web API、会话令牌、同网段 ACL、SSRF 防护、设置边界等安全相关改动时，请补充验证用例与结果。 -->

## 文档同步检查

- [ ] `README.md` 与 `README.en.md` 已同步（中英结构镜像）
- [ ] 官网 `docs/index.html` 已同步（参数 / API / 工具箱 / 版本）
- [ ] `CHANGELOG.md` 已更新（`Unreleased` 或对应版本小节）
- [ ] 参数范围 / 默认值、API 路由、工具箱数量与版本号无遗漏
- [ ] 安全机制变更已同步 `SECURITY.md`（如适用）

## 截图 / 录屏

<!-- 拖入图片或粘贴链接；UI 与官网相关改动必须附前后对比。 -->
