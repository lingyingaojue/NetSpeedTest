using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NetSpeedTest.Models;
using NetSpeedTest.Services;

namespace NetSpeedTest.ViewModels;

/// <summary>
/// 测速配置管理 ViewModel
/// </summary>
public partial class ProfileViewModel : ObservableObject
{
    private readonly ProfileService _profileService;

    [ObservableProperty]
    private ObservableCollection<SpeedTestProfile> _profiles = new();

    [ObservableProperty]
    private SpeedTestProfile? _selectedProfile;

    [ObservableProperty]
    private string _newDownloadUrl = "";

    [ObservableProperty]
    private string _newUploadUrl = "";

    public ProfileViewModel(ProfileService profileService)
    {
        _profileService = profileService;
        LoadProfiles();
    }

    public void LoadProfiles()
    {
        var list = _profileService.GetAllProfiles();
        Profiles = new ObservableCollection<SpeedTestProfile>(list);

        // 保持之前的选中项
        if (SelectedProfile != null)
            SelectedProfile = Profiles.FirstOrDefault(p => p.Id == SelectedProfile.Id);

        SelectedProfile ??= Profiles.FirstOrDefault();
    }

    [RelayCommand]
    private void CreateProfile()
    {
        var profile = new SpeedTestProfile
        {
            Name = $"新建配置 {Profiles.Count + 1}"
        };
        try { _profileService.SaveProfile(profile); }
        catch (Exception ex) { System.Windows.MessageBox.Show($"创建失败: {ex.Message}", "NetSpeedTest"); return; }
        LoadProfiles();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == profile.Id);
    }

    [RelayCommand]
    private void DeleteProfile(SpeedTestProfile? profile)
    {
        if (profile == null) return;
        var result = MessageBox.Show(
            $"确定要删除配置 \"{profile.Name}\" 吗？",
            "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        try { _profileService.DeleteProfile(profile.Id); } catch (Exception ex) { System.Windows.MessageBox.Show($"删除失败: {ex.Message}", "NetSpeedTest"); return; }
        LoadProfiles();
    }

    [RelayCommand]
    private void SaveProfileName(SpeedTestProfile? profile)
    {
        if (profile == null) return;
        try { _profileService.SaveProfile(profile); } catch (Exception ex) { System.Windows.MessageBox.Show($"保存失败: {ex.Message}", "NetSpeedTest"); }
    }

    [RelayCommand]
    private void AddDownloadUrl(SpeedTestProfile? profile)
    {
        if (profile == null || string.IsNullOrWhiteSpace(NewDownloadUrl)) return;
        var url = NewDownloadUrl.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != "http" && uri.Scheme != "https")) return;
        profile.DownloadUrls.Add(url);
        try { _profileService.SaveProfile(profile); } catch (Exception ex) { System.Windows.MessageBox.Show($"保存失败: {ex.Message}", "NetSpeedTest"); }
        NewDownloadUrl = "";
        LoadProfiles();
    }

    [RelayCommand]
    private void RemoveDownloadUrl(string? url)
    {
        if (url == null || SelectedProfile == null) return;
        SelectedProfile.DownloadUrls.Remove(url);
        try { _profileService.SaveProfile(SelectedProfile); } catch (Exception ex) { System.Windows.MessageBox.Show($"保存失败: {ex.Message}", "NetSpeedTest"); }
        LoadProfiles();
    }

    [RelayCommand]
    private void AddUploadUrl(SpeedTestProfile? profile)
    {
        if (profile == null || string.IsNullOrWhiteSpace(NewUploadUrl)) return;
        var url = NewUploadUrl.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != "http" && uri.Scheme != "https")) return;
        profile.UploadUrls.Add(url);
        try { _profileService.SaveProfile(profile); } catch (Exception ex) { System.Windows.MessageBox.Show($"保存失败: {ex.Message}", "NetSpeedTest"); }
        NewUploadUrl = "";
        LoadProfiles();
    }

    [RelayCommand]
    private void RemoveUploadUrl(string? url)
    {
        if (url == null || SelectedProfile == null) return;
        SelectedProfile.UploadUrls.Remove(url);
        try { _profileService.SaveProfile(SelectedProfile); } catch (Exception ex) { System.Windows.MessageBox.Show($"保存失败: {ex.Message}", "NetSpeedTest"); }
        LoadProfiles();
    }

    // ========== 导入导出 ==========

    [RelayCommand]
    private void ImportConfig()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "测速配置文件 (*.bin;*.json)|*.bin;*.json|所有文件 (*.*)|*.*",
            Title = "导入测速配置"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var imported = _profileService.ImportFromFile(dialog.FileName);
                LoadProfiles();
                MessageBox.Show($"成功导入 {imported.Count} 个测速配置", "导入成功",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导入失败：{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private void ExportConfig()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "JSON 文件 (*.json)|*.json|BIN 文件 (*.bin)|*.bin",
            Title = "导出测速配置",
            FileName = $"speed_profiles_{DateTime.Now:yyyyMMdd_HHmmss}.json"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var profiles = _profileService.GetAllProfiles();
                _profileService.ExportToFile(profiles, dialog.FileName);
                MessageBox.Show($"成功导出 {profiles.Count} 个测速配置", "导出成功",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出失败：{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
