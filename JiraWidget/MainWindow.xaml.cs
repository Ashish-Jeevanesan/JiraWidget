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

        private const int LoginHeight = 240;
        private const int MainViewBaseHeight = 160;
        private const int HeightPerIssue = 58;
        private const int MaxMainHeight = 640;
        private const int MinWindowWidth = 340;
        private const int MaxWindowWidth = 520;

        public MainWindow()
        {
            this.InitializeComponent();
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(DragArea);

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            _appWindow.Title = "JiraWidget";

            _appWindow.Resize(new Windows.Graphics.SizeInt32(MinWindowWidth, LoginHeight));

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

            AppLogger.Info("Main window initialized.");
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
            if (string.IsNullOrWhiteSpace(JiraUrlTextBox.Text) || string.IsNullOrWhiteSpace(PatTextBox.Text))
            {
                _ = ShowErrorDialog("Please fill in all fields.");
                return;
            }

            try
            {
                var (isConfigured, setupError) = _jiraService.SetupClient(JiraUrlTextBox.Text, PatTextBox.Text);
                if (!isConfigured)
                {
                    await ShowErrorDialog($"Login failed during client setup. {setupError}");
                    return;
                }

                var (isConnected, errorMessage) = await _jiraService.ValidateConnectionAsync();
                if (!isConnected)
                {
                    await ShowErrorDialog($"Login failed. {errorMessage ?? "Please verify Jira URL and token."}");
                    return;
                }

                LoginView.Visibility = Visibility.Collapsed;
                MainView.Visibility = Visibility.Visible;
                AdjustWindowSize();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Unhandled exception in ConnectButton_Click.", ex);
                await ShowErrorDialog($"Unexpected error during login. Check log: {AppLogger.LogPath}");
            }
        }

        private void TrackButton_Click(object sender, RoutedEventArgs e)
        {
            var issueKey = IssueKeyTextBox.Text.Trim().ToUpper();

            if (!Regex.IsMatch(issueKey, @"^PC-\d+$"))
            {
                _ = ShowErrorDialog("Invalid format. Please use PC-XXXXX format.");
                return;
            }

            if (TrackedIssues.Any(i => i.IssueKey == issueKey))
            {
                _ = ShowErrorDialog("This issue is already being tracked.");
                return;
            }

            var newIssueViewModel = new TrackedIssueViewModel
            {
                IssueKey = issueKey,
                DisplayText = issueKey,
                StatusText = "Loading..."
            };

            TrackedIssues.Add(newIssueViewModel);
            AdjustWindowSize();

            _ = FetchIssueDetails(newIssueViewModel);

            IssueKeyTextBox.Text = "";
            AppLogger.Info($"Added issue '{issueKey}' to tracked list.");
        }

        private async Task FetchIssueDetails(TrackedIssueViewModel issueViewModel)
        {
            try
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
                    AdjustWindowSize();
                }
                else
                {
                    issueViewModel.StatusText = "Error";
                    issueViewModel.Progress = 0;
                    issueViewModel.DisplayText = $"{issueViewModel.IssueKey} ({errorMessage ?? "Not Found"})";
                    AdjustWindowSize();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Unhandled exception while loading '{issueViewModel.IssueKey}'.", ex);
                issueViewModel.StatusText = "Error";
                issueViewModel.DisplayText = $"{issueViewModel.IssueKey} (Exception - check log)";
                AdjustWindowSize();
            }
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is TrackedIssueViewModel issueToRemove)
            {
                TrackedIssues.Remove(issueToRemove);
                AdjustWindowSize();
                AppLogger.Info($"Removed issue '{issueToRemove.IssueKey}' from tracked list.");
            }
        }

        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            _jiraService.Disconnect();
            TrackedIssues.Clear();

            MainView.Visibility = Visibility.Collapsed;
            LoginView.Visibility = Visibility.Visible;
            _appWindow.Resize(new Windows.Graphics.SizeInt32(MinWindowWidth, LoginHeight));
            AppLogger.Info("Logged out and returned to login view.");
        }

        private void AdjustWindowSize()
        {
            var newHeight = MainViewBaseHeight + (TrackedIssues.Count * HeightPerIssue);
            if (newHeight > MaxMainHeight)
            {
                newHeight = MaxMainHeight;
            }

            var longestDisplayLength = TrackedIssues
                .Select(issue => issue.DisplayText?.Length ?? issue.IssueKey.Length)
                .DefaultIfEmpty(0)
                .Max();

            var contentWidth = MinWindowWidth + (longestDisplayLength * 3);
            var boundedWidth = Math.Clamp(contentWidth, MinWindowWidth, MaxWindowWidth);

            _appWindow.Resize(new Windows.Graphics.SizeInt32(boundedWidth, newHeight));
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
