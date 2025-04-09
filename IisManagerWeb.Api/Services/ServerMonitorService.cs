using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using IisManagerWeb.Shared.Models;
using Microsoft.Web.Administration;

namespace IisManagerWeb.Api.Services;

/// <summary>
/// Serviço responsável por monitorar e armazenar métricas do servidor
/// </summary>
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
            _requestsCounter = new PerformanceCounter("Web Service", "Total Method Requests/sec", "_Total", true);
            _connectionsCounter = new PerformanceCounter("Web Service", "Current Connections", "_Total", true);
            
            // Inicializa os contadores para leitura inicial
            _cpuCounter.NextValue();
            _memoryCounter.NextValue();
            _requestsCounter.NextValue();
            _connectionsCounter.NextValue();
            
            Console.WriteLine("Contadores de performance inicializados com sucesso");
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine($"Erro de permissão ao inicializar contadores de performance: {ex.Message}");
            Console.WriteLine("Isto pode ocorrer se a aplicação não está sendo executada como administrador.");
            // Definimos como null e usaremos métodos alternativos
            _cpuCounter = null;
            _memoryCounter = null;
            _requestsCounter = null;
            _connectionsCounter = null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao inicializar contadores de performance: {ex.Message}");
            Console.WriteLine("Usando métodos alternativos para métricas do sistema");
            // Definimos como null e usaremos métodos alternativos
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
        if (milliseconds >= 100) // Definindo limite mínimo de 100ms
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
            Console.WriteLine($"Erro ao obter uso de CPU: {ex.Message}");
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
            Console.WriteLine($"Erro ao obter informações de memória: {ex.Message}");
            availableMemoryMB = GetAvailableMemoryInMB();
        }
        
        try
        {
            if (_requestsCounter != null)
            {
                requestsPerSecond = Math.Round(_requestsCounter.NextValue(), 2);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao obter contagem de requisições: {ex.Message}");
            requestsPerSecond = 0;
        }
        
        try
        {
            if (_connectionsCounter != null)
            {
                currentConnections = Math.Round(_connectionsCounter.NextValue(), 2);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao obter conexões ativas: {ex.Message}");
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
                        var requestCounter = new PerformanceCounter("Web Service", "Total Method Requests/sec", siteName, true);
                        var bytesReceivedCounter = new PerformanceCounter("Web Service", "Bytes Received/sec", siteName, true);
                        var bytesSentCounter = new PerformanceCounter("Web Service", "Bytes Sent/sec", siteName, true);
                        var connectionCounter = new PerformanceCounter("Web Service", "Current Connections", siteName, true);
                        
                        _siteRequestCounters[siteName + "_requests"] = requestCounter;
                        _siteRequestCounters[siteName + "_bytesReceived"] = bytesReceivedCounter;
                        _siteRequestCounters[siteName + "_bytesSent"] = bytesSentCounter;
                        _siteRequestCounters[siteName + "_connections"] = connectionCounter;
                        
                        // Inicializar leituras
                        requestCounter.NextValue();
                        bytesReceivedCounter.NextValue();
                        bytesSentCounter.NextValue();
                        connectionCounter.NextValue();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Erro ao criar contador para o site {siteName}: {ex.Message}");
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
                    Console.WriteLine($"Erro ao obter métricas do site {siteName}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao obter lista de sites: {ex.Message}");
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
            
            // Remover métricas mais antigas que 5 minutos
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
                Console.WriteLine($"Erro ao enviar métricas para cliente {client.Key}: {ex.Message}");
                deadConnections.Add(client.Key);
            }
        }
        
        // Remover conexões fechadas
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
            // Método alternativo usando API Win32 para Windows
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var memStatus = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf(typeof(MemoryStatusEx)) };
                if (GlobalMemoryStatusEx(ref memStatus))
                {
                    return Math.Round(memStatus.ullTotalPhys / 1024.0 / 1024.0, 2);
                }
            }
            
            // Alternativa para Linux usando /proc/meminfo
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
            
            // Alternativa para macOS
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "sysctl",
                    Arguments = "-n hw.memsize",
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                };
                
                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadToEnd().Trim();
                    if (long.TryParse(output, out var totalBytes))
                    {
                        return Math.Round(totalBytes / 1024.0 / 1024.0, 2);
                    }
                }
            }

            // Fallback usando GC
            return Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024.0 / 1024.0, 2);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao obter memória total: {ex.Message}");
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
            // Método alternativo usando API Win32 para Windows
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var memStatus = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf(typeof(MemoryStatusEx)) };
                if (GlobalMemoryStatusEx(ref memStatus))
                {
                    return Math.Round(memStatus.ullAvailPhys / 1024.0 / 1024.0, 2);
                }
            }
            
            // Alternativa para Linux usando /proc/meminfo
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
                    
                    // Caso MemAvailable não exista, tente calcular através de MemFree + Buffers + Cached
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
            
            // Alternativa para macOS
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "vm_stat",
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                };
                
                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadToEnd();
                    
                    // Analisar a saída para encontrar páginas livres
                    var pageSize = 4096; // Tamanho de página padrão em bytes
                    var freePages = 0L;
                    
                    // Obter o tamanho da página real
                    var pageSizeProcess = Process.Start(new ProcessStartInfo
                    {
                        FileName = "sysctl",
                        Arguments = "-n hw.pagesize",
                        RedirectStandardOutput = true,
                        UseShellExecute = false
                    });
                    
                    if (pageSizeProcess != null)
                    {
                        var pageSizeOutput = pageSizeProcess.StandardOutput.ReadToEnd().Trim();
                        if (int.TryParse(pageSizeOutput, out var parsedPageSize))
                        {
                            pageSize = parsedPageSize;
                        }
                    }
                    
                    // Procurar por linhas com contagem de páginas
                    var freeMatch = System.Text.RegularExpressions.Regex.Match(output, @"Pages free:\s+(\d+)");
                    if (freeMatch.Success && long.TryParse(freeMatch.Groups[1].Value, out var pages))
                    {
                        freePages += pages;
                    }
                    
                    // Também considerar páginas inativas
                    var inactiveMatch = System.Text.RegularExpressions.Regex.Match(output, @"Pages inactive:\s+(\d+)");
                    if (inactiveMatch.Success && long.TryParse(inactiveMatch.Groups[1].Value, out pages))
                    {
                        freePages += pages;
                    }
                    
                    return Math.Round((freePages * pageSize) / 1024.0 / 1024.0, 2);
                }
            }
            
            // Fallback usando GC
            return Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024.0 / 1024.0, 2);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao obter memória disponível: {ex.Message}");
            return 0;
        }
    }
    
    private double GetCpuUsageAlternative()
    {
        try
        {
            // Para Linux
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (File.Exists("/proc/stat"))
                {
                    // Leitura inicial
                    var initialStat = File.ReadAllLines("/proc/stat")[0];
                    var initialParts = initialStat.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    
                    if (initialParts.Length < 5) return 0;
                    
                    var initialIdle = long.Parse(initialParts[4]);
                    var initialTotal = initialParts.Skip(1).Take(4).Sum(p => long.Parse(p));
                    
                    // Espere um curto período
                    Thread.Sleep(200);
                    
                    // Segunda leitura
                    var nextStat = File.ReadAllLines("/proc/stat")[0];
                    var nextParts = nextStat.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    
                    if (nextParts.Length < 5) return 0;
                    
                    var nextIdle = long.Parse(nextParts[4]);
                    var nextTotal = nextParts.Skip(1).Take(4).Sum(p => long.Parse(p));
                    
                    // Calcular diferença
                    var idleDelta = nextIdle - initialIdle;
                    var totalDelta = nextTotal - initialTotal;
                    
                    if (totalDelta <= 0) return 0;
                    
                    return Math.Round(100.0 * (1.0 - (idleDelta / (double)totalDelta)), 2);
                }
            }
            // Para macOS
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "top",
                    Arguments = "-l 2 -n 0",
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                };
                
                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadToEnd();
                    var cpuLoad = System.Text.RegularExpressions.Regex.Match(output, @"CPU usage: (\d+\.\d+)% user, (\d+\.\d+)% sys, (\d+\.\d+)% idle");
                    
                    if (cpuLoad.Success && 
                        double.TryParse(cpuLoad.Groups[1].Value, out var user) &&
                        double.TryParse(cpuLoad.Groups[2].Value, out var sys) &&
                        double.TryParse(cpuLoad.Groups[3].Value, out var idle))
                    {
                        return Math.Round(user + sys, 2);
                    }
                }
            }
            // Para Windows, tentar método alternativo baseado em WMI
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
            Console.WriteLine($"Erro ao obter uso alternativo de CPU: {ex.Message}");
            return 0;
        }
    }
} 