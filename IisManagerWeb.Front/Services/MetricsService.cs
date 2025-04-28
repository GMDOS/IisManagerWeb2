using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using IisManagerWeb.Shared.Models;

namespace IisManagerWeb.Front.Services;

/// <summary>
/// Serviço para obter dados de métricas e se conectar ao WebSocket
/// </summary>
public class MetricsService : IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _wsBaseUrl;
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private bool _isConnected;
    private readonly object _connectionLock = new();
    
    private readonly List<Action<ServerMetrics>> _metricsCallbacks = new();
    private readonly List<Action<List<SiteMetrics>>> _siteMetricsCallbacks = new();
    private readonly List<Action<int>> _refreshIntervalCallbacks = new();
    
    public MetricsService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        
        var baseUrl = httpClient.BaseAddress?.ToString() ?? "http://localhost:5135/";
        _wsBaseUrl = baseUrl.Replace("http://", "ws://").Replace("https://", "wss://").Replace("/api/api","/");
    }
    
    /// <summary>
    /// Obtém as métricas atuais do servidor
    /// </summary>
    public async Task<ServerMetrics> GetCurrentMetricsAsync()
    {
        return await _httpClient.GetFromJsonAsync<ServerMetrics>("metrics/current") 
            ?? new ServerMetrics { Timestamp = DateTime.UtcNow };
    }
    
    /// <summary>
    /// Obtém as métricas dos sites
    /// </summary>
    public async Task<List<SiteMetrics>> GetSiteMetricsAsync()
    {
        return await _httpClient.GetFromJsonAsync<List<SiteMetrics>>("metrics/sites") 
            ?? new List<SiteMetrics>();
    }
    
    /// <summary>
    /// Obtém o histórico de métricas dos últimos 5 minutos
    /// </summary>
    public async Task<ServerMetricsHistory> GetMetricsHistoryAsync()
    {
        return await _httpClient.GetFromJsonAsync<ServerMetricsHistory>("metrics/history") 
            ?? new ServerMetricsHistory();
    }
    
    /// <summary>
    /// Conecta ao WebSocket para receber atualizações em tempo real
    /// </summary>
    public async Task ConnectToWebSocketAsync()
    {
        lock (_connectionLock)
        {
            if (_isConnected) 
                return;
            
            _isConnected = true;
        }
        
        try
        {
            _cts = new CancellationTokenSource();
            _webSocket = new ClientWebSocket();
            
            await _webSocket.ConnectAsync(new Uri($"{_wsBaseUrl}metrics/ws"), _cts.Token);
            Console.WriteLine("WebSocket conectado com sucesso");
            
            _ = ReceiveMessagesAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao conectar WebSocket: {ex.ToString()}");
            lock (_connectionLock)
            {
                _isConnected = false;
            }
            throw;
        }
    }
    
    /// <summary>
    /// Desconecta do WebSocket
    /// </summary>
    public async Task DisconnectAsync()
    {
        lock (_connectionLock)
        {
            if (!_isConnected)
                return;
            
            _isConnected = false;
        }
        
        try
        {
            _cts?.Cancel();
            
            if (_webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Desconexão solicitada pelo cliente", 
                    CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao desconectar WebSocket: {ex.ToString()}");
        }
        finally
        {
            _webSocket?.Dispose();
            _webSocket = null;
            _cts?.Dispose();
            _cts = null;
        }
    }
    
    /// <summary>
    /// Registra um callback para receber atualizações de métricas do servidor
    /// </summary>
    public void OnMetricsUpdate(Action<ServerMetrics> callback)
    {
        _metricsCallbacks.Add(callback);
    }
    
    /// <summary>
    /// Registra um callback para receber atualizações de métricas de sites
    /// </summary>
    public void OnSiteMetricsUpdate(Action<List<SiteMetrics>> callback)
    {
        _siteMetricsCallbacks.Add(callback);
    }
    
    /// <summary>
    /// Registra um callback para receber atualizações do intervalo de atualização
    /// </summary>
    public void OnRefreshIntervalUpdate(Action<int> callback)
    {
        _refreshIntervalCallbacks.Add(callback);
    }
    
    /// <summary>
    /// Processa mensagens recebidas do servidor via WebSocket
    /// </summary>
    private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
    {
        if (_webSocket == null) return;
        
        var buffer = new byte[4096];
        var receiveBuffer = new ArraySegment<byte>(buffer);
        
        try
        {
            while (_webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                
                do
                {
                    result = await _webSocket.ReceiveAsync(receiveBuffer, cancellationToken);
                    
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Fechamento recebido do servidor", 
                            CancellationToken.None);
                        break;
                    }
                    
                    ms.Write(receiveBuffer.Array!, receiveBuffer.Offset, result.Count);
                }
                while (!result.EndOfMessage);
                
                if (result.MessageType == WebSocketMessageType.Text && ms.Length > 0)
                {
                    ms.Seek(0, SeekOrigin.Begin);
                    var json = Encoding.UTF8.GetString(ms.ToArray());
                    
                    try
                    {
                        var messageObj = JsonSerializer.Deserialize<JsonElement>(json);
                        var messageType = messageObj.GetProperty("type").GetString();
                        
                        if (messageType == "metrics")
                        {
                            if (messageObj.TryGetProperty("data", out var dataElement))
                            {
                                var metrics = JsonSerializer.Deserialize<ServerMetrics>(dataElement.GetRawText());
                                if (metrics != null)
                                {
                                    foreach (var callback in _metricsCallbacks)
                                    {
                                        try
                                        {
                                            callback(metrics);
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"Erro em callback de métricas: {ex.ToString()}");
                                        }
                                    }
                                }
                            }
                            
                            if (messageObj.TryGetProperty("siteMetrics", out var siteMetricsElement))
                            {
                                var siteMetrics = JsonSerializer.Deserialize<List<SiteMetrics>>(siteMetricsElement.GetRawText());
                                if (siteMetrics != null)
                                {
                                    foreach (var callback in _siteMetricsCallbacks)
                                    {
                                        try
                                        {
                                            callback(siteMetrics);
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"Erro em callback de métricas de sites: {ex.ToString()}");
                                        }
                                    }
                                }
                            }
                        }
                        else if (messageType == "welcome")
                        {
                            if (messageObj.TryGetProperty("initialData", out var initialDataElement))
                            {
                                var metrics = JsonSerializer.Deserialize<ServerMetrics>(initialDataElement.GetRawText());
                                if (metrics != null)
                                {
                                    foreach (var callback in _metricsCallbacks)
                                    {
                                        try
                                        {
                                            callback(metrics);
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"Erro em callback de métricas iniciais: {ex.ToString()}");
                                        }
                                    }
                                }
                            }
                            
                            if (messageObj.TryGetProperty("siteMetrics", out var initialSiteMetricsElement))
                            {
                                var siteMetrics = JsonSerializer.Deserialize<List<SiteMetrics>>(initialSiteMetricsElement.GetRawText());
                                if (siteMetrics != null)
                                {
                                    foreach (var callback in _siteMetricsCallbacks)
                                    {
                                        try
                                        {
                                            callback(siteMetrics);
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"Erro em callback de métricas de sites iniciais: {ex.ToString()}");
                                        }
                                    }
                                }
                            }
                            
                            if (messageObj.TryGetProperty("refreshInterval", out var refreshIntervalElement))
                            {
                                var refreshInterval = refreshIntervalElement.GetInt32();
                                foreach (var callback in _refreshIntervalCallbacks)
                                {
                                    try
                                    {
                                        callback(refreshInterval);
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Erro em callback de intervalo de atualização: {ex.ToString()}");
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Erro ao processar mensagem do WebSocket: {ex.ToString()}");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Recebimento de mensagens do WebSocket cancelado");
        }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            Console.WriteLine("Conexão WebSocket fechada pelo servidor");
            lock (_connectionLock)
            {
                _isConnected = false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao receber mensagens do WebSocket: {ex.ToString()}");
            lock (_connectionLock)
            {
                _isConnected = false;
            }
        }
    }
    
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }
} 