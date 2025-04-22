using IisManagerWeb.Shared.Models;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace IisManagerWeb.Front.Services;

/// <summary>
/// Interface para serviço de upload de arquivos
/// </summary>
public interface IUploadFileService
{
    /// <summary>
    /// Inicia um novo processo de upload
    /// </summary>
    Task<ServiceResult<string>> IniciarUploadAsync();
    
    /// <summary>
    /// Envia um arquivo para o processo de upload
    /// </summary>
    Task<ServiceResult> EnviarArquivoAsync(string uploadId, Stream fileStream, string fileName, DateTime? lastModified = null);
    
    /// <summary>
    /// Envia um arquivo grande em chunks para o processo de upload
    /// </summary>
    Task<ServiceResult> EnviarArquivoEmChunksAsync(string uploadId, Stream fileStream, string fileName, 
        Action<long, long> progressCallback = null, DateTime? lastModified = null);
    
    /// <summary>
    /// Finaliza o processo de upload e atualiza o site
    /// </summary>
    Task<ServiceResult> FinalizarUploadAsync(string uploadId, string siteName);
}

/// <summary>
/// Implementação do serviço de upload de arquivos
/// </summary>
public class UploadFileService : IUploadFileService
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
    /// Inicia um novo processo de upload
    /// </summary>
    public async Task<ServiceResult<string>> IniciarUploadAsync()
    {
        try
        {
            var response = await _httpClient.PostAsync("uploads/iniciar", null);
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
                Console.WriteLine($"Erro na deserialização para Dictionary: {ex.Message}");
                
                try
                {
                    using var document = System.Text.Json.JsonDocument.Parse(jsonContent);
                    if (document.RootElement.TryGetProperty("uploadId", out var uploadIdElement))
                    {
                        var uploadId = uploadIdElement.GetString();
                        if (!string.IsNullOrEmpty(uploadId))
                        {
                            return new ServiceResult<string> 
                            { 
                                Succeeded = true, 
                                Data = uploadId 
                            };
                        }
                    }
                }
                catch (Exception jsonEx)
                {
                    Console.WriteLine($"Erro na deserialização usando JsonDocument: {jsonEx.Message}");
                }
            }
            
            return new ServiceResult<string> 
            { 
                Succeeded = false, 
                Errors = new List<string> { $"Não foi possível obter o ID do upload. Resposta: {jsonContent}" } 
            };
        }
        catch (Exception ex)
        {
            return new ServiceResult<string> 
            { 
                Succeeded = false, 
                Errors = new List<string> { ex.Message } 
            };
        }
    }
    
    /// <summary>
    /// Envia um arquivo para o processo de upload
    /// </summary>
    public async Task<ServiceResult> EnviarArquivoAsync(string uploadId, Stream fileStream, string fileName, DateTime? lastModified = null)
    {
        try
        {
            if (fileStream.Length > ChunkSize)
            {
                return await EnviarArquivoEmChunksAsync(uploadId, fileStream, fileName, null, lastModified);
            }
            
            using var content = new MultipartFormDataContent();
            using var streamContent = new StreamContent(fileStream);
            
            content.Add(streamContent, "file", fileName);
            
            if (lastModified.HasValue)
            {
                content.Add(new StringContent(lastModified.Value.ToString("o")), "lastModified");
            }
            
            var response = await _httpClient.PostAsync($"uploads/{uploadId}/arquivo", content);
            response.EnsureSuccessStatusCode();
            
            return new ServiceResult { Succeeded = true };
        }
        catch (Exception ex)
        {
            return new ServiceResult 
            { 
                Succeeded = false, 
                Errors = new List<string> { ex.Message } 
            };
        }
    }
    
    /// <summary>
    /// Envia um arquivo grande em chunks para o processo de upload
    /// </summary>
    public async Task<ServiceResult> EnviarArquivoEmChunksAsync(string uploadId, Stream fileStream, string fileName, 
        Action<long, long> progressCallback = null, DateTime? lastModified = null)
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
                
                var response = await _httpClient.PostAsync($"uploads/{uploadId}/arquivo", content);
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
                Errors = new List<string> { ex.Message } 
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
                Errors = new List<string> { ex.Message } 
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

