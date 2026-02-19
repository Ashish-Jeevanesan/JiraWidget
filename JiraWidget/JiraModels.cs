using System.Text.Json.Serialization;

namespace JiraWidget
{
    public class JiraIssue
    {
        [JsonPropertyName("key")]
        public string Key { get; set; }

        [JsonPropertyName("fields")]
        public JiraIssueFields Fields { get; set; }
    }

    public class JiraIssueFields
    {
        [JsonPropertyName("summary")]
        public string Summary { get; set; }

        [JsonPropertyName("progress")]
        public JiraProgress Progress { get; set; }
    }

    public class JiraProgress
    {
        [JsonPropertyName("percent")]
        public int Percent { get; set; }
    }
}
