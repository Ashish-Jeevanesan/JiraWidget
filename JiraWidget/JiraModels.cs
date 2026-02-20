using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace JiraWidget
{
    public class JiraIssue
    {
        [JsonPropertyName("key")]
        public string? Key { get; set; }

        [JsonPropertyName("fields")]
        public JiraIssueFields? Fields { get; set; }
    }

    public class JiraIssueFields
    {
        [JsonPropertyName("summary")]
        public string? Summary { get; set; }

        [JsonPropertyName("progress")]
        public JiraProgress? Progress { get; set; }
        
        [JsonPropertyName("issuelinks")]
        public List<JiraIssueLink>? IssueLinks { get; set; }
    }

    public class JiraProgress
    {
        [JsonPropertyName("percent")]
        public int Percent { get; set; }
    }

    public class JiraIssueLink
    {
        [JsonPropertyName("type")]
        public JiraLinkType? Type { get; set; }

        [JsonPropertyName("outwardIssue")]
        public JiraLinkedIssue? OutwardIssue { get; set; }
    }

    public class JiraLinkType
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    public class JiraLinkedIssue
    {
        [JsonPropertyName("fields")]
        public JiraLinkedIssueFields? Fields { get; set; }
    }

    public class JiraLinkedIssueFields
    {
        [JsonPropertyName("status")]
        public JiraStatus? Status { get; set; }
    }

    public class JiraStatus
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    public class JiraSearchResult
    {
        [JsonPropertyName("issues")]
        public List<JiraIssue>? Issues { get; set; }
    }

    public class JiraErrorResponse
    {
        [JsonPropertyName("errorMessages")]
        public List<string>? ErrorMessages { get; set; }

        [JsonPropertyName("errors")]
        public Dictionary<string, string>? Errors { get; set; }
    }
}
