using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace JiraWidget
{
    public class JiraService
    {
        private static HttpClient? _httpClient;

        public void SetupClient(string baseUrl, string pat)
        {
            try
            {
                _httpClient = new HttpClient();
                _httpClient.BaseAddress = new Uri(baseUrl);
                _httpClient.DefaultRequestHeaders.Accept.Clear();
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pat.Trim());
            }

            try
            {
                var response = await _httpClient.GetAsync("/rest/api/3/myself");
                if (response.IsSuccessStatusCode)
                {
                    AppLogger.Info("Jira connection validation succeeded.");
                    return (true, null);
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
                    return (true, null);
                }

                return (false, await BuildErrorMessageAsync(response));
            }
            catch (Exception ex)
            {
                return (false, $"Exception: {ex.Message}");
            }
        }

        public async Task<(JiraIssue? issue, string? errorMessage)> GetIssueAsync(string issueKey)
        {
            if (_httpClient == null)
            {
                return (null, "Not connected.");
            }

            try
            {
                var encodedIssueKey = WebUtility.UrlEncode(issueKey);
                var response = await _httpClient.GetAsync($"/rest/api/3/issue/{encodedIssueKey}?fields=summary,status,issuelinks");

                if (response.IsSuccessStatusCode)
                {
                    var jsonString = await response.Content.ReadAsStringAsync();
                    var issue = JsonSerializer.Deserialize<JiraIssue>(jsonString);
                    return issue == null ? (null, "Issue not found.") : (issue, null);
                }

                return (null, await BuildErrorMessageAsync(response));
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Exception while fetching issue '{issueKey}'.", ex);
                return (null, $"Exception: {ex.Message}");
            }
        }

        private static async Task<string> BuildErrorMessageAsync(HttpResponseMessage response)
        {
            var body = await response.Content.ReadAsStringAsync();
            if (!string.IsNullOrWhiteSpace(body))
            {
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
                catch (JsonException)
                {
                    // ignore parse errors and fall back to status text.
                }
            }

            return $"{(int)response.StatusCode}: {response.ReasonPhrase}";
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
