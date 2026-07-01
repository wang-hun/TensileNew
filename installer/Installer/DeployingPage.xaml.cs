using System.Windows.Controls;

namespace EcsInstaller;

public sealed partial class DeployingPage : UserControl
{
    public DeployingPage()
    {
        InitializeComponent();
    }

    public void SetProgress(string text)
    {
        ProgressTextBlock.Text = text;
    }
}
