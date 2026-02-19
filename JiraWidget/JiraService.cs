using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
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
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pat);
            }
            catch (Exception)
            {
                // Errors will be caught when an actual API call is made.
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
                // Using POST to the /search endpoint with JQL.
                var jql = $"key = '{issueKey}'";
                var jsonBody = $"{{\"jql\":\"{jql}\",\"fields\":[\"summary\",\"status\",\"issuelinks\"]}}";
                var content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("/rest/api/2/search", content);

                if (response.IsSuccessStatusCode)
                {
                    var jsonString = await response.Content.ReadAsStringAsync();
                    var searchResult = JsonSerializer.Deserialize<JiraSearchResult>(jsonString);
                    var issue = searchResult?.Issues?.FirstOrDefault();
                    if (issue == null)
                    {
                        return (null, "Issue not found via search.");
                    }
                    return (issue, null);
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    return (null, $"{(int)response.StatusCode}: {response.ReasonPhrase}");
                }
            }
            catch (Exception ex)
            {
                return (null, $"Exception: {ex.Message}");
            }
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
        }
    }
}
