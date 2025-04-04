using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using IisManagerWeb.Api.Services;
using IisManagerWeb.Shared;
using IisManagerWeb.Shared.Models;

namespace IisManagerWeb.Api.Controllers;

public static class MetricsController
{
    public static void GetMetricsRoutes(this WebApplication app)
    {
        var metricsApi = app.MapGroup("/metrics");
        
        // Obter métricas atuais
        metricsApi.MapGet("/current", (ServerMonitorService monitorService) =>
        {
            var metrics = monitorService.GetCurrentMetrics();
            return Results.Ok(metrics);
        });
        
        // Obter métricas de sites
        metricsApi.MapGet("/sites", (ServerMonitorService monitorService) =>
        {
            var metrics = monitorService.GetSiteMetrics();
            return Results.Ok(metrics);
        });
        
        // Obter histórico de métricas
        metricsApi.MapGet("/history", (ServerMonitorService monitorService) =>
        {
            var history = monitorService.GetMetricsHistory();
            return Results.Ok(history);
        });
        
        // WebSocket para métricas em tempo real
        app.Use(async (context, next) =>
        {
            if (context.Request.Path == "/metrics/ws")
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    var monitorService = app.Services.GetRequiredService<ServerMonitorService>();
                    var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                    var clientId = Guid.NewGuid().ToString();
                    
                    monitorService.AddClient(clientId, webSocket);
                    
                    // Enviar mensagem inicial
                    var initialMetrics = monitorService.GetCurrentMetrics();
                    var initialSiteMetrics = monitorService.GetSiteMetrics();
                    
                    var welcomePacket = new MetricsWebSocketPacket
                    {
                        Type = "welcome",
                        Data = initialMetrics,
                        SiteMetrics = initialSiteMetrics,
                        Message = "Conexão estabelecida com o servidor",
                        RefreshInterval = monitorService.GetRefreshInterval()
                    };
                    
                    var json = JsonSerializer.Serialize(welcomePacket, AppJsonSerializerContext.Default.MetricsWebSocketPacket);
                    var bytes = Encoding.UTF8.GetBytes(json);
                    var buffer = new ArraySegment<byte>(bytes);
                    
                    await webSocket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
                    
                    // Manter a conexão aberta
                    try
                    {
                        var buffer2 = new byte[1024 * 4];
                        var receiveBuffer = new ArraySegment<byte>(buffer2);
                        
                        while (webSocket.State == WebSocketState.Open)
                        {
                            // Receber mensagens do cliente (ping/pong ou comandos)
                            var result = await webSocket.ReceiveAsync(receiveBuffer, CancellationToken.None);
                            
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Fechamento solicitado pelo cliente", CancellationToken.None);
                                break;
                            }
                            
                            if (result.MessageType == WebSocketMessageType.Text)
                            {
                                var message = Encoding.UTF8.GetString(receiveBuffer.Array!, 0, result.Count);
                                Console.WriteLine($"Mensagem recebida do cliente {clientId}: {message}");
                                
                            }
                        }
                    }
                    catch (WebSocketException ex)
                    {
                        Console.WriteLine($"Erro de WebSocket: {ex.Message}");
                    }
                    finally
                    {
                        monitorService.RemoveClient(clientId);
                        Console.WriteLine($"Cliente {clientId} desconectado");
                    }
                }
                else
                {
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                }
            }
            else
            {
                await next();
            }
        });
    }
} 