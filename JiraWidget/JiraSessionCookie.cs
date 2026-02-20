namespace JiraWidget
{
    public class JiraSessionCookie
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
        public string Path { get; set; } = "/";
        public bool IsSecure { get; set; }
        public bool IsHttpOnly { get; set; }
    }
}
