using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetSpeedTest.Models;
using NetSpeedTest.Services;
using NetSpeedTest.ViewModels;
using NetSpeedTest.Views;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;

namespace NetSpeedTest;

/// <summary>
/// 应用程序入口，负责 DI 容器注册和初始化
/// </summary>
public partial class App : Application
{
    private readonly ServiceProvider _serviceProvider;

    public App()
    {
        if (Environment.GetCommandLineArgs().Contains("--debug"))
            Logger.Enabled = true;

        var services = new ServiceCollection();

        // 加载配置文件
        var localSettingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetSpeedTest");
        Directory.CreateDirectory(localSettingsDir);
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .SetBasePath(localSettingsDir)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        // 注册测速配置选项
        var speedOpts = new SpeedTestOptions();
        configuration.GetSection("SpeedTest").Bind(speedOpts);
        services.AddSingleton(speedOpts);

        // 注册 HttpClient（全局单例复用）
        services.AddSingleton<HttpClient>(sp =>
        {
            var handler = new System.Net.Http.SocketsHttpHandler
            {
                SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                {
                    EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13
                }
            };
            var client = new HttpClient(handler)
            {
                Timeout = System.Threading.Timeout.InfiniteTimeSpan
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NetSpeedTest/1.1.1");
            return client;
        });

        // 注册服务
        services.AddSingleton<DataService>();
        services.AddSingleton(sp =>
        {
            var dbDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetSpeedTest");
            Directory.CreateDirectory(dbDir);
            return new ProfileService($"Data Source={Path.Combine(dbDir, "NetSpeedTest.db")}");
        });
        services.AddTransient<SpeedTestService>();
        services.AddTransient<NetworkInfoService>();

        // 注册 ViewModel
        services.AddSingleton<MainViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<ProfileViewModel>();
        services.AddTransient<SettingsViewModel>();

        _serviceProvider = services.BuildServiceProvider();

        // 初始化 SQLite 数据库（自动建表）
        try
        {
            var dataService = _serviceProvider.GetRequiredService<DataService>();
            dataService.Initialize();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"数据库初始化失败: {ex.Message}", "启动错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            throw;
        }
    }

    /// <summary>
    /// 启动主窗口
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 检查 EULA 同意状态（注册表）
        bool eulaAccepted = false;
        try
        {
            using var eulaKey = Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey(@"Software\NetSpeedTest");
            eulaAccepted = eulaKey?.GetValue("EulaAccepted") != null;
        }
        catch { }

        if (!eulaAccepted)
        {
            var eula = new EulaWindow(isFirstLaunch: true);
            eula.ShowDialog();
            if (!eula.Agreed) { Shutdown(); return; }
            try
            {
                using var eulaKey = Microsoft.Win32.Registry.CurrentUser
                    .CreateSubKey(@"Software\NetSpeedTest");
                eulaKey?.SetValue("EulaAccepted", 1);
            }
        catch (Exception ex) { System.Windows.MessageBox.Show($"更新日志加载失败: {ex.Message}", "NetSpeedTest"); }
        }

        var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
        var mainWindow = new MainWindow
        {
            DataContext = mainViewModel
        };

        mainWindow.Show();

        // 首次运行当前版本时显示更新日志
        try
        {
            var v = Assembly.GetEntryAssembly()?.GetName()?.Version;
            var currentVersion = $"{v?.Major ?? 0}.{v?.Minor ?? 0}.{v?.Build ?? 0}";
            using var vk = Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey(@"Software\NetSpeedTest");
            var lastVersion = vk?.GetValue("LastVersion") as string ?? "0.0.0";
            if (currentVersion != lastVersion)
            {
                var about = new AboutWindow { Owner = mainWindow };
                about.ShowDialog();
                try
                {
                    using var wk = Microsoft.Win32.Registry.CurrentUser
                        .CreateSubKey(@"Software\NetSpeedTest");
                    wk?.SetValue("LastVersion", currentVersion);
                }
                catch { }
            }
        }
        catch (Exception ex) { System.Windows.MessageBox.Show($"更新日志加载失败: {ex.Message}", "NetSpeedTest"); }
    }

    public T GetService<T>() where T : notnull => _serviceProvider.GetRequiredService<T>();
}
