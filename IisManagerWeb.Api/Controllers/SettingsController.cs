using System.Text.Json;
using IisManagerWeb.Shared.Models;

namespace IisManagerWeb.Api.Controllers;

public static class SettingsController
{
    private static readonly string SettingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ManagerSettings.json");

    public static void GetSettingsRoutes(this WebApplication app)
    {
        var settingsApi = app.MapGroup("/settings");

        settingsApi.MapGet("/", () =>
        {
            try
            {
                if (!File.Exists(SettingsFilePath))
                {
                    var defaultSettings = new ManagerSettings();
                    var defaultJson = JsonSerializer.Serialize(defaultSettings, AppJsonSerializerContext.Default.ManagerSettings);
                    File.WriteAllText(SettingsFilePath, defaultJson);
                    return Results.Ok(defaultSettings);
                }

                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.ManagerSettings);
                return Results.Ok(settings);
            }
            catch (Exception ex)
            {
                return Results.BadRequest($"Erro ao obter configurações: {ex.Message}");
            }
        });

        settingsApi.MapPost("/", (ManagerSettings settings) =>
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, AppJsonSerializerContext.Default.ManagerSettings);
                File.WriteAllText(SettingsFilePath, json);
                return Results.Ok(settings);
            }
            catch (Exception ex)
            {
                return Results.BadRequest($"Erro ao salvar configurações: {ex.Message}");
            }
        });

        settingsApi.MapPost("/ignored-files", (List<string> ignoredFiles) =>
        {
            try
            {
                if (!File.Exists(SettingsFilePath))
                {
                    var defaultSettings = new ManagerSettings { IgnoredFiles = ignoredFiles };
                    var defaultJson = JsonSerializer.Serialize(defaultSettings, AppJsonSerializerContext.Default.ManagerSettings);
                    File.WriteAllText(SettingsFilePath, defaultJson);
                    return Results.Ok(defaultSettings);
                }

                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.ManagerSettings);
                
                if (settings != null)
                {
                    settings.IgnoredFiles = ignoredFiles;
                    var updatedJson = JsonSerializer.Serialize(settings, AppJsonSerializerContext.Default.ManagerSettings);
                    File.WriteAllText(SettingsFilePath, updatedJson);
                }
                
                return Results.Ok(settings);
            }
            catch (Exception ex)
            {
                return Results.BadRequest($"Erro ao atualizar arquivos ignorados: {ex.Message}");
            }
        });
    }
} 