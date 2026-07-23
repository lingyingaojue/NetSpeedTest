using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetSpeedTest.Models;
using NetSpeedTest.Services;
using System.Collections.ObjectModel;
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

    private bool CanDeleteRecord => SelectedRecord != null;

    partial void OnSelectedRecordChanged(SpeedTestResult? value)
    {
        DeleteRecordCommand.NotifyCanExecuteChanged();
    }

    // ==================== 构造函数 ====================

    public HistoryViewModel(DataService dataService)
    {
        _dataService = dataService;
        try { LoadPage(_currentPage); }
        catch (Exception ex)
        {
            Logger.Log($"History load failed: {ex.Message}");
            Records = new ObservableCollection<SpeedTestResult>();
            TotalPages = 1; CanGoNext = false; CanGoPrevious = false;
        }
    }

    // ==================== 数据加载 ====================

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
    }
}
