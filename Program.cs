using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

// Crear directorios necesarios al inicio
Directory.CreateDirectory("Config");
Directory.CreateDirectory("StaticFiles");
Directory.CreateDirectory("Logs");

Console.WriteLine(" Inicializando servidor web...");
Console.WriteLine(" Directorio actual: " + Directory.GetCurrentDirectory());

ServerConfig _config = new();

// Cargar configuración
LoadConfiguration();

// Iniciar servidor
await StartServer();

void LoadConfiguration()
{
    try
    {
        var configPath = "Config/server-config.json";
        Console.WriteLine($" Buscando configuración en: {Path.GetFullPath(configPath)}");
        
        if (File.Exists(configPath))
        {
            var json = File.ReadAllText(configPath);
            _config = JsonSerializer.Deserialize<ServerConfig>(json) ?? new ServerConfig();
            Console.WriteLine($" Configuración cargada - Puerto: {_config.Port}, Carpeta: {_config.DocumentRoot}");
        }
        else
        {
            // Crear configuración por defecto
            _config = new ServerConfig();
            var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configPath, json);
            Console.WriteLine($" Archivo de configuración creado - Puerto: {_config.Port}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($" Error cargando configuración: {ex.Message}");
    }
}

async Task StartServer()
{
    try
    {
        var listener = new TcpListener(IPAddress.Any, _config.Port);
        listener.Start();
        
        Console.WriteLine($" Servidor escuchando en puerto {_config.Port}");
        Console.WriteLine($" URL: http://localhost:{_config.Port}");
        Console.WriteLine($" Sirviendo archivos desde: {Path.GetFullPath(_config.DocumentRoot)}");
        Console.WriteLine("  Presiona Ctrl+C para detener el servidor");
        Console.WriteLine("=".PadRight(50, '='));
        
        while (true)
        {
            var client = await listener.AcceptTcpClientAsync();
            _ = Task.Run(() => HandleClient(client)); // Manejo concurrente
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($" Error en el servidor: {ex.Message}");
        throw;
    }
}

async Task HandleClient(TcpClient client)
{
    var clientEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
    var clientIp = clientEndPoint?.Address.ToString() ?? "desconocido";
    var clientId = Guid.NewGuid().ToString()[..6];
    
    try
    {
        using (client)
        using (var stream = client.GetStream())
        using (var reader = new StreamReader(stream))
        {
            // Leer la primera línea del request
            var requestLine = await reader.ReadLineAsync();
            if (string.IsNullOrEmpty(requestLine))
            {
                Console.WriteLine($"📭 Cliente {clientId}: Request vacío");
                return;
            }
            
            // Parsear método y ruta
            var requestParts = requestLine.Split(' ');
            if (requestParts.Length < 2)
            {
                Console.WriteLine($" Cliente {clientId}: Request mal formado");
                return;
            }
            
            var method = requestParts[0];
            var path = requestParts[1];
            
            Console.WriteLine($" Cliente {clientId}: {method} {path}");
            
            // Leer headers
            var headers = new Dictionary<string, string>();
            string? headerLine;
            while (!string.IsNullOrEmpty(headerLine = await reader.ReadLineAsync()))
            {
                var separatorIndex = headerLine.IndexOf(':');
                if (separatorIndex > 0)
                {
                    var key = headerLine[..separatorIndex].Trim();
                    var value = headerLine[(separatorIndex + 1)..].Trim();
                    headers[key] = value;
                }
            }
            
            // Leer body si es POST
            string body = "";
            if (method == "POST" && headers.ContainsKey("Content-Length"))
            {
                var contentLength = int.Parse(headers["Content-Length"]);
                var buffer = new char[contentLength];
                await reader.ReadBlockAsync(buffer, 0, contentLength);
                body = new string(buffer);
            }
            
            // Log de la solicitud
            LogRequest(clientIp, method, path, "200");
            
            // Manejar la solicitud
            await HandleRequest(stream, method, path, body, clientId);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($" Error con cliente {clientId}: {ex.Message}");
        LogRequest(clientIp, "UNKNOWN", "ERROR", "500");
    }
    
    Console.WriteLine($" Cliente {clientId} desconectado");
}

async Task HandleRequest(NetworkStream stream, string method, string path, string body, string clientId)
{
    try
    {
        // Servir archivos estáticos para GET
        if (method == "GET")
        {
            await ServeStaticFile(stream, path, clientId);
        }
        else if (method == "POST")
        {
            // Loggear datos POST
            Console.WriteLine($" Cliente {clientId}: POST Data: {body}");
            await SendTextResponse(stream, "200 OK", "text/plain", "Datos recibidos y logueados");
        }
        else
        {
            await SendTextResponse(stream, "405 Method Not Allowed", "text/plain", "Método no permitido");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($" Error manejando request {clientId}: {ex.Message}");
        await SendTextResponse(stream, "500 Internal Server Error", "text/plain", "Error interno del servidor");
    }
}

async Task ServeStaticFile(NetworkStream stream, string path, string clientId)
{
    // Si la ruta es "/", servir index.html
    if (path == "/")
    {
        path = "/index.html";
    }
    
    var filePath = Path.Combine(_config.DocumentRoot, path.TrimStart('/'));
    
    Console.WriteLine($" Cliente {clientId}: Buscando archivo: {filePath}");
    
    if (File.Exists(filePath))
    {
        var content = await File.ReadAllBytesAsync(filePath);
        var contentType = GetContentType(filePath);
        
        Console.WriteLine($" Cliente {clientId}: Sirviendo {path} ({content.Length} bytes)");
        await SendBinaryResponse(stream, "200 OK", contentType, content);
    }
    else
    {
        Console.WriteLine($" Cliente {clientId}: Archivo no encontrado: {path}");
        await Send404Response(stream);
    }
}

async Task SendTextResponse(NetworkStream stream, string status, string contentType, string content)
{
    var bytes = Encoding.UTF8.GetBytes(content);
    await SendBinaryResponse(stream, status, contentType, bytes);
}

async Task SendBinaryResponse(NetworkStream stream, string status, string contentType, byte[] content)
{
    var response = new StringBuilder();
    response.AppendLine($"HTTP/1.1 {status}");
    response.AppendLine($"Content-Type: {contentType}");
    response.AppendLine($"Content-Length: {content.Length}");
    response.AppendLine("Connection: close");
    response.AppendLine();
    
    var headerBytes = Encoding.UTF8.GetBytes(response.ToString());
    await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
    await stream.WriteAsync(content, 0, content.Length);
}

async Task Send404Response(NetworkStream stream)
{
    var notFoundHtml = """
        <!DOCTYPE html>
        <html>
        <head>
            <title>404 No Encontrado</title>
            <style>
                body { font-family: Arial, sans-serif; text-align: center; padding: 50px; }
                h1 { color: #d32f2f; }
            </style>
        </head>
        <body>
            <h1>404 - Página No Encontrada</h1>
            <p>El archivo solicitado no existe en el servidor.</p>
        </body>
        </html>
        """;
    
    await SendTextResponse(stream, "404 Not Found", "text/html", notFoundHtml);
}

string GetContentType(string filePath)
{
    var extension = Path.GetExtension(filePath).ToLower();
    return extension switch
    {
        ".html" => "text/html",
        ".css" => "text/css",
        ".js" => "application/javascript",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".txt" => "text/plain",
        _ => "application/octet-stream"
    };
}

void LogRequest(string clientIp, string method, string path, string statusCode)
{
    try
    {
        var logDate = DateTime.Now.ToString("yyyy-MM-dd");
        var logFile = Path.Combine(_config.LogDirectory, $"access_{logDate}.log");
        
        var logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {clientIp} | {method} {path} | {statusCode}";
        
        File.AppendAllText(logFile, logEntry + Environment.NewLine);
    }
    catch (Exception ex)
    {
        Console.WriteLine($" Error escribiendo log: {ex.Message}");
    }
}


public class ServerConfig
{
    public int Port { get; set; } = 8080;
    public string DocumentRoot { get; set; } = "./StaticFiles";
    public string LogDirectory { get; set; } = "./Logs";
}