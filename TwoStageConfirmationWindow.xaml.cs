using System.Windows;
using System.Windows.Input;

namespace TensileNeW;

public partial class TwoStageConfirmationWindow : Window
{
    public TwoStageConfirmationWindow()
    {
        InitializeComponent();
    }

    public void ConfigurePrompt(string title, string question)
    {
        Title = title;
        PromptTextBlock.Text = question;
    }

    private void Yes_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void No_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}