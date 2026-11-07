# 第三方组件声明 · Third-Party Notices

本仓库的运行时第三方组件只有一项：官网顶栏使用的液态玻璃引擎 **hyalite**。
程序本体（WPF 应用）与内置 Web 控制台的依赖见 `src/NetSpeedTest/NetSpeedTest.csproj` 与 NuGet 包清单，不在此文件重复。

The only third-party runtime component in this repository is **hyalite**, the liquid-glass engine
used by the website navigation bar. Dependencies of the WPF app and the embedded web console are
declared in `src/NetSpeedTest/NetSpeedTest.csproj` and are not repeated here.

---

## hyalite

| 项 | 值 |
|:--|:--|
| 上游仓库 | <https://github.com/VII-Cae/hyalite--liquid-glass> |
| 版本 | v0.5.0 |
| 固定 commit | `b9f27192a7256bf5295bf027b582b86864f9b59d` |
| 文件 blob | `f0159b111a8d2132463480cdc8adb8f4d347603a`（`hyalite.js`，51,559 B） |
| 本仓库路径 | `docs/assets/vendor/hyalite.js` |
| 许可证全文 | [docs/assets/vendor/hyalite.LICENSE](docs/assets/vendor/hyalite.LICENSE) |
| 用途 | 官网 `#navGlass` 顶栏玻璃的折射滤镜（Chromium 生效，其余浏览器走 CSS 兜底） |
| 许可 | MIT © 2026 VII-Cae (VII) |

### 升级步骤

1. 按新的上游 commit 重新取回 `hyalite.js` 与 `LICENSE`，保持字节级一致（不要手改）；
2. 更新本文件的版本、commit、blob 与 `docs/assets/vendor/hyalite.LICENSE`；
3. 如 API 有变化，同步 `docs/assets/site.js` 的挂载参数与 `docs/design/DESIGN.md` §3.1；
4. 跑 `dotnet test tests\NetSpeedTest.Tests\NetSpeedTest.Tests.csproj --filter "FullyQualifiedName~SiteTemplateTests"` 与 `tools/docs/verify-docs.ps1`；
5. 用 headless Edge 复核置顶 / 滚动 / 浅色 / 移动端 / `?glass=off` 五种状态。

---

## MIT License（hyalite）

Copyright (c) 2026 VII-Cae (VII)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
