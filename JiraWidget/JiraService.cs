using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace JiraWidget
{
    public class JiraService
    {
        private static HttpClient? _httpClient;
        private string? _preferredApiVersion;

        public (bool isConfigured, string? errorMessage) SetupClient(string baseUrl, string pat)
        {
            try
            {
                var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = false
                };

                _httpClient = new HttpClient(handler);
                _httpClient.BaseAddress = new Uri(baseUrl);
                _httpClient.DefaultRequestHeaders.Accept.Clear();
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pat.Trim());
                _preferredApiVersion = null;

                AppLogger.Info($"Configured Jira client for '{baseUrl}'.");
                return (true, null);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to configure Jira client.", ex);
                return (false, ex.Message);
            }
        }

        public (bool isConfigured, string? errorMessage) SetupClientWithCookies(string baseUrl, IEnumerable<JiraSessionCookie> cookies)
        {
            try
            {
                var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    CookieContainer = new CookieContainer()
                };

                var baseUri = new Uri(baseUrl);
                foreach (var cookie in cookies)
                {
                    var netCookie = new Cookie(cookie.Name, cookie.Value, cookie.Path, cookie.Domain)
                    {
                        Secure = cookie.IsSecure,
                        HttpOnly = cookie.IsHttpOnly
                    };
                    var cookieDomain = cookie.Domain?.TrimStart('.') ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(cookieDomain))
                    {
                        handler.CookieContainer.Add(baseUri, netCookie);
                    }
                    else
                    {
                        var cookieUri = new Uri($"{baseUri.Scheme}://{cookieDomain}");
                        handler.CookieContainer.Add(cookieUri, netCookie);
                    }
                }

                _httpClient = new HttpClient(handler);
                _httpClient.BaseAddress = baseUri;
                _httpClient.DefaultRequestHeaders.Accept.Clear();
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                _preferredApiVersion = null;

                AppLogger.Info($"Configured Jira client for '{baseUrl}' using session cookies.");
                return (true, null);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to configure Jira client with cookies.", ex);
                return (false, ex.Message);
            }
        }

        public async Task<(bool isConnected, string? errorMessage)> ValidateConnectionAsync()
        {
            if (_httpClient == null)
            {
                return (false, "Not connected.");
            }

            try
            {
                var firstVersion = _preferredApiVersion ?? "3";
                var secondVersion = firstVersion == "3" ? "2" : "3";

                var (okFirst, errorFirst) = await TryValidateConnectionAsync(firstVersion);
                if (okFirst)
                {
                    _preferredApiVersion = firstVersion;
                    return (true, null);
                }

                AppLogger.Info($"API v{firstVersion} connection validation failed. Trying v{secondVersion}. Reason: {errorFirst}");
                var (okSecond, errorSecond) = await TryValidateConnectionAsync(secondVersion);
                if (okSecond)
                {
                    _preferredApiVersion = secondVersion;
                    AppLogger.Info($"Using Jira API v{secondVersion} for this session.");
                    return (true, null);
                }

                var finalError = errorSecond ?? errorFirst ?? "Connection validation failed.";
                AppLogger.Error($"Jira connection validation failed for both API versions. Last error: {finalError}");
                return (false, finalError);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Exception during Jira connection validation.", ex);
                return (false, $"Exception: {ex.Message}");
            }
        }

        private async Task<(bool ok, string? errorMessage)> TryValidateConnectionAsync(string apiVersion)
        {
            var response = await _httpClient!.GetAsync($"/rest/api/{apiVersion}/myself");
            if (response.IsSuccessStatusCode)
            {
                AppLogger.Info($"Jira connection validation succeeded (api/{apiVersion}).");
                return (true, null);
            }

            if (IsRedirect(response))
            {
                var location = response.Headers.Location?.ToString() ?? "<unknown>";
                return (false, "Authentication was redirected to a login page (likely Okta/SSO). API token-based access is not valid for this flow.");
            }

            var error = await BuildErrorMessageAsync(response);
            return (false, error);
        }

        public async Task<(JiraIssue? issue, string? errorMessage)> GetIssueAsync(string issueKey)
        {
            if (_httpClient == null)
            {
                return (null, "Not connected.");
            }

            var firstVersion = _preferredApiVersion ?? "3";
            var secondVersion = firstVersion == "3" ? "2" : "3";

            var (issue, firstError) = await GetIssueForApiVersionAsync(issueKey, firstVersion);
            if (issue != null)
            {
                _preferredApiVersion = firstVersion;
                return (issue, null);
            }

            AppLogger.Info($"API v{firstVersion} issue lookup failed for '{issueKey}'. Trying v{secondVersion}. Reason: {firstError}");
            var (fallbackIssue, secondError) = await GetIssueForApiVersionAsync(issueKey, secondVersion);
            if (fallbackIssue != null)
            {
                _preferredApiVersion = secondVersion;
                AppLogger.Info($"Issue lookup for '{issueKey}' succeeded on fallback API v{secondVersion}. Using it for this session.");
                return (fallbackIssue, null);
            }

            var finalError = secondError ?? firstError;
            AppLogger.Error($"Issue lookup failed for '{issueKey}' on both API versions. Last error: {finalError}");
            return (null, finalError);
        }

        public async Task<(List<JiraSubtask> subtasks, string? errorMessage)> GetIncludedSubtasksAsync(string parentIssueKey, IEnumerable<string> includeSummaryTerms)
        {
            if (_httpClient == null)
            {
                return (new List<JiraSubtask>(), "Not connected.");
            }

            var terms = includeSummaryTerms
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .Select(term => term.Trim())
                .ToList();
            if (terms.Count == 0)
            {
                return (new List<JiraSubtask>(), null);
            }

            var firstVersion = _preferredApiVersion ?? "3";
            var secondVersion = firstVersion == "3" ? "2" : "3";

            var (firstResult, firstError) = await GetIncludedSubtasksForApiVersionAsync(parentIssueKey, terms, firstVersion);
            if (firstResult != null)
            {
                _preferredApiVersion = firstVersion;
                return (firstResult, null);
            }

            AppLogger.Info($"Subtask lookup failed for '{parentIssueKey}' on api/{firstVersion}. Trying api/{secondVersion}. Reason: {firstError}");
            var (secondResult, secondError) = await GetIncludedSubtasksForApiVersionAsync(parentIssueKey, terms, secondVersion);
            if (secondResult != null)
            {
                _preferredApiVersion = secondVersion;
                return (secondResult, null);
            }

            return (new List<JiraSubtask>(), secondError ?? firstError);
        }

        private async Task<(JiraIssue? issue, string? errorMessage)> GetIssueForApiVersionAsync(string issueKey, string apiVersion)
        {
            try
            {
                var encodedIssueKey = Uri.EscapeDataString(issueKey);
                var response = await _httpClient!.GetAsync($"/rest/api/{apiVersion}/issue/{encodedIssueKey}?fields=summary,status,issuelinks");

                if (IsRedirect(response))
                {
                    return (null, "Request was redirected to a login page (Okta/SSO). This Jira endpoint requires session/OAuth auth rather than the current token.");
                }

                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();

                    if (!LooksLikeJson(response, body))
                    {
                        var snippet = GetSnippet(body);
                        var contentType = response.Content.Headers.ContentType?.MediaType ?? "unknown";
                        AppLogger.Info($"Non-JSON success response for issue '{issueKey}' (api/{apiVersion}). Status={(int)response.StatusCode}, ContentType={contentType}, Snippet={snippet}");
                        return (null, "Received non-JSON response from Jira (likely SSO/permission HTML page). Please verify Jira API access for this issue.");
                    }

                    try
                    {
                        var issue = JsonSerializer.Deserialize<JiraIssue>(body);
                        return issue == null ? (null, "Issue not found.") : (issue, null);
                    }
                    catch (JsonException ex)
                    {
                        AppLogger.Error($"Failed to parse Jira issue response for '{issueKey}' (api/{apiVersion}). Snippet={GetSnippet(body)}", ex);
                        return (null, "Jira returned an unexpected response format.");
                    }
                }

                var error = await BuildErrorMessageAsync(response);
                AppLogger.Info($"Issue lookup failed for '{issueKey}' (api/{apiVersion}): {error}");
                return (null, error);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Exception while fetching issue '{issueKey}' (api/{apiVersion}).", ex);
                return (null, $"Exception: {ex.Message}");
            }
        }

        private async Task<(List<JiraSubtask>? subtasks, string? errorMessage)> GetIncludedSubtasksForApiVersionAsync(string parentIssueKey, List<string> includeTerms, string apiVersion)
        {
            try
            {
                var encodedIssueKey = Uri.EscapeDataString(parentIssueKey);
                var response = await _httpClient!.GetAsync($"/rest/api/{apiVersion}/issue/{encodedIssueKey}?fields=subtasks");

                if (IsRedirect(response))
                {
                    return (null, "Request was redirected to login while loading subtasks.");
                }

                if (!response.IsSuccessStatusCode)
                {
                    var error = await BuildErrorMessageAsync(response);
                    return (null, error);
                }

                var body = await response.Content.ReadAsStringAsync();
                if (!LooksLikeJson(response, body))
                {
                    return (null, "Received non-JSON response while loading subtasks.");
                }

                var container = JsonSerializer.Deserialize<JiraSubtaskContainer>(body);
                var subtasks = container?.Fields?.Subtasks ?? new List<JiraSubtask>();
                AppLogger.Info($"Fetched {subtasks.Count} subtask(s) for '{parentIssueKey}' (api/{apiVersion}).");
                foreach (var subtask in subtasks)
                {
                    var key = subtask.Key ?? "<unknown-key>";
                    var summary = subtask.Fields?.Summary ?? "<no-summary>";
                    var status = subtask.Fields?.Status?.Name ?? "<no-status>";
                    AppLogger.Info($"Subtask candidate for '{parentIssueKey}': Key='{key}', Summary='{summary}', Status='{status}'.");
                }

                var included = subtasks
                    .Where(subtask => !string.IsNullOrWhiteSpace(subtask.Fields?.Summary))
                    .Where(subtask => includeTerms.Any(term =>
                        SummaryMatches(subtask.Fields!.Summary!, term)))
                    .ToList();

                AppLogger.Info($"Included {included.Count} subtask(s) for '{parentIssueKey}' after summary filters.");
                return (included, null);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Exception while loading subtasks for '{parentIssueKey}' (api/{apiVersion}).", ex);
                return (null, $"Exception: {ex.Message}");
            }
        }

        private static bool SummaryMatches(string summary, string term)
        {
            if (summary.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var normalizedSummary = NormalizeAlphaNumeric(summary);
            var normalizedTerm = NormalizeAlphaNumeric(term);
            return normalizedSummary.Contains(normalizedTerm, StringComparison.Ordinal);
        }

        private static string NormalizeAlphaNumeric(string input)
        {
            var chars = input
                .Where(char.IsLetterOrDigit)
                .Select(char.ToUpperInvariant)
                .ToArray();
            return new string(chars);
        }

        private static async Task<string> BuildErrorMessageAsync(HttpResponseMessage response)
        {
            var body = await response.Content.ReadAsStringAsync();
            if (!string.IsNullOrWhiteSpace(body))
            {
                if (!LooksLikeJson(response, body))
                {
                    var contentType = response.Content.Headers.ContentType?.MediaType ?? "unknown";
                    AppLogger.Info($"Non-JSON error response. Status={(int)response.StatusCode}, ContentType={contentType}, Snippet={GetSnippet(body)}");
                    return $"{(int)response.StatusCode}: Jira returned an HTML/non-JSON response (possible SSO redirect or access page).";
                }

                try
                {
                    var jiraError = JsonSerializer.Deserialize<JiraErrorResponse>(body);
                    var parsedError = jiraError?.ErrorMessages?.FirstOrDefault()
                        ?? jiraError?.Errors?.Values.FirstOrDefault();

                    if (!string.IsNullOrWhiteSpace(parsedError))
                    {
                        return $"{(int)response.StatusCode}: {parsedError}";
                    }
                }
                catch (JsonException ex)
                {
                    AppLogger.Error($"Failed to parse Jira error response. Snippet={GetSnippet(body)}", ex);
                }
            }

            return $"{(int)response.StatusCode}: {response.ReasonPhrase}";
        }

        private static bool IsRedirect(HttpResponseMessage response)
        {
            var code = (int)response.StatusCode;
            return code is >= 300 and < 400;
        }

        private static bool LooksLikeJson(HttpResponseMessage response, string body)
        {
            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (!string.IsNullOrWhiteSpace(contentType) && contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var trimmed = body.TrimStart();
            return trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal);
        }

        private static string GetSnippet(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return "<empty>";
            }

            var normalized = body.Replace("\r", " ").Replace("\n", " ").Trim();
            if (normalized.Length > 220)
            {
                return normalized[..220] + "...";
            }

            return normalized;
        }

        public int CalculateProgressPercentage(JiraIssue? issue)
        {
            if (issue?.Fields?.IssueLinks == null || issue.Fields.IssueLinks.Count == 0)
            {
                return 0;
            }

            var activityLinks = issue.Fields.IssueLinks
                .Where(link => link.Type?.Name == "Activities" && link.OutwardIssue?.Fields?.Status != null)
                .ToList();

            if (activityLinks.Count == 0)
            {
                return 0;
            }

            var doneCount = activityLinks.Count(link => link.OutwardIssue!.Fields!.Status!.Name == "Done");
            return (int)((double)doneCount / activityLinks.Count * 100);
        }

        public void Disconnect()
        {
            _httpClient?.Dispose();
            _httpClient = null;
            _preferredApiVersion = null;
            AppLogger.Info("Disconnected Jira client.");
        }
    }
}
