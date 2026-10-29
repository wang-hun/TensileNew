using HandyControl.Data;
using Microsoft.Win32;
using Haukcode.HighResolutionTimer;
using NLog;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Xps.Packaging;
using TensileNeW.Models;
using TensileNeW.Services;
using TensileNeW.Tools;
using Dialog = HandyControl.Controls.Dialog;
using Growl = HandyControl.Controls.Growl;
using MessageBox = HandyControl.Controls.MessageBox;

namespace TensileNeW;


public partial class MainWindow : Window
{
    private void LoadHelpDocument()
    {
        try
        {
            HelpNavigationTree.ItemsSource = ManualDocumentService.LoadManualNavigation();
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "加载说明书导航失败。");
            HelpNavigationTree.ItemsSource = null;
        }
    }

    private async void HelpNavigationTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        await NavigateHelpItemAsync(e.NewValue as HelpNavigationItem);
    }

    private async void HelpNavigationTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (HelpNavigationTree.SelectedItem is not HelpNavigationItem item)
        {
            return;
        }

        await NavigateHelpItemAsync(item);
        e.Handled = true;
    }

    private async Task NavigateHelpItemAsync(HelpNavigationItem? item)
    {
        try
        {
            if (item?.IsManualFile == true)
            {
                await OpenManualDocumentAsync(item);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "说明书导航失败，保留当前文档。");
            // Keep the current document unchanged if navigation fails.
        }
    }

    private async Task OpenManualDocumentAsync(HelpNavigationItem item)
    {
        if (item.IsUnavailable)
        {
            ShowWarning(item.UnavailableMessage ?? ManualDocumentService.MissingOfficeMessage);
            return;
        }

        if (string.IsNullOrWhiteSpace(item.FilePath) || !File.Exists(item.FilePath))
        {
            ShowWarning("说明书文件不存在。");
            return;
        }

        bool isPdf = ManualDocumentService.IsPdfManual(item.FilePath);
        if (!isPdf && !ManualDocumentService.CanConvertToXps(item.FilePath))
        {
            ShowWarning("当前说明书格式不支持预览。");
            return;
        }

        HelpDocumentViewer.Visibility = Visibility.Visible;
        if (isPdf)
        {
            try
            {
                CloseManualXpsDocument();
                _currentHelpItem = item;
                HelpDocumentViewer.Visibility = Visibility.Visible;
                OpenHelpSourceButton.Visibility = Visibility.Visible;
                HelpDocumentViewer.SetPdfDocument(item.FilePath);
                HelpDocumentViewer.SetZoomFactor(_helpZoomFactor);
                HelpSearchTextBox.Clear();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "打开 PDF 说明书预览失败。");
                HideHelpDocumentViewer();
                ShowWarning($"说明书预览失败：{ex.Message}");
            }

            return;
        }

        ManualDocumentConvertResult result = !string.IsNullOrWhiteSpace(item.CachedPath) && File.Exists(item.CachedPath)
            ? ManualDocumentConvertResult.Ok(item.CachedPath)
            : await Task.Run(() => ManualDocumentService.ConvertToXpsFile(item.FilePath));
        if (!result.Success || string.IsNullOrWhiteSpace(result.XpsPath))
        {
            HideHelpDocumentViewer();
            ShowWarning(result.Message ?? "说明书打开失败。");
            return;
        }

        try
        {
            item.CachedPath = result.XpsPath;
            CloseManualXpsDocument();
            _currentHelpItem = item;
            _manualXpsDocument = new XpsDocument(result.XpsPath, FileAccess.Read);
            HelpDocumentViewer.Visibility = Visibility.Visible;
            OpenHelpSourceButton.Visibility = Visibility.Visible;
            HelpDocumentViewer.SetDocument(_manualXpsDocument);
            HelpDocumentViewer.SetZoomFactor(_helpZoomFactor);
            HelpSearchTextBox.Clear();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "打开 XPS 说明书预览失败。");
            HideHelpDocumentViewer();
            ShowWarning($"说明书预览失败：{ex.Message}");
        }
    }

    private void HideHelpDocumentViewer()
    {
        CloseManualXpsDocument();
        HelpDocumentViewer.Visibility = Visibility.Collapsed;
        OpenHelpSourceButton.Visibility = Visibility.Collapsed;
        _currentHelpItem = null;
    }

    private void CloseManualXpsDocument()
    {
        HelpDocumentViewer.ClearDocument();
        _manualXpsDocument?.Close();
        _manualXpsDocument = null;
    }

    private void OpenCurrentManualFile_Click(object sender, RoutedEventArgs e)
    {
        string? filePath = _currentHelpItem?.FilePath;
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            ShowWarning("说明书文件不存在。");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            if (OpenContainingFolder(filePath))
            {
                ShowWarning("打开原文件失败，已打开所在文件夹。");
                return;
            }

            ShowWarning($"打开原文件失败：{ex.Message}");
        }
    }

    private static bool OpenContainingFolder(string filePath)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "打开说明书所在文件夹失败。");
            return false;
        }
    }

    private async void HelpSearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        await ExecuteHelpSearchAsync(true, false);
        e.Handled = true;
    }

    private async void HelpSearchButton_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteHelpSearchAsync(true, false);
    }

    private async void HelpSearchPrevious_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteHelpSearchAsync(false, true);
    }

    private async void HelpSearchNext_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteHelpSearchAsync(true, true);
    }

    private Task ExecuteHelpSearchAsync(bool forward, bool repeatLastSearch)
    {
        string keyword = HelpSearchTextBox.Text.Trim();
        if (!repeatLastSearch && string.IsNullOrWhiteSpace(keyword))
        {
            return Task.CompletedTask;
        }

        bool searchWholeDocument = (HelpSearchModeComboBox.SelectedItem as HelpSearchModeOption)?.SearchWholeDocument == true;
        if (HelpDocumentViewer.Visibility == Visibility.Visible)
        {
            bool found = repeatLastSearch
                ? HelpDocumentViewer.RepeatLastSearch(forward, out string? message)
                : HelpDocumentViewer.Search(keyword, searchWholeDocument, forward, out message);
            if (!found && !string.IsNullOrWhiteSpace(message))
            {
                ShowInfo(message);
            }
        }

        return Task.CompletedTask;
    }

    private void HelpZoomOut_Click(object sender, RoutedEventArgs e)
    {
        SetHelpZoom(_helpZoomFactor - HelpZoomStep);
    }

    private void HelpZoomIn_Click(object sender, RoutedEventArgs e)
    {
        SetHelpZoom(_helpZoomFactor + HelpZoomStep);
    }

    private void HelpZoomReset_Click(object sender, RoutedEventArgs e)
    {
        SetHelpZoom(1.0);
    }

    private void HelpZoomBox_LostFocus(object sender, RoutedEventArgs e)
    {
        ApplyHelpZoomBoxValue();
    }

    private void HelpZoomBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyHelpZoomBoxValue();
            HelpZoomBox.SelectAll();
            e.Handled = true;
        }
    }

    private void ApplyHelpZoomBoxValue()
    {
        string zoomText = HelpZoomBox.Text.Trim().TrimEnd('%');
        if (double.TryParse(zoomText, out double zoomPercent))
        {
            SetHelpZoom(zoomPercent / 100.0);
            return;
        }

        UpdateHelpZoomText();
    }

    private void SetHelpZoom(double zoomFactor)
    {
        _helpZoomFactor = Math.Clamp(zoomFactor, HelpMinZoom, HelpMaxZoom);
        UpdateHelpZoomText();

        if (HelpDocumentViewer.Visibility == Visibility.Visible)
        {
            HelpDocumentViewer.SetZoomFactor(_helpZoomFactor);
        }
    }

    private void UpdateHelpZoomText()
    {
        HelpZoomBox.Text = $"{_helpZoomFactor:P0}";
    }

}