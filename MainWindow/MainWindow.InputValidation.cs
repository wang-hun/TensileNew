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
    private void DecimalBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var tb = (System.Windows.Controls.TextBox)sender;
        if (TryNormalizeDecimalInput(tb.Text, out string normalized))
        {
            ClearInputValidationState(tb);
            tb.Text = normalized;
            tb.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
        }
        else
        {
            tb.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateTarget();
            ClearInputValidationState(tb);
        }
    }

    private void DecimalBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var tb = (System.Windows.Controls.TextBox)sender;
        SetInputValidationState(tb, TryNormalizeDecimalInput(tb.Text, out _), "请输入数字。");
    }

    private void ShutdownRatioBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var tb = (System.Windows.Controls.TextBox)sender;
        if (!TryNormalizeDecimalInput(tb.Text, out string normalized))
        {
            tb.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateTarget();
            ClearInputValidationState(tb);
            return;
        }

        if (!decimal.TryParse(normalized, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.CurrentCulture, out decimal value))
        {
            tb.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateTarget();
            ClearInputValidationState(tb);
            return;
        }

        if (value < 0m || value > 1m)
        {
            tb.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateTarget();
            ClearInputValidationState(tb);
            return;
        }

        ClearInputValidationState(tb);
        tb.Text = normalized;
        tb.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
    }

    private void ShutdownRatioBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var tb = (System.Windows.Controls.TextBox)sender;
        if (!TryNormalizeDecimalInput(tb.Text, out string normalized))
        {
            SetInputValidationState(tb, false, "请输入数字，范围为 0 到 1。");
            return;
        }

        if (!decimal.TryParse(normalized, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.CurrentCulture, out decimal value) || value < 0m || value > 1m)
        {
            SetInputValidationState(tb, false, "停机比例必须在 0 到 1 之间。");
            return;
        }

        ClearInputValidationState(tb);
    }

    private static void SetInputValidationState(System.Windows.Controls.TextBox tb, bool isValid, string message)
    {
        if (isValid)
        {
            ClearInputValidationState(tb);
            return;
        }

        tb.BorderBrush = System.Windows.Media.Brushes.Red;
        tb.BorderThickness = new Thickness(1.5);
        tb.ToolTip = message;
    }

    private static void ClearInputValidationState(System.Windows.Controls.TextBox tb)
    {
        tb.ClearValue(System.Windows.Controls.Control.BorderBrushProperty);
        tb.ClearValue(System.Windows.Controls.Control.BorderThicknessProperty);
        tb.ClearValue(FrameworkElement.ToolTipProperty);
    }

    private static bool TryNormalizeDecimalInput(string text, out string normalized)
    {
        string cultureSeparator = System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        normalized = text.Trim().Replace(".", cultureSeparator);
        return decimal.TryParse(normalized, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.CurrentCulture, out _);
    }

}