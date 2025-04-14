using IisManagerWeb.Shared.Models;

namespace IisManagerWeb.Front.Services;

/// <summary>
/// Interface para serviço de upload de arquivos
/// </summary>
public interface IUploadFileService
{
    /// <summary>
    /// Faz o upload de um arquivo para o site especificado
    /// </summary>
    Task<ServiceResult> UploadFileAsync(string siteName, Stream fileStream, string fileName);
}

/// <summary>
/// Implementação do serviço de upload de arquivos que utiliza o SiteService
/// </summary>
public class UploadFileService : IUploadFileService
{
    private readonly SiteService _siteService;

    public UploadFileService(SiteService siteService)
    {
        _siteService = siteService;
    }

    /// <summary>
    /// Faz o upload de um arquivo para o site especificado
    /// </summary>
    public async Task<ServiceResult> UploadFileAsync(string siteName, Stream fileStream, string fileName)
    {
        try
        {
            using var ms = new MemoryStream();
            await fileStream.CopyToAsync(ms);
            var fileBytes = ms.ToArray();
            
            await _siteService.UpdateSiteFilesAsync(siteName, fileBytes);
            
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