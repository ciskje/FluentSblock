using System.Windows;

namespace FluentSblock;

public partial class EjectErrorDialog : Window
{
    public EjectErrorDialog(string title, string cause, string remedy)
    {
        InitializeComponent();
        Title = title;
        DlgTitle.Text = title;
        DlgCause.Text = cause;
        DlgRemedy.Text = remedy;
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => Close();
}
