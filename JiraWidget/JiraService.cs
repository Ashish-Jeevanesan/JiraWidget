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

                AppLogger.Info($"Configured Jira client for '{baseUrl}' with auto-redirect disabled.");
                AppLogger.Info($"Configured Jira client for '{baseUrl}'.");
                return (true, null);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to configure Jira client.", ex);
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
                var response = await _httpClient.GetAsync("/rest/api/3/myself");
                if (response.IsSuccessStatusCode)
                {
                    AppLogger.Info("Jira connection validation succeeded.");
                    return (true, null);
                }

                if (IsRedirect(response))
                {
                    var location = response.Headers.Location?.ToString() ?? "<unknown>";
                    AppLogger.Error($"Connection validation redirected. Status={(int)response.StatusCode}, Location={location}");
                    return (false, "Authentication was redirected to a login page (likely Okta/SSO). API token-based access is not valid for this flow.");
                }

                var error = await BuildErrorMessageAsync(response);
                AppLogger.Error($"Jira connection validation failed: {error}");
                return (false, error);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Exception during Jira connection validation.", ex);
                return (false, $"Exception: {ex.Message}");
            }
        }

        public async Task<(JiraIssue? issue, string? errorMessage)> GetIssueAsync(string issueKey)
        {
            if (_httpClient == null)
            {
                return (null, "Not connected.");
            }

            var (issue, errorV3) = await GetIssueForApiVersionAsync(issueKey, "3");
            if (issue != null)
            {
                return (issue, null);
            }

            AppLogger.Info($"API v3 issue lookup failed for '{issueKey}'. Trying v2. Reason: {errorV3}");
            var (issueV2, errorV2) = await GetIssueForApiVersionAsync(issueKey, "2");
            return issueV2 != null ? (issueV2, null) : (null, errorV2 ?? errorV3);
        }

        private async Task<(JiraIssue? issue, string? errorMessage)> GetIssueForApiVersionAsync(string issueKey, string apiVersion)
        {
            try
            {
                var encodedIssueKey = Uri.EscapeDataString(issueKey);
                var response = await _httpClient!.GetAsync($"/rest/api/{apiVersion}/issue/{encodedIssueKey}?fields=summary,status,issuelinks");

                if (IsRedirect(response))
                {
                    var location = response.Headers.Location?.ToString() ?? "<unknown>";
                    AppLogger.Error($"Issue lookup redirected for '{issueKey}' (api/{apiVersion}). Status={(int)response.StatusCode}, Location={location}");
                    return (null, "Request was redirected to a login page (Okta/SSO). This Jira endpoint requires session/OAuth auth rather than the current token.");
                }
                response = await _httpClient.GetAsync($"/rest/api/3/issue/{encodedIssueKey}?fields=summary,status,issuelinks");

                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();

                    if (!LooksLikeJson(response, body))
                    {
                        var snippet = GetSnippet(body);
                        var contentType = response.Content.Headers.ContentType?.MediaType ?? "unknown";
                        AppLogger.Error($"Non-JSON success response for issue '{issueKey}' (api/{apiVersion}). Status={(int)response.StatusCode}, ContentType={contentType}, Snippet={snippet}");
                        AppLogger.Error($"Non-JSON success response for issue '{issueKey}'. Status={(int)response.StatusCode}, ContentType={contentType}, Snippet={snippet}");
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
                        AppLogger.Error($"Failed to parse Jira issue response for '{issueKey}'. Snippet={GetSnippet(body)}", ex);
                        return (null, "Jira returned an unexpected response format.");
                    }
                }

                var error = await BuildErrorMessageAsync(response);
                AppLogger.Error($"Issue lookup failed for '{issueKey}' (api/{apiVersion}): {error}");
                AppLogger.Error($"Issue lookup failed for '{issueKey}': {error}");
                return (null, error);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Exception while fetching issue '{issueKey}' (api/{apiVersion}).", ex);
                AppLogger.Error($"Exception while fetching issue '{issueKey}'.", ex);
                return (null, $"Exception: {ex.Message}");
            }
        }

        private static async Task<string> BuildErrorMessageAsync(HttpResponseMessage response)
        {
            var body = await response.Content.ReadAsStringAsync();
            if (!string.IsNullOrWhiteSpace(body))
            {
                if (!LooksLikeJson(response, body))
                {
                    var contentType = response.Content.Headers.ContentType?.MediaType ?? "unknown";
                    AppLogger.Error($"Non-JSON error response. Status={(int)response.StatusCode}, ContentType={contentType}, Snippet={GetSnippet(body)}");
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
            AppLogger.Info("Disconnected Jira client.");
        }
    }
}
