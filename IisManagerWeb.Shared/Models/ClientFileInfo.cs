namespace IisManagerWeb.Shared.Models;

/// <summary>
/// Representa informações sobre um arquivo no cliente.
/// </summary>
public class ClientFileInfo
{
    /// <summary>
    /// Caminho relativo do arquivo (a partir da pasta raiz selecionada)
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