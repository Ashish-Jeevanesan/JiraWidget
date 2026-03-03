using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace JiraWidget
{
    public sealed partial class MainWindow : Window
    {
        private readonly AppWindow _appWindow;
        private readonly IntPtr _hwnd;
        private readonly JiraService _jiraService;
        private readonly FavoritesStore _favoritesStore;
        private readonly HashSet<string> _favoriteIssueKeys = new(StringComparer.OrdinalIgnoreCase);
        private Uri? _jiraBaseUri;
        private bool _oktaLoginInitialized;
        private bool _oktaLoginCompleted;
        private bool _oktaLoginInProgress;

        public ObservableCollection<TrackedIssueViewModel> TrackedIssues { get; } = new();

        private const int LoginHeight = 240;
        private const int MainViewBaseHeight = 160;
        private const int HeightPerIssue = 84;
        private const int MaxMainHeight = 640;
        private const int DefaultMainIssueSlots = 3;
        private const int MinWindowWidth = 340;
        private const int MaxWindowWidth = 520;
        private const int MaxTitleLength = 70;
        private const int OktaCookieRetryAttempts = 3;
        private const int OktaCookieRetryDelayMs = 700;
        private const double MinAppOpacity = 0.35;
        private const double MaxAppOpacity = 1.0;
        private const double DefaultAppOpacity = 1.0;
        private const int GwlExStyle = -20;
        private const int WsExLayered = 0x00080000;
        private const uint LwaAlpha = 0x00000002;
        private static readonly string[] IncludedSubtaskSummaryTerms = { "PM Approval", "Post-Production Verification", "Post Production Verification" };

        public MainWindow()
        {
            InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(DragArea);

            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            _appWindow.Title = "JiraWidget";

            _appWindow.Resize(new Windows.Graphics.SizeInt32(MinWindowWidth, LoginHeight));

            var presenter = (OverlappedPresenter)_appWindow.Presenter;
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
            presenter.SetBorderAndTitleBar(false, false);

            _jiraService = new JiraService();
            _favoritesStore = new FavoritesStore();
            TrackedIssuesItemsControl.ItemsSource = TrackedIssues;

            JiraUrlTextBox.Text = "https://jira.globusmedical.com/";
            PatTextBox.Text = "";
            ApplyWindowOpacity(DefaultAppOpacity);

            AppLogger.Info("Main window initialized.");
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_appWindow.Presenter.Kind == AppWindowPresenterKind.Overlapped)
            {
                ((OverlappedPresenter)_appWindow.Presenter).Minimize();
            }
        }

        private void MenuButton_Click(object sender, RoutedEventArgs e)
        {
            FlyoutBase.ShowAttachedFlyout((FrameworkElement)sender);
        }

        private void OpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            var sliderPercent = e.NewValue / 100d;
            ApplyAppOpacity(sliderPercent);
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
                if (!Uri.TryCreate(JiraUrlTextBox.Text.Trim(), UriKind.Absolute, out var baseUri))
                {
                    await ShowErrorDialog("Please enter a valid Jira URL (e.g., https://your-domain.atlassian.net/).");
                    return;
                }

                _jiraBaseUri = baseUri;
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
                _oktaLoginCompleted = false;
                _oktaLoginInProgress = false;
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
                DisplayText = "Pending...",
                StatusText = "Queued for loading...",
                IsFavorite = _favoriteIssueKeys.Contains(issueKey)
            };

            TrackedIssues.Add(newIssueViewModel);
            AdjustWindowSize();

            _ = FetchIssueDetails(newIssueViewModel);

            IssueKeyTextBox.Text = "";
            AppLogger.Info($"Added issue '{issueKey}' to tracked list.");
        }

        private void IssueKeyTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                TrackButton_Click(TrackButton, new RoutedEventArgs());
            }
        }

        private async Task FetchIssueDetails(TrackedIssueViewModel issueViewModel)
        {
            if (issueViewModel.IsLoading)
            {
                return;
            }

            issueViewModel.IsLoading = true;
            issueViewModel.StatusText = "Loading issue details...";

            try
            {
                var (issue, errorMessage) = await _jiraService.GetIssueAsync(issueViewModel.IssueKey);

                if (issue != null)
                {
                    var activityLinks = issue.Fields?.IssueLinks?
                        .Where(link => link.Type?.Name == "Activities" && link.OutwardIssue?.Fields?.Status != null)
                        .ToList();

                    var excludedTaskLinks = activityLinks?
                        .Where(link => link.OutwardIssue?.Key?.StartsWith("TSK-", StringComparison.OrdinalIgnoreCase) == true)
                        .ToList() ?? new List<JiraIssueLink>();

                    var includedDirectLinks = activityLinks?
                        .Where(link => link.OutwardIssue?.Key?.StartsWith("TSK-", StringComparison.OrdinalIgnoreCase) != true)
                        .ToList() ?? new List<JiraIssueLink>();

                    var includedSubtasks = new List<JiraSubtask>();
                    var includedSubtaskKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    var (parentSubtasks, parentSubtaskError) = await _jiraService.GetIncludedSubtasksAsync(issueViewModel.IssueKey, IncludedSubtaskSummaryTerms);
                    if (!string.IsNullOrWhiteSpace(parentSubtaskError))
                    {
                        AppLogger.Info($"Could not load included subtasks for parent issue '{issueViewModel.IssueKey}'. Reason: {parentSubtaskError}");
                    }
                    else
                    {
                        foreach (var subtask in parentSubtasks)
                        {
                            var subtaskKey = subtask.Key ?? string.Empty;
                            if (includedSubtaskKeys.Add(subtaskKey))
                            {
                                includedSubtasks.Add(subtask);
                            }
                        }
                    }

                    foreach (var excludedTask in excludedTaskLinks)
                    {
                        var taskKey = excludedTask.OutwardIssue?.Key;
                        if (string.IsNullOrWhiteSpace(taskKey))
                        {
                            continue;
                        }

                        var (subtasks, subtaskError) = await _jiraService.GetIncludedSubtasksAsync(taskKey, IncludedSubtaskSummaryTerms);
                        if (!string.IsNullOrWhiteSpace(subtaskError))
                        {
                            AppLogger.Info($"Could not load included subtasks for '{taskKey}'. Reason: {subtaskError}");
                            continue;
                        }

                        foreach (var subtask in subtasks)
                        {
                            var subtaskKey = subtask.Key ?? string.Empty;
                            if (includedSubtaskKeys.Add(subtaskKey))
                            {
                                includedSubtasks.Add(subtask);
                            }
                        }
                    }

                    var total = includedDirectLinks.Count + includedSubtasks.Count;
                    var doneDirect = includedDirectLinks.Count(link => link.OutwardIssue!.Fields!.Status!.Name == "Done");
                    var doneSubtasks = includedSubtasks.Count(subtask => subtask.Fields?.Status?.Name == "Done");
                    var done = doneDirect + doneSubtasks;
                    var percentage = (total == 0) ? 0 : (int)((double)done / total * 100);
                    var title = BuildDisplayTitle(issue.Fields?.Summary);

                    AppLogger.Info($"Activities used for '{issueViewModel.IssueKey}' progress calculation: Count={total}, Done={done}.");
                    foreach (var excludedTask in excludedTaskLinks)
                    {
                        var key = excludedTask.OutwardIssue?.Key ?? "<unknown-key>";
                        var summary = excludedTask.OutwardIssue?.Fields?.Summary ?? "<no-summary>";
                        var status = excludedTask.OutwardIssue?.Fields?.Status?.Name ?? "<no-status>";
                        AppLogger.Info($"Excluded activity (TSK rule) for '{issueViewModel.IssueKey}': Key='{key}', Summary='{summary}', Status='{status}'.");
                    }

                    if (total == 0)
                    {
                        AppLogger.Info($"No included activities were found for '{issueViewModel.IssueKey}' after applying filters.");
                    }
                    else
                    {
                        foreach (var link in includedDirectLinks)
                        {
                            var linkedIssue = link.OutwardIssue;
                            var linkedKey = linkedIssue?.Key ?? "<unknown-key>";
                            var linkedSummary = linkedIssue?.Fields?.Summary ?? "<no-summary>";
                            var linkedStatus = linkedIssue?.Fields?.Status?.Name ?? "<no-status>";
                            AppLogger.Info($"Included activity link for '{issueViewModel.IssueKey}': Key='{linkedKey}', Summary='{linkedSummary}', Status='{linkedStatus}'.");
                        }

                        foreach (var subtask in includedSubtasks)
                        {
                            var subtaskKey = subtask.Key ?? "<unknown-key>";
                            var subtaskSummary = subtask.Fields?.Summary ?? "<no-summary>";
                            var subtaskStatus = subtask.Fields?.Status?.Name ?? "<no-status>";
                            AppLogger.Info($"Included subtask for '{issueViewModel.IssueKey}': Key='{subtaskKey}', Summary='{subtaskSummary}', Status='{subtaskStatus}'.");
                        }
                    }

                    issueViewModel.Progress = percentage;
                    issueViewModel.DisplayText = $"{title} ({percentage}%)";
                    issueViewModel.StatusText = $"{done}/{total} Done";
                    AppLogger.Info($"Issue '{issueViewModel.IssueKey}' loaded successfully. Progress={percentage}%.");
                    AdjustWindowSize();
                }
                else
                {
                    var friendlyError = string.IsNullOrWhiteSpace(errorMessage) ? "Issue lookup failed." : errorMessage;
                    issueViewModel.StatusText = $"Error: {friendlyError}";
                    issueViewModel.Progress = 0;
                    issueViewModel.DisplayText = issueViewModel.IssueKey;
                    AdjustWindowSize();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Unhandled exception while loading '{issueViewModel.IssueKey}'.", ex);
                issueViewModel.StatusText = "Error: Unexpected exception. Check logs.";
                issueViewModel.DisplayText = "Failed to load issue details.";
                AdjustWindowSize();
            }
            finally
            {
                issueViewModel.IsLoading = false;
            }
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is TrackedIssueViewModel issueToRemove)
            {
                TrackedIssues.Remove(issueToRemove);
                if (issueToRemove.IsFavorite)
                {
                    RemoveFavorite(issueToRemove.IssueKey);
                }
                AdjustWindowSize();
                AppLogger.Info($"Removed issue '{issueToRemove.IssueKey}' from tracked list.");
            }
        }

        private void FavoriteButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not TrackedIssueViewModel issue)
            {
                return;
            }

            if (issue.IsFavorite)
            {
                issue.IsFavorite = false;
                RemoveFavorite(issue.IssueKey);
                AppLogger.Info($"Issue '{issue.IssueKey}' removed from favorites.");
            }
            else
            {
                issue.IsFavorite = true;
                AddFavorite(issue.IssueKey);
                AppLogger.Info($"Issue '{issue.IssueKey}' added to favorites.");
            }
        }

        private async void IssueLink_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not TrackedIssueViewModel issue)
            {
                return;
            }

            if (_jiraBaseUri == null)
            {
                await ShowErrorDialog("Jira base URL is unavailable. Please log in again.");
                return;
            }

            try
            {
                var targetUri = new Uri(_jiraBaseUri, $"browse/{issue.IssueKey}");
                Process.Start(new ProcessStartInfo
                {
                    FileName = targetUri.ToString(),
                    UseShellExecute = true
                });
                AppLogger.Info($"Opened Jira issue link '{targetUri}'.");
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Failed to open Jira issue '{issue.IssueKey}' in browser.", ex);
                await ShowErrorDialog("Unable to open Jira issue link in your browser.");
            }
        }

        private void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is TrackedIssueViewModel issueToRetry)
            {
                if (issueToRetry.IsLoading)
                {
                    return;
                }

                AppLogger.Info($"Retry requested for issue '{issueToRetry.IssueKey}'.");
                _ = FetchIssueDetails(issueToRetry);
            }
        }

        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            _jiraService.Disconnect();
            TrackedIssues.Clear();
            _oktaLoginCompleted = false;
            _oktaLoginInProgress = false;
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

            var contentWidth = MinWindowWidth + (longestDisplayLength * 6);
            if (contentWidth < MinWindowWidth)
            {
                contentWidth = MinWindowWidth;
            }

            _appWindow.Resize(new Windows.Graphics.SizeInt32(contentWidth, newHeight));
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

        private static string BuildDisplayTitle(string? summary)
        {
            if (string.IsNullOrWhiteSpace(summary))
            {
                return "No Title";
            }

            var trimmed = summary.Trim();
            if (trimmed.Length <= MaxTitleLength)
            {
                return trimmed;
            }

            return trimmed[..(MaxTitleLength - 3)].TrimEnd() + "...";
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
            await AutoLoadFavoritesAsync();
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

            if (_oktaLoginInProgress)
            {
                AppLogger.Info($"Okta login completion skipped (attempt already in progress). Trigger={trigger}");
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
                _oktaLoginInProgress = true;
                var currentUrl = OktaWebView.Source?.ToString() ?? "<null>";
                AppLogger.Info($"Attempting Okta login completion. Trigger={trigger}, CurrentUrl='{currentUrl}'.");

                IReadOnlyList<CoreWebView2Cookie>? cookies = null;
                for (var attempt = 1; attempt <= OktaCookieRetryAttempts; attempt++)
                {
                    cookies = await OktaWebView.CoreWebView2.CookieManager.GetCookiesAsync(_jiraBaseUri.ToString());
                    AppLogger.Info($"Okta cookies retrieved. Attempt={attempt}/{OktaCookieRetryAttempts}, Count={cookies.Count}.");

                    if (HasRequiredSessionCookie(cookies))
                    {
                        break;
                    }

                    if (attempt < OktaCookieRetryAttempts)
                    {
                        await Task.Delay(OktaCookieRetryDelayMs);
                    }
                }

                if (cookies == null || !HasRequiredSessionCookie(cookies))
                {
                    AppLogger.Info("Okta session cookie is not ready yet; keeping WebView open for retry.");
                    await ShowErrorDialog("Login session is still being established. Please wait a few seconds and click Continue.");
                    return;
                }

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
            finally
            {
                _oktaLoginInProgress = false;
            }
        }

        private static bool HasRequiredSessionCookie(IEnumerable<CoreWebView2Cookie> cookies)
        {
            return cookies.Any(cookie => string.Equals(cookie.Name, "JSESSIONID", StringComparison.OrdinalIgnoreCase));
        }

        private void ApplyAppOpacity(double opacity)
        {
            var clamped = Math.Clamp(opacity, MinAppOpacity, MaxAppOpacity);
            ApplyWindowOpacity(clamped);

            var percent = (int)Math.Round(clamped * 100, MidpointRounding.AwayFromZero);
            if (OpacityValueText != null)
            {
                OpacityValueText.Text = $"{percent}%";
            }
        }

        private void ApplyWindowOpacity(double opacity)
        {
            if (_hwnd == IntPtr.Zero)
            {
                return;
            }

            var exStyle = GetWindowLong(_hwnd, GwlExStyle);
            if ((exStyle & WsExLayered) == 0)
            {
                SetWindowLong(_hwnd, GwlExStyle, exStyle | WsExLayered);
            }

            var alpha = (byte)Math.Round(Math.Clamp(opacity, MinAppOpacity, MaxAppOpacity) * 255, MidpointRounding.AwayFromZero);
            _ = SetLayeredWindowAttributes(_hwnd, 0, alpha, LwaAlpha);
        }

        private async Task AutoLoadFavoritesAsync()
        {
            _favoriteIssueKeys.Clear();
            foreach (var key in _favoritesStore.LoadFavorites())
            {
                _favoriteIssueKeys.Add(key);
            }

            AppLogger.Info($"Loaded {_favoriteIssueKeys.Count} favorite issue(s).");
            if (_favoriteIssueKeys.Count == 0)
            {
                return;
            }

            var keysToAdd = _favoriteIssueKeys
                .Where(key => Regex.IsMatch(key, @"^PC-\d+$"))
                .Where(key => TrackedIssues.All(issue => !string.Equals(issue.IssueKey, key, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            foreach (var issueKey in keysToAdd)
            {
                var viewModel = new TrackedIssueViewModel
                {
                    IssueKey = issueKey,
                    DisplayText = "Pending...",
                    StatusText = "Queued for loading...",
                    IsFavorite = true
                };

                TrackedIssues.Add(viewModel);
                _ = FetchIssueDetails(viewModel);
            }

            if (keysToAdd.Count > 0)
            {
                AdjustWindowSize();
                AppLogger.Info($"Auto-loaded {keysToAdd.Count} favorite issue(s).");
            }

            await Task.CompletedTask;
        }

        private void AddFavorite(string issueKey)
        {
            _favoriteIssueKeys.Add(issueKey);
            _favoritesStore.SaveFavorites(_favoriteIssueKeys);
        }

        private void RemoveFavorite(string issueKey)
        {
            _favoriteIssueKeys.Remove(issueKey);
            _favoritesStore.SaveFavorites(_favoriteIssueKeys);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
    }
}
