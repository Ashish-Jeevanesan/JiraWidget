using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace JiraWidget
{
    public sealed partial class MainWindow : Window
    {
        private AppWindow _appWindow;
        private readonly JiraService _jiraService;

        public ObservableCollection<TrackedIssueViewModel> TrackedIssues { get; } = new();

        private const int MainViewBaseHeight = 140;
        private const int HeightPerIssue = 60;

        public MainWindow()
        {
            this.InitializeComponent();
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(DragArea);

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            _appWindow.Title = "JiraWidget";

            _appWindow.Resize(new Windows.Graphics.SizeInt32(300, 240)); 

            var presenter = (OverlappedPresenter)_appWindow.Presenter;
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.SetBorderAndTitleBar(false, false);

            _jiraService = new JiraService();
            TrackedIssuesItemsControl.ItemsSource = TrackedIssues;

            // Pre-populate for development
            JiraUrlTextBox.Text = "https://jira.globusmedical.com/";
            PatTextBox.Text = "";
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

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(JiraUrlTextBox.Text) || string.IsNullOrWhiteSpace(PatTextBox.Text))
            {
                _ = ShowErrorDialog("Please fill in all fields.");
                return;
            }

            _jiraService.SetupClient(JiraUrlTextBox.Text, PatTextBox.Text);

            // Switch to the main view
            LoginView.Visibility = Visibility.Collapsed;
            MainView.Visibility = Visibility.Visible;
            AdjustWindowHeight();
        }
        
        private void TrackButton_Click(object sender, RoutedEventArgs e)
        {
            var issueKey = IssueKeyTextBox.Text.Trim().ToUpper();

            // 1. Validation
            if (!Regex.IsMatch(issueKey, @"^PC-\d+$"))
            {
                _ = ShowErrorDialog("Invalid format. Please use PC-XXXXX format.");
                return;
            }

            // 2. Check for duplicates
            if (TrackedIssues.Any(i => i.IssueKey == issueKey))
            {
                _ = ShowErrorDialog("This issue is already being tracked.");
                return;
            }

            // 3. Add to collection
            var newIssueViewModel = new TrackedIssueViewModel { DisplayText = issueKey, StatusText = "Loading..." };
            TrackedIssues.Add(newIssueViewModel);
            AdjustWindowHeight();

            // 4. Fetch data asynchronously
            _ = FetchIssueDetails(newIssueViewModel);

            // 5. Clear input
            IssueKeyTextBox.Text = "";
        }
        
        private async Task FetchIssueDetails(TrackedIssueViewModel issueViewModel)
        {
            var (issue, errorMessage) = await _jiraService.GetIssueAsync(issueViewModel.IssueKey);

            if (issue != null)
            {
                var activityLinks = issue.Fields?.IssueLinks?
                    .Where(link => link.Type?.Name == "Activities" && link.OutwardIssue?.Fields?.Status != null)
                    .ToList();
        
                var total = activityLinks?.Count ?? 0;
                var done = activityLinks?.Count(link => link.OutwardIssue!.Fields!.Status!.Name == "Done") ?? 0;
                var percentage = (total == 0) ? 0 : (int)((double)done / total * 100);

                issueViewModel.Progress = percentage;
                issueViewModel.DisplayText = $"{issueViewModel.IssueKey} ({done}/{total} Done)";
                issueViewModel.StatusText = "Loaded";
            }
            else
            {
                issueViewModel.StatusText = "Error";
                issueViewModel.Progress = 0;
                issueViewModel.DisplayText = $"{issueViewModel.IssueKey} ({errorMessage ?? "Not Found"})";
            }
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is TrackedIssueViewModel issueToRemove)
            {
                TrackedIssues.Remove(issueToRemove);
                AdjustWindowHeight();
            }
        }

        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            _jiraService.Disconnect();
            TrackedIssues.Clear();
            
            MainView.Visibility = Visibility.Collapsed;
            LoginView.Visibility = Visibility.Visible;
            _appWindow.Resize(new Windows.Graphics.SizeInt32(300, 240));
        }

        private void AdjustWindowHeight()
        {
            var newHeight = MainViewBaseHeight + (TrackedIssues.Count * HeightPerIssue);
            _appWindow.Resize(new Windows.Graphics.SizeInt32(300, newHeight));
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
