using System;
using System.IO;
using Xunit;

namespace NetSpeedTest.Tests;

/// <summary>
/// 仓库分类守卫：根目录只保留标准工程文件，源码、测试、官网、脚本各自归位。
/// 与 README「项目结构」及 docs/design/DESIGN.md 的仓库结构约定同步。
/// </summary>
public class RepositoryLayoutTests
{
    [Fact]
    public void Repository_is_split_into_src_tests_docs_tools()
    {
        var root = RepositoryRoot();

        foreach (var relative in new[]
                 {
                     "src/NetSpeedTest/NetSpeedTest.csproj",
                     "src/NetSpeedTest/Views/SettingsWindow.xaml",
                     "tests/NetSpeedTest.Tests/NetSpeedTest.Tests.csproj",
                     "docs/index.html",
                     "docs/404.html",
                     "docs/.nojekyll",
                     "docs/design/DESIGN.md",
                     "docs/qa/README.md",
                     "docs/assets/site.css",
                     "docs/assets/site.js",
                     "docs/assets/vendor/hyalite.js",
                     "tools/docs/verify-docs.ps1",
                     "tools/qa/smoke-test.ps1",
                     "tools/qa/smoke-debug.ps1",
                     "tools/screenshots/capture-screenshots.ps1",
                     "tools/verify/verify-version.ps1",
                     "tools/verify/verify-token.ps1",
                     "tools/verify/verify-extra.ps1",
                 })
        {
            Assert.True(File.Exists(Path.Combine(root, relative)), $"缺少分类后的文件：{relative}");
        }

        foreach (var leftover in new[]
                 {
                     "NetSpeedTest",
                     "NetSpeedTest.Tests",
                     "assets",
                     "index.html",
                     "404.html",
                     ".nojekyll",
                     "tools/verify-docs.ps1",
                     "tools/smoke-test.ps1",
                     "tools/verify-version.ps1",
                 })
        {
            var path = Path.Combine(root, leftover);
            Assert.False(File.Exists(path) || Directory.Exists(path), $"旧路径仍存在：{leftover}");
        }
    }

    [Fact]
    public void Solution_tests_and_ci_reference_the_new_paths()
    {
        var root = RepositoryRoot();

        var solution = File.ReadAllText(Path.Combine(root, "NetSpeedTest.sln"));
        Assert.Contains("src\\NetSpeedTest\\NetSpeedTest.csproj", solution, StringComparison.Ordinal);
        Assert.Contains("tests\\NetSpeedTest.Tests\\NetSpeedTest.Tests.csproj", solution, StringComparison.Ordinal);

        var testsProject = File.ReadAllText(Path.Combine(root, "tests", "NetSpeedTest.Tests", "NetSpeedTest.Tests.csproj"));
        Assert.Contains(@"..\..\src\NetSpeedTest\NetSpeedTest.csproj", testsProject, StringComparison.Ordinal);

        var ci = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));
        Assert.Contains("dotnet test tests\\NetSpeedTest.Tests\\NetSpeedTest.Tests.csproj", ci, StringComparison.Ordinal);
        Assert.Contains("tools/docs/verify-docs.ps1", ci, StringComparison.Ordinal);

        foreach (var relative in new[]
                 {
                     "tools/docs/verify-docs.ps1",
                     "tools/qa/smoke-test.ps1",
                     "tools/qa/smoke-debug.ps1",
                     "tools/verify/verify-token.ps1",
                     "tools/verify/verify-extra.ps1",
                     "tools/verify/verify-version.ps1",
                     "tools/screenshots/capture-screenshots.ps1",
                 })
        {
            var text = File.ReadAllText(Path.Combine(root, relative));
            Assert.DoesNotContain(@"D:\Program Files\DSH\NetSpeedTest", text, StringComparison.Ordinal);
        }
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "NetSpeedTest.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("NetSpeedTest.sln not found above " + AppContext.BaseDirectory);
    }
}
