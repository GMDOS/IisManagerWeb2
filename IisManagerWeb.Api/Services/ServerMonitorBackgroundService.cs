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
        
        // Carregar configurações iniciais
        LoadSettingsFromFile();
        
        // Configurar um timer para recarregar configurações periodicamente
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
                    // Obter métricas atuais
                    var metrics = _monitorService.GetCurrentMetrics();
                    var siteMetrics = _monitorService.GetSiteMetrics();
                    
                    // Adicionar ao histórico
                    _monitorService.UpdateMetricsHistory(metrics);
                    
                    // Enviar para os clientes conectados
                    await _monitorService.BroadcastMetricsAsync(metrics, siteMetrics);
                    
                    // Aguardar pelo próximo intervalo com base nas configurações
                    await Task.Delay(TimeSpan.FromSeconds(_monitorService.GetRefreshInterval()), stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao coletar ou enviar métricas do servidor");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Exceção normal durante o encerramento, apenas registre
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
                    // Atualiza o intervalo de atualização
                    _monitorService.UpdateRefreshInterval(settings.RefreshInterval);
                    _logger.LogInformation("Configurações carregadas com sucesso. Intervalo de atualização: {RefreshInterval} segundos",
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