using System.Text.Json.Serialization;

namespace IisManagerWeb.Shared.Models;

public class ManagerSettings
{
    [JsonPropertyName("organizationName")]
    public string OrganizationName { get; set; } = "Minha Empresa";
    
    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "light";
    
    [JsonPropertyName("language")]
    public string Language { get; set; } = "pt-BR";
    
    [JsonPropertyName("enableNotifications")]
    public bool EnableNotifications { get; set; } = true;
    
    [JsonPropertyName("refreshInterval")]
    public int RefreshInterval { get; set; } = 30;
    
    [JsonPropertyName("connectionLimit")]
    public int ConnectionLimit { get; set; } = 100;
    
    [JsonPropertyName("serverUrl")]
    public string ServerUrl { get; set; } = "https://localhost:5001";
    
    [JsonPropertyName("enableLogging")]
    public bool EnableLogging { get; set; } = true;
    
    [JsonPropertyName("enableSSL")]
    public bool EnableSSL { get; set; } = true;
    
    [JsonPropertyName("ignoredFiles")]
    public List<string> IgnoredFiles { get; set; } = new List<string>();
} 