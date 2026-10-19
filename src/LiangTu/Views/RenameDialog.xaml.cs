using System.Windows;

namespace LiangTu.Views;

public partial class RenameDialog : Window
{
    public string FileName => NameBox.Text.Trim();

    public RenameDialog(string initialStem)
    {
        InitializeComponent();
        NameBox.Text = initialStem;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void Ok_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text)) return;
        DialogResult = true;
        Close();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
