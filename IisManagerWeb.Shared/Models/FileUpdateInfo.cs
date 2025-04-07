namespace IisManagerWeb.Shared.Models;

/// <summary>
/// Representa um arquivo que precisa ser atualizado no servidor
/// </summary>
public class FileUpdateInfo
{
    /// <summary>
    /// Caminho relativo do arquivo
    /// </summary>
    public string RelativePath { get; set; } = string.Empty;
    
    /// <summary>
    /// Informações do arquivo no cliente
    /// </summary>
    public ClientFileInfo? ClientFile { get; set; }
    
    /// <summary>
    /// Informações do arquivo no servidor (null se não existir)
    /// </summary>
    public ServerFileInfo? ServerFile { get; set; }
    
    /// <summary>
    /// Motivo pelo qual o arquivo será atualizado
    /// </summary>
    public FileUpdateReason UpdateReason { get; set; }
    
    /// <summary>
    /// Indica se o arquivo deve ser ignorado
    /// </summary>
    public bool IsIgnored { get; set; }
    
    /// <summary>
    /// Motivo pelo qual o arquivo foi ignorado (se aplicável)
    /// </summary>
    public string IgnoreReason { get; set; } = string.Empty;
}

/// <summary>
/// Representa informações sobre um arquivo no servidor
/// </summary>
public class ServerFileInfo
{
    /// <summary>
    /// Caminho relativo do arquivo
    /// </summary>
    public string RelativePath { get; set; } = string.Empty;
    
    /// <summary>
    /// Nome do arquivo
    /// </summary>
    public string FileName { get; set; } = string.Empty;
    
    /// <summary>
    /// Tamanho do arquivo em bytes
    /// </summary>
    public long Size { get; set; }
    
    /// <summary>
    /// Data da última modificação do arquivo
    /// </summary>
    public DateTime LastModified { get; set; }
    
    /// <summary>
    /// Indica se o item é um diretório
    /// </summary>
    public bool IsDirectory { get; set; }
}

/// <summary>
/// Enumera os motivos pelos quais um arquivo precisa ser atualizado
/// </summary>
public enum FileUpdateReason
{
    /// <summary>
    /// O arquivo não existe no servidor
    /// </summary>
    FileNotExistsOnServer,
    
    /// <summary>
    /// O arquivo no cliente é mais recente do que no servidor
    /// </summary>
    modifiedDateDifferent,
    
    /// <summary>
    /// O arquivo no cliente tem tamanho diferente do arquivo no servidor
    /// </summary>
    DifferentSize,
    
    /// <summary>
    /// O arquivo é ignorado e não será atualizado
    /// </summary>
    Ignored
}

/// <summary>
/// Resposta da verificação de arquivos
/// </summary>
public class FileCheckResponse
{
    /// <summary>
    /// Lista de todos os arquivos analisados
    /// </summary>
    public List<FileUpdateInfo> Files { get; set; } = new();
    
    /// <summary>
    /// Lista apenas dos arquivos que serão atualizados
    /// </summary>
    public List<string> FilesToUpdate { get; set; } = new();
    
    /// <summary>
    /// Lista de arquivos ignorados
    /// </summary>
    public List<FileUpdateInfo> IgnoredFiles { get; set; } = new();
} 