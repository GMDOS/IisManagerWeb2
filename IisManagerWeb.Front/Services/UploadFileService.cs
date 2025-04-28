using IisManagerWeb.Shared.Models;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace IisManagerWeb.Front.Services;

/// <summary>
/// Implementação do serviço de upload de arquivos
/// </summary>
public class UploadFileService
{
    private readonly SiteService _siteService;
    private readonly HttpClient _httpClient;
    private const int ChunkSize = 1024 * 1024; // 1 MB

    public UploadFileService(SiteService siteService, HttpClient httpClient)
    {
        _siteService = siteService;
        _httpClient = httpClient;
    }
    
    /// <summary>
    /// Inicia um novo processo de upload para um site único
    /// </summary>
    public async Task<ServiceResult<string>> IniciarUploadAsync(string siteName)
    {
        try
        {
            var response = await _httpClient.PostAsync($"uploads/iniciar/{siteName}", null);
            response.EnsureSuccessStatusCode();
            
            var jsonContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Resposta da API: {jsonContent}");
            
            try
            {
                var uploadResponse = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
                if (uploadResponse != null && uploadResponse.TryGetValue("uploadId", out var uploadId) && 
                    !string.IsNullOrEmpty(uploadId))
                {
                    return new ServiceResult<string> 
                    { 
                        Succeeded = true, 
                        Data = uploadId 
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao deserializar a resposta: {ex.ToString()}");
            }
            
            return new ServiceResult<string> 
            { 
                Succeeded = false, 
                Errors = new List<string> { "Não foi possível obter o ID de upload da resposta" } 
            };
        }
        catch (Exception ex)
        {
            return new ServiceResult<string> 
            { 
                Succeeded = false, 
                Errors = new List<string> { ex.ToString() } 
            };
        }
    }
    
    /// <summary>
    /// Inicia um novo processo de upload para um grupo de sites
    /// </summary>
    public async Task<ServiceResult<string>> IniciarUploadGrupoAsync(string groupName)
    {
        try
        {
            var response = await _httpClient.PostAsync($"uploads/grupo/iniciar/{groupName}", null);
            response.EnsureSuccessStatusCode();
            
            var jsonContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Resposta da API para iniciar upload do grupo: {jsonContent}");
            
            try
            {
                var uploadResponse = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
                if (uploadResponse != null && uploadResponse.TryGetValue("uploadId", out var uploadId) && 
                    !string.IsNullOrEmpty(uploadId))
                {
                    return new ServiceResult<string> 
                    { 
                        Succeeded = true, 
                        Data = uploadId 
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao deserializar a resposta: {ex.ToString()}");
            }
            
            return new ServiceResult<string> 
            { 
                Succeeded = false, 
                Errors = new List<string> { "Não foi possível obter o ID de upload da resposta" } 
            };
        }
        catch (Exception ex)
        {
            return new ServiceResult<string> 
            { 
                Succeeded = false, 
                Errors = new List<string> { ex.ToString() } 
            };
        }
    }
    
    /// <summary>
    /// Envia um arquivo em chunks para o upload especificado (site único ou grupo)
    /// </summary>
    public async Task<ServiceResult> EnviarArquivoEmChunksAsync(
        string uploadId, 
        Stream fileStream, 
        string fileName, 
        string targetName,
        Action<long, long>? progressCallback = null,
        DateTime? lastModified = null,
        bool isGroup = false)
    {
        try
        {
            fileStream.Position = 0;
            var buffer = new byte[ChunkSize];
            int bytesRead;
            int chunkIndex = 0;
            long totalBytes = fileStream.Length;
            long bytesProcessed = 0;
            
            while ((bytesRead = await fileStream.ReadAsync(buffer)) > 0)
            {
                using var chunkStream = new MemoryStream(buffer, 0, bytesRead);
                using var content = new MultipartFormDataContent();
                using var streamContent = new StreamContent(chunkStream);
                
                content.Add(streamContent, "file", fileName);
                content.Add(new StringContent(chunkIndex.ToString()), "chunkIndex");
                content.Add(new StringContent(totalBytes.ToString()), "totalSize");
                
                if (lastModified.HasValue)
                {
                    content.Add(new StringContent(lastModified.Value.ToString("o")), "lastModified");
                }
                
                string endpoint = isGroup 
                    ? $"uploads/{uploadId}/arquivo/grupo/{targetName}"
                    : $"uploads/{uploadId}/arquivo/{targetName}";
                
                var response = await _httpClient.PostAsync(endpoint, content);
                response.EnsureSuccessStatusCode();
                
                bytesProcessed += bytesRead;

                progressCallback?.Invoke(bytesProcessed, totalBytes);
                
                chunkIndex++;
            }
            
            progressCallback?.Invoke(totalBytes, totalBytes);
            
            return new ServiceResult { Succeeded = true };
        }
        catch (Exception ex)
        {
            return new ServiceResult 
            { 
                Succeeded = false, 
                Errors = new List<string> { ex.ToString() } 
            };
        }
    }
    
    /// <summary>
    /// Finaliza o processo de upload e atualiza o site
    /// </summary>
    public async Task<ServiceResult> FinalizarUploadAsync(string uploadId, string siteName)
    {
        try
        {
            var response = await _httpClient.PostAsync($"uploads/{uploadId}/finalizar/{siteName}", null);
            response.EnsureSuccessStatusCode();
            
            return new ServiceResult { Succeeded = true };
        }
        catch (Exception ex)
        {
            return new ServiceResult 
            { 
                Succeeded = false, 
                Errors = new List<string> { ex.ToString() } 
            };
        }
    }
    
    /// <summary>
    /// Finaliza o processo de upload de um site específico dentro de um grupo
    /// </summary>
    public async Task<ServiceResult> FinalizarUploadSiteGrupoAsync(string uploadId, string groupName, string siteName)
    {
        try
        {
            var response = await _httpClient.PostAsync($"uploads/{uploadId}/finalizar/grupo/{groupName}/site/{siteName}", null);
            response.EnsureSuccessStatusCode();
            
            return new ServiceResult { Succeeded = true };
        }
        catch (Exception ex)
        {
            return new ServiceResult 
            { 
                Succeeded = false, 
                Errors = new List<string> { ex.ToString() } 
            };
        }
    }
    
    /// <summary>
    /// Finaliza o processo de upload para um grupo inteiro e limpa os arquivos temporários
    /// </summary>
    public async Task<ServiceResult> FinalizarUploadGrupoAsync(string uploadId, string groupName)
    {
        try
        {
            var response = await _httpClient.PostAsync($"uploads/{uploadId}/finalizar/grupo/{groupName}", null);
            response.EnsureSuccessStatusCode();
            
            return new ServiceResult { Succeeded = true };
        }
        catch (Exception ex)
        {
            return new ServiceResult 
            { 
                Succeeded = false, 
                Errors = new List<string> { ex.ToString() } 
            };
        }
    }
}

/// <summary>
/// Classe de resultado genérico
/// </summary>
public class ServiceResult<T> : ServiceResult
{
    public T? Data { get; set; }
}

/// <summary>
/// Classe base de resultado
/// </summary>
public class ServiceResult
{
    public bool Succeeded { get; set; }
    public List<string> Errors { get; set; } = new();
}

