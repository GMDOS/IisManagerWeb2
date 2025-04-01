using System.Net.Http.Json;
using IisManagerWeb.Shared.Models;

namespace IisManagerWeb.Front.Services;

public class SiteService 
{
    private readonly HttpClient _httpClient;
    private const string ApiUrl = "http://localhost:5135/sites";

    public SiteService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        // Comentando esta linha pois o BaseAddress já está configurado no Program.cs
        // _httpClient.BaseAddress = new Uri("http://localhost:5135/");
    }

    public async Task<List<SiteDto>> GetSitesAsync()
    {
        
        return await _httpClient.GetFromJsonAsync<List<SiteDto>>("sites") ?? new List<SiteDto>();
    }

    public async Task<SiteDto> GetSiteAsync(string name)
    {
        return await _httpClient.GetFromJsonAsync<SiteDto>($"sites/{name}") ?? new SiteDto();
    }

    public async Task StartSiteAsync(string name)
    {
        var response = await _httpClient.PostAsync($"sites/{name}/start", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task StopSiteAsync(string name)
    {
        var response = await _httpClient.PostAsync($"sites/{name}/stop", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task RestartSiteAsync(string name)
    {
        var response = await _httpClient.PostAsync($"sites/{name}/restart", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task UpdateSiteFilesAsync(string name, byte[] zipContent)
    {
        var content = new ByteArrayContent(zipContent);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
        
        var response = await _httpClient.PostAsync($"sites/{name}/update-files", content);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<SiteGroupDto>> GetSiteGroupsAsync()
    {
        return await _httpClient.GetFromJsonAsync<List<SiteGroupDto>>("site-groups") ?? new List<SiteGroupDto>();
    }

    public async Task<SiteGroupDto> GetSiteGroupAsync(string name)
    {
        return await _httpClient.GetFromJsonAsync<SiteGroupDto>($"site-groups/{name}") ?? new SiteGroupDto();
    }

    public async Task<SiteGroupDto> CreateSiteGroupAsync(SiteGroupDto group)
    {
        var response = await _httpClient.PostAsJsonAsync("site-groups", group);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SiteGroupDto>() ?? new SiteGroupDto();
    }

    public async Task<SiteGroupDto> UpdateSiteGroupAsync(string name, SiteGroupDto group)
    {
        var response = await _httpClient.PutAsJsonAsync($"site-groups/{name}", group);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SiteGroupDto>() ?? new SiteGroupDto();
    }

    public async Task DeleteSiteGroupAsync(string name)
    {
        var response = await _httpClient.DeleteAsync($"site-groups/{name}");
        response.EnsureSuccessStatusCode();
    }

    public async Task StartSiteGroupAsync(string name)
    {
        var response = await _httpClient.PostAsync($"site-groups/{name}/start", null);
        response.EnsureSuccessStatusCode();
    }

    public async Task StopSiteGroupAsync(string name)
    {
        var response = await _httpClient.PostAsync($"site-groups/{name}/stop", null);
        response.EnsureSuccessStatusCode();
    }
} 