using HandyControl.Controls;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TensileNeW;

public partial class TrialStartupNoticeDialog : UserControl
{
    public TrialStartupNoticeDialog(int startupCount)
    {
        Message = $"您已经启动ECS共 {startupCount} 次了，觉得好用就购买完整版？";
        InitializeComponent();
    }

    public string Message { get; }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DependencyObject? current = this;
        while (current != null)
        {
            if (current is Dialog dialog)
            {
                dialog.Close();
                return;
            }

            current = VisualTreeHelper.GetParent(current);
        }
    }
}
