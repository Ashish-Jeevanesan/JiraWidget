using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
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
        private readonly AppWindow _appWindow;
        private readonly JiraService _jiraService;
        private Uri? _jiraBaseUri;
        private bool _oktaLoginInitialized;
        private bool _oktaLoginCompleted;

        public ObservableCollection<TrackedIssueViewModel> TrackedIssues { get; } = new();

        private const int LoginHeight = 240;
        private const int MainViewBaseHeight = 160;
        private const int HeightPerIssue = 58;
        private const int MaxMainHeight = 640;
        private const int DefaultMainIssueSlots = 3;
        private const int MinWindowWidth = 340;
        private const int MaxWindowWidth = 520;

        public MainWindow()
        {
            InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(DragArea);

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            _appWindow.Title = "JiraWidget";

            _appWindow.Resize(new Windows.Graphics.SizeInt32(MinWindowWidth, LoginHeight));

            var presenter = (OverlappedPresenter)_appWindow.Presenter;
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
            presenter.SetBorderAndTitleBar(false, false);

            _jiraService = new JiraService();
            TrackedIssuesItemsControl.ItemsSource = TrackedIssues;

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
            Close();
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(JiraUrlTextBox.Text) || string.IsNullOrWhiteSpace(PatTextBox.Text))
            {
                await ShowErrorDialog("Please fill in Jira URL and PAT.");
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

                await ValidateAndEnterMainViewAsync("Please verify Jira URL and token.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Unhandled exception in ConnectButton_Click.", ex);
                await ShowErrorDialog($"Unexpected error during login. Check log: {AppLogger.LogPath}");
            }
        }

        private async void OktaLoginButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(JiraUrlTextBox.Text))
            {
                await ShowErrorDialog("Please enter Jira URL before starting Okta login.");
                return;
            }

            try
            {
                if (!Uri.TryCreate(JiraUrlTextBox.Text.Trim(), UriKind.Absolute, out var baseUri))
                {
                    await ShowErrorDialog("Please enter a valid Jira URL (e.g., https://your-domain.atlassian.net/).");
                    return;
                }

                _jiraBaseUri = baseUri;
                await EnsureOktaWebViewAsync();
                OktaWebViewHost.Visibility = Visibility.Visible;

                // Start SSO flow in the embedded browser.
                OktaWebView.Source = _jiraBaseUri;
                AppLogger.Info($"Okta login initiated. BaseUrl='{_jiraBaseUri}'.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Unhandled exception in ConnectButton_Click.", ex);
                await ShowErrorDialog($"Unexpected error during login. Check log: {AppLogger.LogPath}");
            }
        }

        private void TrackButton_Click(object sender, RoutedEventArgs e)
        {
            var issueKey = IssueKeyTextBox.Text.Trim().ToUpperInvariant();

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
            _oktaLoginCompleted = false;
            OktaWebViewHost.Visibility = Visibility.Collapsed;
            OktaWebView.Source = null;

            MainView.Visibility = Visibility.Collapsed;
            LoginView.Visibility = Visibility.Visible;
            _appWindow.Resize(new Windows.Graphics.SizeInt32(MinWindowWidth, LoginHeight));
            AppLogger.Info("Logged out and returned to login view.");
        }

        private void AdjustWindowSize()
        {
            var visibleIssueSlots = Math.Max(TrackedIssues.Count, DefaultMainIssueSlots);
            var newHeight = MainViewBaseHeight + (visibleIssueSlots * HeightPerIssue);
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
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();
        }

        private async Task<bool> ValidateAndEnterMainViewAsync(string defaultErrorMessage)
        {
            var (isConnected, errorMessage) = await _jiraService.ValidateConnectionAsync();
            if (!isConnected)
            {
                await ShowErrorDialog($"Login failed. {errorMessage ?? defaultErrorMessage}");
                return false;
            }

            LoginView.Visibility = Visibility.Collapsed;
            MainView.Visibility = Visibility.Visible;
            AdjustWindowSize();
            return true;
        }

        private async Task EnsureOktaWebViewAsync()
        {
            if (_oktaLoginInitialized)
            {
                return;
            }

            await OktaWebView.EnsureCoreWebView2Async();
            OktaWebView.CoreWebView2.NavigationCompleted += OktaWebView_NavigationCompleted;
            _oktaLoginInitialized = true;
            AppLogger.Info("Okta WebView initialized.");
        }

        private async void OktaWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (_oktaLoginCompleted || _jiraBaseUri == null || OktaWebView.CoreWebView2 == null)
            {
                return;
            }

            var currentUrl = OktaWebView.Source?.ToString();
            if (string.IsNullOrWhiteSpace(currentUrl) || !Uri.TryCreate(currentUrl, UriKind.Absolute, out var currentUri))
            {
                AppLogger.Info("Okta navigation completed, but current URL is unavailable.");
                return;
            }

            if (!string.Equals(currentUri.Host, _jiraBaseUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                AppLogger.Info($"Okta navigation completed on non-Jira host '{currentUri.Host}'. Waiting for Jira host '{_jiraBaseUri.Host}'.");
                return;
            }

            if (currentUri.AbsolutePath.Contains("login", StringComparison.OrdinalIgnoreCase))
            {
                AppLogger.Info($"Okta navigation completed on Jira login path '{currentUri.AbsolutePath}'. Waiting for a non-login page.");
                return;
            }

            await TryCompleteOktaLoginAsync("NavigationCompleted");
        }

        private async void OktaContinueButton_Click(object sender, RoutedEventArgs e)
        {
            await TryCompleteOktaLoginAsync("ContinueButton");
        }

        private async Task TryCompleteOktaLoginAsync(string trigger)
        {
            if (_oktaLoginCompleted)
            {
                AppLogger.Info($"Okta login completion skipped (already completed). Trigger={trigger}");
                return;
            }

            if (_jiraBaseUri == null)
            {
                await ShowErrorDialog("Missing Jira URL. Please enter the Jira URL and try again.");
                return;
            }

            if (OktaWebView.CoreWebView2 == null)
            {
                await ShowErrorDialog("Okta browser is not ready yet. Please wait a moment and try again.");
                return;
            }

            try
            {
                var currentUrl = OktaWebView.Source?.ToString() ?? "<null>";
                AppLogger.Info($"Attempting Okta login completion. Trigger={trigger}, CurrentUrl='{currentUrl}'.");

                var cookies = await OktaWebView.CoreWebView2.CookieManager.GetCookiesAsync(_jiraBaseUri.ToString());
                AppLogger.Info($"Okta cookies retrieved. Count={cookies.Count}.");

                foreach (var cookie in cookies)
                {
                    AppLogger.Info($"Okta cookie found. Name='{cookie.Name}', Domain='{cookie.Domain}', Path='{cookie.Path}', Secure={cookie.IsSecure}, HttpOnly={cookie.IsHttpOnly}");
                }

                var sessionCookies = cookies
                    .Select(cookie => new JiraSessionCookie
                    {
                        Name = cookie.Name,
                        Value = cookie.Value,
                        Domain = cookie.Domain,
                        Path = cookie.Path,
                        IsSecure = cookie.IsSecure,
                        IsHttpOnly = cookie.IsHttpOnly
                    })
                    .ToList();

                var (isConfigured, setupError) = _jiraService.SetupClientWithCookies(_jiraBaseUri.ToString(), sessionCookies);
                if (!isConfigured)
                {
                    await ShowErrorDialog($"Login failed during client setup. {setupError}");
                    return;
                }

                AppLogger.Info("Okta login cookies captured; proceeding to Jira API validation.");
                var validated = await ValidateAndEnterMainViewAsync("Okta login was not accepted for Jira API access.");
                if (validated)
                {
                    _oktaLoginCompleted = true;
                    OktaWebViewHost.Visibility = Visibility.Collapsed;
                    AppLogger.Info("Okta login completed successfully.");
                }
                else
                {
                    AppLogger.Info("Okta login validation failed; keeping WebView open for retry.");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Unhandled exception while completing Okta login.", ex);
                await ShowErrorDialog($"Unexpected error during Okta login. Check log: {AppLogger.LogPath}");
            }
        }
    }
}
