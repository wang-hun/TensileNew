using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace TensileNeW.Controls;

public partial class ReportOptionsEditor : UserControl
{
    public ReportOptionsEditor()
    {
        InitializeComponent();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ReportOptionEditorViewModel editor)
        {
            return;
        }

        try
        {
            string directory = Path.GetDirectoryName(editor.FilePath)
                ?? throw new InvalidOperationException("报告选项配置路径无效。");
            Process.Start(new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            editor.ShowOperationError($"打开文件夹失败：{ex.Message}");
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ReportOptionEditorViewModel editor)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "导入报告备选项",
            Filter = "报告选项配置 (*.json)|*.json",
            InitialDirectory = Path.GetDirectoryName(editor.FilePath),
            CheckFileExists = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            editor.Import(dialog.FileName);
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ReportOptionEditorViewModel editor)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "导出报告备选项",
            Filter = "报告选项配置 (*.json)|*.json",
            DefaultExt = ".json",
            AddExtension = true,
            FileName = "ReportOptions-导出.json"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            editor.Export(dialog.FileName);
        }
    }
}
