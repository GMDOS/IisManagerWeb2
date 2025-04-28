using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using IisManagerWeb.Shared.Models;
using Microsoft.Web.Administration;
using System.Runtime.Versioning;

namespace IisManagerWeb.Api.Services;

/// <summary>
/// Serviço responsável por monitorar e armazenar métricas do servidor
/// </summary>
[SupportedOSPlatform("windows")]
[UnsupportedOSPlatform("browser")]

public class ServerMonitorService
{
    private readonly ConcurrentDictionary<string, WebSocket> _clients = new();
    private readonly List<ServerMetrics> _metricsHistory = new();
    private readonly object _historyLock = new();
    private readonly PerformanceCounter _cpuCounter;
    private readonly PerformanceCounter _memoryCounter;
    private readonly PerformanceCounter _requestsCounter;
    private readonly PerformanceCounter _connectionsCounter;
    private readonly Dictionary<string, PerformanceCounter> _siteRequestCounters = new();
    private int _refreshInterval = 1000; 
    
    [StructLayout(LayoutKind.Sequential)]
    public struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
    
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);
    
    public ServerMonitorService()
    {
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
            _memoryCounter = new PerformanceCounter("Memory", "Available MBytes", true);
            
            _cpuCounter.NextValue();
            _memoryCounter.NextValue();
            
            try 
            {
                _requestsCounter = new PerformanceCounter("Web Service", "Total Method Requests/sec", "_Total", true);
                _connectionsCounter = new PerformanceCounter("Web Service", "Current Connections", "_Total", true);
                
                _requestsCounter.NextValue();
                _connectionsCounter.NextValue();
                
                Console.WriteLine("Contadores IIS inicializados com sucesso");
            }
            catch (Exception iisEx)
            {
                Console.WriteLine($"Falha ao inicializar contadores IIS: {iisEx.ToString()}");
                _requestsCounter = null;
                _connectionsCounter = null;
            }
            
            Console.WriteLine("Contadores de performance inicializados com sucesso");
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine($"Erro de permissão ao inicializar contadores de performance: {ex.ToString()}");
            Console.WriteLine("Isto pode ocorrer se a aplicação não está sendo executada como administrador.");
            _cpuCounter = null;
            _memoryCounter = null;
            _requestsCounter = null;
            _connectionsCounter = null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao inicializar contadores de performance: {ex.ToString()}");
            Console.WriteLine("Usando métodos alternativos para métricas do sistema");
            _cpuCounter = null;
            _memoryCounter = null;
            _requestsCounter = null;
            _connectionsCounter = null;
        }
    }
    
    /// <summary>
    /// Adiciona um cliente WebSocket para receber atualizações
    /// </summary>
    public void AddClient(string id, WebSocket webSocket)
    {
        _clients.TryAdd(id, webSocket);
    }
    
    /// <summary>
    /// Remove um cliente WebSocket
    /// </summary>
    public void RemoveClient(string id)
    {
        _clients.TryRemove(id, out _);
    }
    
    public void UpdateRefreshInterval(int milliseconds)
    {
        if (milliseconds >= 100) // Limite mínimo de 100ms
        {
            _refreshInterval = milliseconds;
        }
    }
    
    public int GetRefreshInterval() => _refreshInterval;
    
    public ServerMetrics GetCurrentMetrics()
    {
        double cpuUsage = 0;
        double availableMemoryMB = 0;
        double totalMemoryMB = 0;
        double requestsPerSecond = 0;
        double currentConnections = 0;
        
        try
        {
            if (_cpuCounter != null)
            {
                cpuUsage = Math.Round(_cpuCounter.NextValue(), 2);
            }
            else
            {
                cpuUsage = GetCpuUsageAlternative();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao obter uso de CPU: {ex.ToString()}");
            cpuUsage = GetCpuUsageAlternative();
        }
        
        try
        {
            totalMemoryMB = GetTotalMemoryInMB();
            
            if (_memoryCounter != null)
            {
                availableMemoryMB = Math.Round(_memoryCounter.NextValue(), 2);
            }
            else
            {
                availableMemoryMB = GetAvailableMemoryInMB();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao obter informações de memória: {ex.ToString()}");
            availableMemoryMB = GetAvailableMemoryInMB();
        }
        
        try
        {
            if (_requestsCounter != null)
            {
                requestsPerSecond = Math.Round(_requestsCounter.NextValue(), 2);
            }
            else 
            {
                requestsPerSecond = 0;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao obter contagem de requisições: {ex.ToString()}");
            requestsPerSecond = 0;
        }
        
        try
        {
            if (_connectionsCounter != null)
            {
                currentConnections = Math.Round(_connectionsCounter.NextValue(), 2);
            }
            else 
            {
                try 
                {
                    using var serverManager = new ServerManager();
                    currentConnections = serverManager.Sites.Count(site => site.State == ObjectState.Started);
                }
                catch 
                {
                    currentConnections = 0;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao obter conexões ativas: {ex.ToString()}");
            currentConnections = 0;
        }
        
        return new ServerMetrics
        {
            CpuUsage = cpuUsage,
            MemoryUsage = totalMemoryMB > 0 ? Math.Round(100 * (totalMemoryMB - availableMemoryMB) / totalMemoryMB, 2) : 0,
            AvailableMemory = availableMemoryMB,
            TotalMemory = totalMemoryMB,
            RequestsPerSecond = requestsPerSecond,
            ActiveConnections = (int)currentConnections,
            Timestamp = DateTime.UtcNow
        };
    }
    
    public List<SiteMetrics> GetSiteMetrics()
    {
        var siteMetrics = new List<SiteMetrics>();
        
        try
        {
            using var serverManager = new ServerManager();
            var timestamp = DateTime.UtcNow;
            
            foreach (var site in serverManager.Sites)
            {
                var siteName = site.Name;
                
                if (!_siteRequestCounters.ContainsKey(siteName))
                {
                    try
                    {
                        // var requestCounter = new PerformanceCounter("Web Service", "Get Requests/sec", siteName, true);
                        // _siteRequestCounters[siteName + "_requests"] = requestCounter;
                        // requestCounter.NextValue();
                        

                        // var bytesReceivedCounter = new PerformanceCounter("Web Service", "Total Bytes Received", siteName, true);
                        // _siteRequestCounters[siteName + "_bytesReceived"] = bytesReceivedCounter;
                        // bytesReceivedCounter.NextValue();

                        // var bytesSentCounter = new PerformanceCounter("Web Service", "Total Bytes Sent", siteName, true);
                        // _siteRequestCounters[siteName + "_bytesSent"] = bytesSentCounter;
                        // bytesSentCounter.NextValue();
                        
                        // var connectionCounter = new PerformanceCounter("Web Service", "Current Connections", siteName, true);
                        // _siteRequestCounters[siteName + "_connections"] = connectionCounter;
                        // connectionCounter.NextValue();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Erro ao criar contador para o site {siteName}: {ex.ToString()}");
                        continue;
                    }
                }
                
                try
                {
                    var metrics = new SiteMetrics
                    {
                        SiteName = siteName,
                        Timestamp = timestamp,
                        RequestsPerSecond = Math.Round(_siteRequestCounters.ContainsKey(siteName + "_requests") 
                            ? _siteRequestCounters[siteName + "_requests"].NextValue() : 0, 2),
                        BytesReceivedPerSecond = Math.Round(_siteRequestCounters.ContainsKey(siteName + "_bytesReceived") 
                            ? _siteRequestCounters[siteName + "_bytesReceived"].NextValue() : 0, 2),
                        BytesSentPerSecond = Math.Round(_siteRequestCounters.ContainsKey(siteName + "_bytesSent") 
                            ? _siteRequestCounters[siteName + "_bytesSent"].NextValue() : 0, 2),
                        ActiveConnections = (int)(_siteRequestCounters.ContainsKey(siteName + "_connections") 
                            ? _siteRequestCounters[siteName + "_connections"].NextValue() : 0)
                    };
                    
                    siteMetrics.Add(metrics);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erro ao obter métricas do site {siteName}: {ex.ToString()}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao obter lista de sites: {ex.ToString()}");
        }
        
        return siteMetrics;
    }
    
    /// <summary>
    /// Adiciona uma nova leitura ao histórico e mantém apenas os últimos 5 minutos
    /// </summary>
    public void UpdateMetricsHistory(ServerMetrics metrics)
    {
        lock (_historyLock)
        {
            _metricsHistory.Add(metrics);
            
            var cutoffTime = DateTime.UtcNow.AddMinutes(-5);
            _metricsHistory.RemoveAll(m => m.Timestamp < cutoffTime);
        }
    }
    
    /// <summary>
    /// Obtém o histórico de métricas dos últimos 5 minutos
    /// </summary>
    public ServerMetricsHistory GetMetricsHistory()
    {
        lock (_historyLock)
        {
            var cutoffTime = DateTime.UtcNow.AddMinutes(-5);
            return new ServerMetricsHistory
            {
                Metrics = _metricsHistory
                    .Where(m => m.Timestamp >= cutoffTime)
                    .OrderBy(m => m.Timestamp)
                    .ToList()
            };
        }
    }
    
    /// <summary>
    /// Envia métricas atualizadas para todos os clientes WebSocket
    /// </summary>
    public async Task BroadcastMetricsAsync(ServerMetrics metrics, List<SiteMetrics> siteMetrics)
    {
        var packet = new MetricsWebSocketPacket
        {
            Type = "metrics",
            Data = metrics,
            SiteMetrics = siteMetrics
        };
        
        var json = JsonSerializer.Serialize(packet, AppJsonSerializerContext.Default.MetricsWebSocketPacket);
        var bytes = Encoding.UTF8.GetBytes(json);
        var buffer = new ArraySegment<byte>(bytes);
        
        var deadConnections = new List<string>();
        
        foreach (var client in _clients)
        {
            try
            {
                if (client.Value.State == WebSocketState.Open)
                {
                    await client.Value.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
                }
                else
                {
                    deadConnections.Add(client.Key);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao enviar métricas para cliente {client.Key}: {ex.ToString()}");
                deadConnections.Add(client.Key);
            }
        }
        
        foreach (var id in deadConnections)
        {
            RemoveClient(id);
        }
    }
    
    /// <summary>
    /// Obtém a memória total do sistema em MB
    /// </summary>
    private double GetTotalMemoryInMB()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var memStatus = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf(typeof(MemoryStatusEx)) };
                if (GlobalMemoryStatusEx(ref memStatus))
                {
                    return Math.Round(memStatus.ullTotalPhys / 1024.0 / 1024.0, 2);
                }
            }
            
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (File.Exists("/proc/meminfo"))
                {
                    var memInfo = File.ReadAllText("/proc/meminfo");
                    var match = System.Text.RegularExpressions.Regex.Match(memInfo, @"MemTotal:\s+(\d+)\s+kB");
                    if (match.Success && long.TryParse(match.Groups[1].Value, out var totalKb))
                    {
                        return Math.Round(totalKb / 1024.0, 2);
                    }
                }
            }
            
            return Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024.0 / 1024.0, 2);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao obter memória total: {ex.ToString()}");
            return 0;
        }
    }
    
    /// <summary>
    /// Obtém a memória disponível do sistema em MB (método alternativo sem PerformanceCounter)
    /// </summary>
    private double GetAvailableMemoryInMB()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var memStatus = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf(typeof(MemoryStatusEx)) };
                if (GlobalMemoryStatusEx(ref memStatus))
                {
                    return Math.Round(memStatus.ullAvailPhys / 1024.0 / 1024.0, 2);
                }
            }
            
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (File.Exists("/proc/meminfo"))
                {
                    var memInfo = File.ReadAllText("/proc/meminfo");
                    var match = System.Text.RegularExpressions.Regex.Match(memInfo, @"MemAvailable:\s+(\d+)\s+kB");
                    if (match.Success && long.TryParse(match.Groups[1].Value, out var availableKb))
                    {
                        return Math.Round(availableKb / 1024.0, 2);
                    }
                    
                    var matchFree = System.Text.RegularExpressions.Regex.Match(memInfo, @"MemFree:\s+(\d+)\s+kB");
                    var matchBuffers = System.Text.RegularExpressions.Regex.Match(memInfo, @"Buffers:\s+(\d+)\s+kB");
                    var matchCached = System.Text.RegularExpressions.Regex.Match(memInfo, @"Cached:\s+(\d+)\s+kB");
                    
                    if (matchFree.Success && matchBuffers.Success && matchCached.Success &&
                        long.TryParse(matchFree.Groups[1].Value, out var freeKb) &&
                        long.TryParse(matchBuffers.Groups[1].Value, out var buffersKb) &&
                        long.TryParse(matchCached.Groups[1].Value, out var cachedKb))
                    {
                        return Math.Round((freeKb + buffersKb + cachedKb) / 1024.0, 2);
                    }
                }
            }
            
            return Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024.0 / 1024.0, 2);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao obter memória disponível: {ex.ToString()}");
            return 0;
        }
    }
    
    private double GetCpuUsageAlternative()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (File.Exists("/proc/stat"))
                {
                    var initialStat = File.ReadAllLines("/proc/stat")[0];
                    var initialParts = initialStat.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    
                    if (initialParts.Length < 5) return 0;
                    
                    var initialIdle = long.Parse(initialParts[4]);
                    var initialTotal = initialParts.Skip(1).Take(4).Sum(p => long.Parse(p));
                    
                    Thread.Sleep(200);
                    
                    var nextStat = File.ReadAllLines("/proc/stat")[0];
                    var nextParts = nextStat.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    
                    if (nextParts.Length < 5) return 0;
                    
                    var nextIdle = long.Parse(nextParts[4]);
                    var nextTotal = nextParts.Skip(1).Take(4).Sum(p => long.Parse(p));
                    
                    var idleDelta = nextIdle - initialIdle;
                    var totalDelta = nextTotal - initialTotal;
                    
                    if (totalDelta <= 0) return 0;
                    
                    return Math.Round(100.0 * (1.0 - (idleDelta / (double)totalDelta)), 2);
                }
            }          
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "wmic",
                    Arguments = "cpu get loadpercentage",
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                };
                
                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadToEnd();
                    var lines = output.Trim().Split('\n');
                    
                    if (lines.Length >= 2)
                    {
                        var cpuLoadStr = lines[1].Trim();
                        if (int.TryParse(cpuLoadStr, out var cpuLoad))
                        {
                            return cpuLoad;
                        }
                    }
                }
            }
            
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao obter uso alternativo de CPU: {ex.ToString()}");
            return 0;
        }
    }
} 