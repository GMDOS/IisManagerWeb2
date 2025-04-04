using System.Text.Json.Serialization;

namespace IisManagerWeb.Shared.Models;

/// <summary>
/// Representa métricas de desempenho do servidor
/// </summary>
public class ServerMetrics
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
    
    [JsonPropertyName("cpuUsage")]
    public double CpuUsage { get; set; }
    
    [JsonPropertyName("memoryUsage")]
    public double MemoryUsage { get; set; }
    
    [JsonPropertyName("availableMemory")]
    public double AvailableMemory { get; set; }
    
    [JsonPropertyName("totalMemory")]
    public double TotalMemory { get; set; }
    
    [JsonPropertyName("requestsPerSecond")]
    public double RequestsPerSecond { get; set; }
    
    [JsonPropertyName("activeConnections")]
    public int ActiveConnections { get; set; }
}

/// <summary>
/// Representa métricas de desempenho de um site específico
/// </summary>
public class SiteMetrics
{
    [JsonPropertyName("siteName")]
    public string SiteName { get; set; } = string.Empty;
    
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
    
    [JsonPropertyName("requestsPerSecond")]
    public double RequestsPerSecond { get; set; }
    
    [JsonPropertyName("activeConnections")]
    public int ActiveConnections { get; set; }
    
    [JsonPropertyName("bytesReceivedPerSecond")]
    public double BytesReceivedPerSecond { get; set; }
    
    [JsonPropertyName("bytesSentPerSecond")]
    public double BytesSentPerSecond { get; set; }
}

/// <summary>
/// Representa uma série histórica de métricas do servidor
/// </summary>
public class ServerMetricsHistory
{
    [JsonPropertyName("metrics")]
    public List<ServerMetrics> Metrics { get; set; } = new List<ServerMetrics>();
}

/// <summary>
/// Representa um pacote WebSocket para envio de métricas em tempo real
/// </summary>
public class MetricsWebSocketPacket
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "metrics";
    
    [JsonPropertyName("data")]
    public ServerMetrics? Data { get; set; }
    
    [JsonPropertyName("siteMetrics")]
    public List<SiteMetrics>? SiteMetrics { get; set; }
    
    [JsonPropertyName("message")]
    public string? Message { get; set; }
    
    [JsonPropertyName("refreshInterval")]
    public int? RefreshInterval { get; set; }
} 