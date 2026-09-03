using HandyControl.Controls;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Documents;

namespace TensileNeW;

public partial class AnnotationDialog : UserControl
{
    public event EventHandler<AnnotationConfirmedEventArgs>? Confirmed;

    public AnnotationDialog(string annotationName, string annotationContent)
    {
        InitializeComponent();
        AnnotationNameTextBox.Text = annotationName;
        GetAnnotationContentRange().Text = annotationContent;
        Loaded += (_, _) =>
        {
            AnnotationNameTextBox.Focus();
            AnnotationNameTextBox.SelectAll();
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Confirmed?.Invoke(
            this,
            new AnnotationConfirmedEventArgs(
                AnnotationNameTextBox.Text.Trim(),
                GetAnnotationContentRange().Text.Trim()));
        CloseDialog();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CloseDialog();
    }

    private TextRange GetAnnotationContentRange()
    {
        return new TextRange(
            AnnotationContentTextBox.Document.ContentStart,
            AnnotationContentTextBox.Document.ContentEnd);
    }

    private void CloseDialog()
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

public sealed class AnnotationConfirmedEventArgs(string name, string content) : EventArgs
{
    public string Name { get; } = name;
    public string Content { get; } = content;
}
