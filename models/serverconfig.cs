namespace WebServer.Models;

public class ServerConfig
{
    public int Port { get; set; } = 8080;
    public string DocumentRoot { get; set; } = "./StaticFiles";
    public string LogDirectory { get; set; } = "./Logs";
}