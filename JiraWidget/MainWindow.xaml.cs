using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace JiraWidget
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private AppWindow _appWindow;
        private readonly JiraService _jiraService;

        public MainWindow()
        {
            this.InitializeComponent();
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(DragArea);

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);

            _appWindow.Resize(new Windows.Graphics.SizeInt32(300, 280)); // Adjusted height for all fields
            _appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);

            var presenter = (OverlappedPresenter)_appWindow.Presenter;
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.SetBorderAndTitleBar(false, false);

            _jiraService = new JiraService();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_appWindow.Presenter.Kind == AppWindowPresenterKind.Overlapped)
            {
                ((OverlappedPresenter)_appWindow.Presenter).Minimize();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(JiraUrlTextBox.Text) ||
                string.IsNullOrWhiteSpace(EmailTextBox.Text) ||
                string.IsNullOrWhiteSpace(ApiTokenTextBox.Text) ||
                string.IsNullOrWhiteSpace(JiraIssueTextBox.Text))
            {
                await ShowErrorDialog("Please fill in all fields.");
                return;
            }

            ConnectButton.IsEnabled = false;
            ConnectButton.Content = "Connecting...";

            bool isConnected = await _jiraService.ConnectAsync(JiraUrlTextBox.Text, EmailTextBox.Text, ApiTokenTextBox.Text);

            if (isConnected)
            {
                var issueKey = JiraIssueTextBox.Text;
                var issue = await _jiraService.GetIssueAsync(issueKey);

                if (issue != null)
                {
                    // Success! Switch view and update UI
                    LoginView.Visibility = Visibility.Collapsed;
                    TicketView.Visibility = Visibility.Visible;
                    _appWindow.Resize(new Windows.Graphics.SizeInt32(300, 120));

                    TicketIdTextBlock.Text = issue.Key;
                    TicketProgressBar.Value = issue.Fields?.Progress?.Percent ?? 0;
                }
                else
                {
                    await ShowErrorDialog($"Could not find or access Jira issue '{issueKey}'. Please check the ID and your permissions.");
                    ConnectButton.IsEnabled = true;
                    ConnectButton.Content = "Connect";
                }
            }
            else
            {
                await ShowErrorDialog("Failed to connect to Jira. Please check your URL, email, and API token.");
                ConnectButton.IsEnabled = true;
                ConnectButton.Content = "Connect";
            }
        }

        private async Task ShowErrorDialog(string message)
        {
            var dialog = new ContentDialog
            {
                Title = "Error",
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot
            };
            await dialog.ShowAsync();
        }
    }
}
