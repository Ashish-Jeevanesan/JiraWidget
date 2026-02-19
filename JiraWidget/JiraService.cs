using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace JiraWidget
{
    public class JiraService
    {
        private static HttpClient _httpClient;

        public async Task<bool> ConnectAsync(string baseUrl, string email, string apiToken)
        {
            try
            {
                _httpClient = new HttpClient();
                _httpClient.BaseAddress = new Uri(baseUrl);
                _httpClient.DefaultRequestHeaders.Accept.Clear();
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{email}:{apiToken}"));
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);

                var response = await _httpClient.GetAsync("/rest/api/3/myself");

                return response.IsSuccessStatusCode;
            }
            catch (Exception)
            {
                // Could be invalid URI, network error, etc.
                return false;
            }
        }

        public async Task<JiraIssue> GetIssueAsync(string issueKey)
        {
            if (_httpClient == null)
            {
                return null; // Not connected yet
            }

            try
            {
                var response = await _httpClient.GetAsync($"/rest/api/3/issue/{issueKey}");

                if (response.IsSuccessStatusCode)
                {
                    var jsonString = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<JiraIssue>(jsonString);
                }

                return null; // Issue not found or other error
            }
            catch (Exception)
            {
                // Network error, JSON parsing error, etc.
                return null;
            }
        }
    }
}
