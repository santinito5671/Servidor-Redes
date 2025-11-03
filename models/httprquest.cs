namespace WebServer.models;

public class HttpRequest
{
    public string method { get; set; } = string.Empty;
    public string path { get; set; } = string.Empty;
    public string version { get; set; } = string.Empty;
    public Dictionary<string, string> Headers { get; set; } = new();
    public string body { get; set; } = string.Empty;
    public Dictionary<string, string> QueryParameters{ get; set; } = new();
}