using System.Net.Http.Json;
using IisManagerWeb.Shared.Models;

namespace IisManagerWeb.Front.Services;

public class SettingsService
{
    private readonly HttpClient _httpClient;

    public SettingsService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Obtém as configurações atuais do gerenciador
    /// </summary>
    public async Task<ManagerSettings> GetSettingsAsync()
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<ManagerSettings>("settings") ?? new ManagerSettings();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao obter configurações: {ex.ToString()}");
            return new ManagerSettings();
        }
    }

    /// <summary>
    /// Salva as configurações do gerenciador
    /// </summary>
    public async Task<ManagerSettings> SaveSettingsAsync(ManagerSettings settings)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("settings", settings);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<ManagerSettings>() ?? new ManagerSettings();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao salvar configurações: {ex.ToString()}");
            throw;
        }
    }

    /// <summary>
    /// Atualiza apenas as configurações de arquivos ignorados
    /// </summary>
    public async Task<ManagerSettings> UpdateIgnoredFilesAsync(List<string> ignoredFiles)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("settings/ignored-files", ignoredFiles);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<ManagerSettings>() ?? new ManagerSettings();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao atualizar arquivos ignorados: {ex.ToString()}");
            throw;
        }
    }
} 