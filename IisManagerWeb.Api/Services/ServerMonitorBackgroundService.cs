using System.Text.Json;
using IisManagerWeb.Shared.Models;

namespace IisManagerWeb.Api.Services;

/// <summary>
/// Serviço de fundo para monitorar e coletar métricas do servidor periodicamente
/// </summary>
public class ServerMonitorBackgroundService : BackgroundService
{
    private readonly ServerMonitorService _monitorService;
    private readonly ILogger<ServerMonitorBackgroundService> _logger;
    private readonly string _settingsFilePath;
    private Timer? _configTimer;
    
    public ServerMonitorBackgroundService(
        ServerMonitorService monitorService,
        ILogger<ServerMonitorBackgroundService> logger)
    {
        _monitorService = monitorService;
        _logger = logger;
        _settingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ManagerSettings.json");
        
        LoadSettingsFromFile();
        
            _configTimer = new Timer(_ => LoadSettingsFromFile(), null, TimeSpan.Zero, TimeSpan.FromSeconds(15));
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Serviço de monitoramento do servidor iniciado");
        
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var metrics = _monitorService.GetCurrentMetrics();
                    var siteMetrics = _monitorService.GetSiteMetrics();
                    
                    _monitorService.UpdateMetricsHistory(metrics);
                    
                    await _monitorService.BroadcastMetricsAsync(metrics, siteMetrics);
                    
                    await Task.Delay(_monitorService.GetRefreshInterval(), stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao coletar ou enviar métricas do servidor");
                    await Task.Delay(5000, stoppingToken); // 5000ms = 5 segundos
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Serviço de monitoramento do servidor está sendo encerrado");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Erro crítico no serviço de monitoramento do servidor");
            throw;
        }
        finally
        {
            _logger.LogInformation("Serviço de monitoramento do servidor foi encerrado");
        }
    }
    
    public override void Dispose()
    {
        _configTimer?.Dispose();
        base.Dispose();
    }
    
    /// <summary>
    /// Carrega as configurações do arquivo e atualiza o serviço
    /// </summary>
    private void LoadSettingsFromFile()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var settings = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.ManagerSettings);
                
                if (settings != null)
                {
                    _monitorService.UpdateRefreshInterval(settings.RefreshInterval);
                    _logger.LogInformation("Configurações carregadas com sucesso. Intervalo de atualização: {RefreshInterval} milissegundos",
                        settings.RefreshInterval);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao carregar configurações do arquivo");
        }
    }
} 