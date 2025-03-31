namespace IisManagerWeb.Shared.Models;

/// <summary>
/// Representa o resultado de uma operação de serviço
/// </summary>
public class ServiceResult
{
    /// <summary>
    /// Indica se a operação foi bem-sucedida
    /// </summary>
    public bool Succeeded { get; set; }
    
    /// <summary>
    /// Lista de erros que ocorreram durante a operação, se houver
    /// </summary>
    public List<string>? Errors { get; set; }
} 