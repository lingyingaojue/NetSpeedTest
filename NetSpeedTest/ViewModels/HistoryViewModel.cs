using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NetSpeedTest.Models;
using NetSpeedTest.Helpers;
using NetSpeedTest.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;

namespace NetSpeedTest.ViewModels;

/// <summary>
/// 历史记录页 ViewModel
/// </summary>
public partial class HistoryViewModel : ObservableObject
{
    private readonly DataService _dataService;
    private const int PageSize = 20;
    private int _currentPage = 1;
    private int _totalCount;
    private bool _totalCountLoaded;
    private Task<int>? _totalCountTask;
    private int _statsVersion;

    // ==================== 可绑定属性 ====================

    /// <summary>
    /// 历史记录列表
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<SpeedTestResult> _records = new();

    /// <summary>
    /// 当前页码
    /// </summary>
    [ObservableProperty]
    private int _pageNumber = 1;

    /// <summary>
    /// 总页数
    /// </summary>
    [ObservableProperty]
    private int _totalPages = 1;

    /// <summary>
    /// 上一页是否可用
    /// </summary>
    [ObservableProperty]
    private bool _canGoPrevious;

    /// <summary>
    /// 下一页是否可用
    /// </summary>
    [ObservableProperty]
    private bool _canGoNext;

    /// <summary>
    /// 选中的记录
    /// </summary>
    [ObservableProperty]
    private SpeedTestResult? _selectedRecord;

    [ObservableProperty]
    private SpeedTestStats _stats = new();

    /// <summary>
    /// 记录列表是否正在加载
    /// </summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// 统计栏是否正在加载
    /// </summary>
    [ObservableProperty]
    private bool _isStatsLoading;

    private bool CanDeleteRecord => SelectedRecord != null;

    partial void OnSelectedRecordChanged(SpeedTestResult? value)
    {
        DeleteRecordCommand.NotifyCanExecuteChanged();
    }

    // ==================== 构造函数 ====================

    public HistoryViewModel(DataService dataService)
    {
        _dataService = dataService;
        _ = LoadInitialAsync();
    }

    // ==================== 数据加载 ====================

    private async Task LoadInitialAsync()
    {
        await LoadPageAsync(1, loadStatsAfter: true);
    }

    private async Task LoadPageAsync(int page, bool loadStatsAfter)
    {
        if (IsLoading) return;

        IsLoading = true;
        CanGoPrevious = false;
        CanGoNext = false;
        var statsAfter = false;

        try
        {
            var recordsTask = Task.Run(() => _dataService.GetRecords(page, PageSize));
            var countTask = EnsureTotalCountAsync();

            var records = await recordsTask;

            Records = new ObservableCollection<SpeedTestResult>(records);
            _currentPage = page;
            PageNumber = page;

            try
            {
                _totalCount = await countTask;
                _totalCountLoaded = true;
                UpdatePaging();
            }
            catch (Exception ex)
            {
                Logger.Log($"History count load failed: {ex.Message}");
            }

            statsAfter = loadStatsAfter;
        }
        catch (Exception ex)
        {
            Logger.Log($"History load failed: {ex.Message}");
            Records = new ObservableCollection<SpeedTestResult>();
            TotalPages = 1;
        }
        finally
        {
            IsLoading = false;
            CanGoPrevious = _currentPage > 1;
            CanGoNext = _currentPage < TotalPages;
        }

        if (statsAfter)
        {
            _ = LoadStatsAsync();
        }
    }

    private Task<int> EnsureTotalCountAsync()
    {
        if (_totalCountLoaded)
            return Task.FromResult(_totalCount);

        if (_totalCountTask == null || _totalCountTask.IsFaulted || _totalCountTask.IsCanceled)
            _totalCountTask = Task.Run(() => _dataService.GetRecordCount());

        return _totalCountTask;
    }

    private void UpdatePaging()
    {
        TotalPages = Math.Max(1, (int)Math.Ceiling(_totalCount / (double)PageSize));
        CanGoPrevious = !IsLoading && _currentPage > 1;
        CanGoNext = !IsLoading && _currentPage < TotalPages;
    }

    private async Task LoadStatsAsync()
    {
        var version = Interlocked.Increment(ref _statsVersion);
        IsStatsLoading = true;

        try
        {
            var stats = await Task.Run(() => _dataService.GetStatistics());
            if (version == Volatile.Read(ref _statsVersion))
                Stats = stats;
        }
        catch (Exception ex)
        {
            Logger.Log($"History stats load failed: {ex.Message}");
        }
        finally
        {
            if (version == Volatile.Read(ref _statsVersion))
                IsStatsLoading = false;
        }
    }

    // ==================== 命令 ====================

    /// <summary>
    /// 上一页
    /// </summary>
    [RelayCommand]
    private async Task PreviousPageAsync()
    {
        if (IsLoading || _currentPage <= 1) return;
        await LoadPageAsync(_currentPage - 1, loadStatsAfter: false);
    }

    /// <summary>
    /// 下一页
    /// </summary>
    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (IsLoading || _currentPage >= TotalPages) return;
        await LoadPageAsync(_currentPage + 1, loadStatsAfter: false);
    }

    /// <summary>
    /// 删除选中的记录
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDeleteRecord))]
    private async Task DeleteRecordAsync()
    {
        if (IsLoading || SelectedRecord == null) return;
        var id = SelectedRecord.Id;

        var result = MessageBox.Show(
            "确定要删除这条测速记录吗？",
            "确认删除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            await Task.Run(() => _dataService.DeleteRecord(id));

            Interlocked.Increment(ref _statsVersion);
            _totalCount = Math.Max(0, _totalCount - 1);
            _totalCountLoaded = true;

            if (Records.Count <= 1 && _currentPage > 1)
                _currentPage--;

            await LoadPageAsync(_currentPage, loadStatsAfter: true);
        }
        catch (Exception ex)
        {
            Logger.Log($"Delete record failed: {ex.Message}");
            MessageBox.Show($"删除失败: {ex.Message}", "NetSpeedTest", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task ClearAllRecordsAsync()
    {
        if (IsLoading) return;

        var result = MessageBox.Show(
            "确定要清除所有历史记录吗？此操作不可撤销。",
            "确认清除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            await Task.Run(() => _dataService.ClearAllRecords());

            Interlocked.Increment(ref _statsVersion);
            _totalCount = 0;
            _totalCountLoaded = true;
            _totalCountTask = null;

            await LoadPageAsync(1, loadStatsAfter: true);
        }
        catch (Exception ex)
        {
            Logger.Log($"Clear history failed: {ex.Message}");
            MessageBox.Show($"清除失败: {ex.Message}", "NetSpeedTest", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ExportCsv()
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = "CSV 文件 (*.csv)|*.csv",
                Title = "导出历史记录",
                FileName = $"speedtest_history_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };
            if (dialog.ShowDialog() != true) return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Time,Type,Profile,Threads,DownloadAvg(Mbps),UploadAvg(Mbps),LANLatency(ms),WANLatency(ms),PacketLoss(%),TotalAvg(Mbps),TotalBytes,Duration(s),Adapter,BatchId");
            int totalExported = 0;
            const int batchSize = 500;
            for (int page = 1; ; page++)
            {
                var records = _dataService.GetRecords(page, batchSize);
                if (records.Count == 0) break;
                foreach (var r in records)
                {
                    sb.AppendLine($"{r.Timestamp:yyyy-MM-dd HH:mm:ss},{r.TestType},{EscapeCsv(r.NodeName)},{r.ThreadCount}," +
                        $"{FormatCsv(r.DownloadMbps)},{FormatCsv(r.UploadMbps)},{FormatCsvLatency(r.LatencyMs)}," +
                        $"{FormatCsv(r.WanLatencyMs)},{r.PacketLoss.ToString("F1", CultureInfo.InvariantCulture)},{FormatCsv(r.AverageTotalMbps)},{r.TotalBytes},{r.DurationSeconds:F1}," +
                        $"{EscapeCsv(r.NetworkAdapterName)},{EscapeCsv(r.BatchId)}");
                    totalExported++;
                }
                if (records.Count < batchSize) break;
            }
            File.WriteAllText(dialog.FileName, sb.ToString(), System.Text.Encoding.UTF8);
            MessageBox.Show($"已导出 {totalExported} 条记录", "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show($"导出失败: {ex.Message}", "NetSpeedTest"); }
    }

    private static string EscapeCsv(string? s) => $"\"{(s ?? "").Replace("\"", "\"\"")}\"";
    private static string FormatCsv(double? v) => v.HasValue && !double.IsNaN(v.Value) ? v.Value.ToString("F1", CultureInfo.InvariantCulture) : "";
    private static string FormatCsvLatency(double v) => double.IsNaN(v) ? "" : v.ToString("F0", CultureInfo.InvariantCulture);
}