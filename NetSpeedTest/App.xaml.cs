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
    private readonly IConfiguration _configuration;

    public App()
    {
        if (Environment.GetCommandLineArgs().Contains("--debug"))
            Logger.Enabled = true;

        var services = new ServiceCollection();

        // 加载配置文件（出厂默认 + 用户自定义层层覆盖）
        var localSettingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetSpeedTest");
        Directory.CreateDirectory(localSettingsDir);
        _configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json"), optional: true, reloadOnChange: false)
            .AddJsonFile(Path.Combine(localSettingsDir, "appsettings.json"), optional: true, reloadOnChange: false)
            .Build();
        var configuration = _configuration;
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
                Timeout = TimeSpan.FromSeconds(900)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NetSpeedTest/1.3.7");
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
        services.AddTransient<MoreViewModel>();

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
            Environment.Exit(1);
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
        catch (Exception ex) { Logger.Log($"EULA registry check failed: {ex.Message}"); }

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
        catch (Exception ex) { System.Windows.MessageBox.Show($"EULA 保存失败: {ex.Message}", "NetSpeedTest"); }
        }

        var mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();
        var mainWindow = new MainWindow
        {
            DataContext = mainViewModel
        };

        // 深色标题栏 + 圆角（无背景模糊）
        mainWindow.SourceInitialized += (_, _) => Helpers.WindowHelper.ApplyWindowChrome(mainWindow);

        mainWindow.Show();
        Application.Current.MainWindow = mainWindow;

        // 广告弹窗（设置页关闭广告 7 天内跳过；弹窗关闭不写抑制，抑制仅由设置页提供）
        try
        {
            if (Helpers.AdManager.ShouldShowAd())
            {
                var ad = new AdWindow(_configuration) { Owner = mainWindow };
                ad.Show();
            }
        }
        catch (Exception ex) { Logger.Log($"Ad window failed: {ex.Message}"); }

        // 启动自动检查更新（延迟 3s，避开广告窗；失败静默不打扰）
        var updConfig = _configuration;
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                await Task.Delay(3000);
                var (status, info) = await Helpers.UpdateChecker.CheckAsync(updConfig);
                if (status == Helpers.CheckStatus.HasUpdate && info != null
                    && !Application.Current.Windows.OfType<Views.UpdateWindow>().Any())
                {
                    var win = new Views.UpdateWindow(info.Version, info.Body, info.DownloadUrl)
                    {
                        Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w is Views.MainWindow)
                    };
                    win.Show();
                }
            }
            catch (Exception ex) { Logger.Log($"Auto update check failed: {ex.Message}"); }
        }), System.Windows.Threading.DispatcherPriority.Background);

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
                if (mainViewModel is ViewModels.MainViewModel mvm)
                    mvm.OpenAboutCommand.Execute(null);
                try
                {
                    using var wk = Microsoft.Win32.Registry.CurrentUser
                        .CreateSubKey(@"Software\NetSpeedTest");
                    wk?.SetValue("LastVersion", currentVersion);
                }
        catch (Exception ex) { Logger.Log($"Version registry write failed: {ex.Message}"); }
            }
        }
        catch (Exception ex) { System.Windows.MessageBox.Show($"版本检查失败: {ex.Message}", "NetSpeedTest"); }
    }

    public T GetService<T>() where T : notnull => _serviceProvider.GetRequiredService<T>();
}
