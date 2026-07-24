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

    private bool CanDeleteRecord => SelectedRecord != null;

    partial void OnSelectedRecordChanged(SpeedTestResult? value)
    {
        DeleteRecordCommand.NotifyCanExecuteChanged();
    }

    // ==================== 构造函数 ====================

    public HistoryViewModel(DataService dataService)
    {
        _dataService = dataService;
        try { LoadPage(_currentPage); LoadStats(); }
        catch (Exception ex)
        {
            Logger.Log($"History load failed: {ex.Message}");
            Records = new ObservableCollection<SpeedTestResult>();
            TotalPages = 1; CanGoNext = false; CanGoPrevious = false;
        }
    }

    // ==================== 数据加载 ====================

    private void LoadStats()
    {
        try { Stats = _dataService.GetStatistics(); }
        catch { Stats = new SpeedTestStats(); }
    }

    private void LoadPage(int page)
    {
        var records = _dataService.GetRecords(page, PageSize);
        Records = new ObservableCollection<SpeedTestResult>(records);

        var totalCount = _dataService.GetRecordCount();
        PageNumber = page;
        TotalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        CanGoPrevious = page > 1;
        CanGoNext = page < TotalPages;
    }

    // ==================== 命令 ====================

    /// <summary>
    /// 上一页
    /// </summary>
    [RelayCommand]
    private void PreviousPage()
    {
        if (_currentPage > 1)
        {
            _currentPage--;
            try { LoadPage(_currentPage); }
            catch { _currentPage++; PageNumber = 1; TotalPages = 1; CanGoNext = false; CanGoPrevious = false; }
        }
    }

    /// <summary>
    /// 下一页
    /// </summary>
    [RelayCommand]
    private void NextPage()
    {
        if (_currentPage >= TotalPages) return;
        _currentPage++;
        try { LoadPage(_currentPage); }
        catch { _currentPage--; PageNumber = _currentPage; TotalPages = 1; CanGoNext = false; CanGoPrevious = false; }
    }

    /// <summary>
    /// 删除选中的记录
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDeleteRecord))]
    private void DeleteRecord()
    {
        if (SelectedRecord == null) return;

        var result = MessageBox.Show(
            "确定要删除这条测速记录吗？",
            "确认删除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        _dataService.DeleteRecord(SelectedRecord.Id);
        LoadPage(_currentPage);
        LoadStats();
    }

    [RelayCommand]
    private void ClearAllRecords()
    {
        var result = MessageBox.Show(
            "确定要清除所有历史记录吗？此操作不可撤销。",
            "确认清除",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        _dataService.ClearAllRecords();
        _currentPage = 1;
        try { LoadPage(_currentPage); LoadStats(); }
        catch { Records = new ObservableCollection<SpeedTestResult>(); TotalPages = 1; CanGoNext = false; CanGoPrevious = false; }
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

            var records = _dataService.GetAllRecords();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Time,Type,Profile,Threads,DownloadAvg(Mbps),UploadAvg(Mbps),LANLatency(ms),WANLatency(ms),TotalAvg(Mbps),TotalBytes,Duration(s),Adapter");
            foreach (var r in records)
            {
                sb.AppendLine($"{r.Timestamp:yyyy-MM-dd HH:mm:ss},{r.TestType},{EscapeCsv(r.NodeName)},{r.ThreadCount}," +
                    $"{FormatCsv(r.DownloadMbps)},{FormatCsv(r.UploadMbps)},{r.LatencyMs:F0}," +
                    $"{FormatCsv(r.WanLatencyMs)},{r.AverageTotalMbps:F1},{r.TotalBytes},{r.DurationSeconds:F1}," +
                    $"{EscapeCsv(r.NetworkAdapterName)}");
            }
            File.WriteAllText(dialog.FileName, sb.ToString(), System.Text.Encoding.UTF8);
            MessageBox.Show($"已导出 {records.Count} 条记录", "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show($"导出失败: {ex.Message}", "NetSpeedTest"); }
    }

    private static string EscapeCsv(string? s) => $"\"{(s ?? "").Replace("\"", "\"\"")}\"";
    private static string FormatCsv(double? v) => v.HasValue ? v.Value.ToString("F1", CultureInfo.InvariantCulture) : "";
}
