using Microsoft.AspNetCore.Mvc;
using Microsoft.Web.Administration;
using System.IO;
using System.IO.Compression;
using IisManagerWeb.Shared.Models;

namespace IisManagerWeb.Api.Controllers;

public static class UploadController
{
    private static readonly Dictionary<string, Dictionary<string, DateTime>> FileLastModifiedTimes = new();
    private static readonly Dictionary<string, string> UploadPaths = new();

    public static void GetUploadRoutes(this WebApplication app)
    {
        var uploadApi = app.MapGroup("/uploads");

        uploadApi.MapPost("/iniciar/{siteName}", (string siteName) =>
        {
            try
            {
                using var serverManager = new ServerManager();
                var site = serverManager.Sites[siteName];
                
                if (site == null)
                {
                    return Results.NotFound($"Site '{siteName}' não encontrado");
                }

                var physicalPath = site.Applications["/"].VirtualDirectories["/"].PhysicalPath;
                if (string.IsNullOrEmpty(physicalPath))
                {
                    return Results.BadRequest("Caminho físico do site não encontrado");
                }

                var tempUploadsDir = Path.Combine(physicalPath, "__temp_uploads__");
                if (Directory.Exists(tempUploadsDir))
                {
                    try
                    {
                        Directory.Delete(tempUploadsDir, true);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Erro ao remover diretório temporário existente: {ex.ToString()}");
                    }
                }

                var uploadId = Guid.NewGuid().ToString();
                var uploadPath = Path.Combine(physicalPath, "__temp_uploads__", uploadId);
                
                if (!Directory.Exists(uploadPath))
                {
                    Directory.CreateDirectory(uploadPath);
                }
                
                UploadPaths[uploadId] = uploadPath;
                FileLastModifiedTimes[uploadId] = new Dictionary<string, DateTime>();

                return Results.Ok(new { UploadId = uploadId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest($"Erro ao iniciar o upload: {ex.ToString()}");
            }
        });

        uploadApi.MapPost("/{uploadId}/arquivo/{siteName}", async (string uploadId, string siteName, HttpRequest request) =>
        {
            try
            {
                if (!UploadPaths.TryGetValue(uploadId, out var uploadPath) || !Directory.Exists(uploadPath))
                {
                    return Results.BadRequest("ID de upload inválido ou expirado");
                }

                if (!request.HasFormContentType || request.Form.Files.Count == 0)
                {
                    return Results.BadRequest("Nenhum arquivo foi enviado");
                }

                var file = request.Form.Files[0];
                var chunkIndex = int.Parse(request.Form["chunkIndex"]);
                var totalSize = long.Parse(request.Form["totalSize"]);
                var fileName = file.FileName?.Trim('"') ?? "unknown";
                
                var filePath = Path.Combine(uploadPath, fileName);
                var chunkPath = Path.Combine(uploadPath, $"{fileName}.part_{chunkIndex}");
                
                // Garantir que o diretório pai do chunk exista
                var chunkDirectory = Path.GetDirectoryName(chunkPath);
                if (!Directory.Exists(chunkDirectory) && chunkDirectory != null)
                {
                    Directory.CreateDirectory(chunkDirectory);
                }
                
                // Salvar informação de última modificação, se fornecida
                if (request.Form.ContainsKey("lastModified") && !string.IsNullOrEmpty(request.Form["lastModified"]))
                {
                    var lastModified = DateTime.Parse(request.Form["lastModified"]);
                    FileLastModifiedTimes[uploadId][fileName] = lastModified;
                }
                
                using (var stream = new FileStream(chunkPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }
                
                return Results.Ok(new { Message = $"Chunk {chunkIndex} recebido com sucesso" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao receber arquivo: {ex}");
                return Results.BadRequest($"Erro ao processar o arquivo: {ex}");
            }
        });

        uploadApi.MapPost("/{uploadId}/finalizar/{siteName}", (string uploadId, string siteName) =>
        {
            try
            {
                if (!UploadPaths.TryGetValue(uploadId, out var uploadPath) || !Directory.Exists(uploadPath))
                {
                    return Results.BadRequest("ID de upload inválido ou expirado");
                }
                
                using var serverManager = new ServerManager();
                var site = serverManager.Sites[siteName];
                
                if (site == null)
                {
                    return Results.NotFound($"Site '{siteName}' não encontrado");
                }

                var physicalPath = site.Applications["/"].VirtualDirectories["/"].PhysicalPath;
                if (string.IsNullOrEmpty(physicalPath))
                {
                    return Results.BadRequest("Caminho físico do site não encontrado");
                }

                ProcessPendingChunks(uploadPath);

                var backupFileName = CreateSiteBackup(siteName, physicalPath);
                
                var siteState = site.State;
                if (siteState == ObjectState.Started)
                {
                    site.Stop();
                }
                
                var appPoolName = site.Applications["/"].ApplicationPoolName;
                var appPool = serverManager.ApplicationPools[appPoolName];
                var appPoolState = appPool.State;
                
                if (appPoolState == ObjectState.Started)
                {
                    appPool.Stop();
                }
                
                MoveFiles(uploadPath, physicalPath);
                SetDates(uploadPath, physicalPath, uploadId);
                
                if (appPoolState == ObjectState.Started)
                {
                    appPool.Start();
                }
                
                if (siteState == ObjectState.Started)
                {
                    site.Start();
                }
                
                if (FileLastModifiedTimes.ContainsKey(uploadId))
                {
                    FileLastModifiedTimes.Remove(uploadId);
                }
                
                if (UploadPaths.ContainsKey(uploadId))
                {
                    UploadPaths.Remove(uploadId);
                }
                
                try 
                {
                    Directory.Delete(uploadPath, true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erro ao excluir diretório temporário: {ex.ToString()}");
                }

                return Results.Ok(new { 
                    Mensagem = $"Site '{siteName}' atualizado com sucesso",
                    BackupFile = backupFileName
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest($"Erro ao finalizar upload: {ex.ToString()}");
            }
        });
        
        // Novas rotas para upload de grupo de sites
        uploadApi.MapPost("/grupo/iniciar/{groupName}", async (string groupName, [FromServices] GroupService groupService) =>
        {
            try
            {
                var group = await groupService.GetGroupAsync(groupName);
                if (group == null || group.SiteNames.Count == 0)
                {
                    return Results.NotFound($"Grupo '{groupName}' não encontrado ou não contém sites");
                }
                
                // Usa o primeiro site do grupo como base para o upload
                var firstSiteName = group.SiteNames[0];
                
                using var serverManager = new ServerManager();
                var site = serverManager.Sites[firstSiteName];
                
                if (site == null)
                {
                    return Results.NotFound($"Site '{firstSiteName}' do grupo não encontrado");
                }

                var physicalPath = site.Applications["/"].VirtualDirectories["/"].PhysicalPath;
                if (string.IsNullOrEmpty(physicalPath))
                {
                    return Results.BadRequest("Caminho físico do site não encontrado");
                }

                var tempUploadsDir = Path.Combine(physicalPath, "__temp_uploads_group__");
                if (Directory.Exists(tempUploadsDir))
                {
                    try
                    {
                        Directory.Delete(tempUploadsDir, true);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Erro ao remover diretório temporário existente: {ex.ToString()}");
                    }
                }

                var uploadId = Guid.NewGuid().ToString();
                var uploadPath = Path.Combine(physicalPath, "__temp_uploads_group__", uploadId);
                
                if (!Directory.Exists(uploadPath))
                {
                    Directory.CreateDirectory(uploadPath);
                }
                
                UploadPaths[uploadId] = uploadPath;
                FileLastModifiedTimes[uploadId] = new Dictionary<string, DateTime>();

                return Results.Ok(new { UploadId = uploadId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest($"Erro ao iniciar o upload do grupo: {ex.ToString()}");
            }
        });
        
        uploadApi.MapPost("/{uploadId}/arquivo/grupo/{groupName}", async (string uploadId, string groupName, HttpRequest request) =>
        {
            try
            {
                if (!UploadPaths.TryGetValue(uploadId, out var uploadPath) || !Directory.Exists(uploadPath))
                {
                    return Results.BadRequest("ID de upload inválido ou expirado");
                }

                if (!request.HasFormContentType || request.Form.Files.Count == 0)
                {
                    return Results.BadRequest("Nenhum arquivo foi enviado");
                }

                var file = request.Form.Files[0];
                var chunkIndex = int.Parse(request.Form["chunkIndex"]);
                var totalSize = long.Parse(request.Form["totalSize"]);
                var fileName = file.FileName?.Trim('"') ?? "unknown";
                
                var filePath = Path.Combine(uploadPath, fileName);
                var chunkPath = Path.Combine(uploadPath, $"{fileName}.part_{chunkIndex}");
                
                // Garantir que o diretório pai do chunk exista
                var chunkDirectory = Path.GetDirectoryName(chunkPath);
                if (!Directory.Exists(chunkDirectory) && chunkDirectory != null)
                {
                    Directory.CreateDirectory(chunkDirectory);
                }
                
                // Salvar informação de última modificação, se fornecida
                if (request.Form.ContainsKey("lastModified") && !string.IsNullOrEmpty(request.Form["lastModified"]))
                {
                    var lastModified = DateTime.Parse(request.Form["lastModified"]);
                    FileLastModifiedTimes[uploadId][fileName] = lastModified;
                }
                
                using (var stream = new FileStream(chunkPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }
                
                return Results.Ok(new { Message = $"Chunk {chunkIndex} recebido com sucesso para o grupo" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao receber arquivo para o grupo: {ex.ToString()}");
                return Results.BadRequest($"Erro ao processar o arquivo para o grupo: {ex.ToString()}");
            }
        });
        
        uploadApi.MapPost("/{uploadId}/finalizar/grupo/{groupName}/site/{siteName}", (string uploadId, string groupName, string siteName) =>
        {
            try
            {
                if (!UploadPaths.TryGetValue(uploadId, out var uploadPath) || !Directory.Exists(uploadPath))
                {
                    return Results.BadRequest("ID de upload inválido ou expirado");
                }
                
                using var serverManager = new ServerManager();
                var site = serverManager.Sites[siteName];
                
                if (site == null)
                {
                    return Results.NotFound($"Site '{siteName}' não encontrado");
                }

                var physicalPath = site.Applications["/"].VirtualDirectories["/"].PhysicalPath;
                if (string.IsNullOrEmpty(physicalPath))
                {
                    return Results.BadRequest("Caminho físico do site não encontrado");
                }

                // Sempre processar os chunks, mesmo que a verificação inicial não os encontre
                Console.WriteLine($"Verificando chunks pendentes para o grupo: {groupName}, site: {siteName}");
                ProcessPendingChunks(uploadPath);

                var backupFileName = CreateSiteBackup(siteName, physicalPath);
                
                var siteState = site.State;
                if (siteState == ObjectState.Started)
                {
                    site.Stop();
                }
                
                var appPoolName = site.Applications["/"].ApplicationPoolName;
                var appPool = serverManager.ApplicationPools[appPoolName];
                var appPoolState = appPool.State;
                
                if (appPoolState == ObjectState.Started)
                {
                    appPool.Stop();
                }
                
                // Para grupos, copiamos os arquivos em vez de movê-los
                CopyFiles(uploadPath, physicalPath);
                SetDates(uploadPath, physicalPath, uploadId);
                
                if (appPoolState == ObjectState.Stopped)
                {
                    appPool.Start();
                }
                
                if (siteState == ObjectState.Stopped)
                {
                    site.Start();
                }

                return Results.Ok(new { 
                    Mensagem = $"Site '{siteName}' do grupo '{groupName}' atualizado com sucesso",
                    BackupFile = backupFileName
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao finalizar upload do site no grupo: {ex.ToString()}");
                return Results.BadRequest($"Erro ao finalizar o upload do site no grupo: {ex.ToString()}");
            }
        });
        
        uploadApi.MapPost("/{uploadId}/finalizar/grupo/{groupName}", (string uploadId, string groupName) =>
        {
            try
            {
                if (!UploadPaths.TryGetValue(uploadId, out var uploadPath) || !Directory.Exists(uploadPath))
                {
                    return Results.BadRequest("ID de upload inválido ou expirado");
                }
                
                // Limpar recursos
                if (FileLastModifiedTimes.ContainsKey(uploadId))
                {
                    FileLastModifiedTimes.Remove(uploadId);
                }
                
                try 
                {
                    Directory.Delete(uploadPath, true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erro ao excluir diretório temporário do grupo: {ex.ToString()}");
                }
                
                UploadPaths.Remove(uploadId);

                return Results.Ok(new { 
                    Mensagem = $"Upload do grupo '{groupName}' finalizado com sucesso"
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao finalizar upload do grupo: {ex.ToString()}");
                return Results.BadRequest($"Erro ao finalizar o upload do grupo: {ex.ToString()}");
            }
        });
    }

    private static void ProcessPendingChunks(string uploadPath)
    {
        Console.WriteLine($"Iniciando processamento de chunks no diretório: {uploadPath}");
        var chunkFiles = Directory.GetFiles(uploadPath, "*.part_*", SearchOption.AllDirectories).GroupBy(f => 
        {
            var fileName = Path.GetFileName(f);
            return fileName.Substring(0, fileName.LastIndexOf(".part_"));
        });
        
        Console.WriteLine($"Encontrados {chunkFiles.Count()} arquivos para processar");
        
        foreach (var fileChunks in chunkFiles)
        {
            var fileName = fileChunks.Key;
            var chunks = fileChunks.OrderBy(f => 
            {
                var chunkNumberStr = f[(f.LastIndexOf("_") + 1)..];
                return int.Parse(chunkNumberStr);
            }).ToList();
            
            Console.WriteLine($"Processando arquivo: {fileName} com {chunks.Count} chunks");
            
            // Pegar o diretório do primeiro chunk para determinar o caminho completo
            var firstChunkDir = Path.GetDirectoryName(chunks.First());
            var relativeDir = "";
            
            if (firstChunkDir != null && firstChunkDir != uploadPath)
            {
                relativeDir = Path.GetRelativePath(uploadPath, firstChunkDir);
                if (relativeDir == ".")
                {
                    relativeDir = "";
                }
            }
            
            var targetPath = Path.Combine(uploadPath, relativeDir, fileName);
            Console.WriteLine($"Criando arquivo de saída: {targetPath}");
            
            // Garantir que o diretório de destino exista
            var targetDir = Path.GetDirectoryName(targetPath);
            if (!Directory.Exists(targetDir) && targetDir != null)
            {
                Directory.CreateDirectory(targetDir);
            }
            
            using (var outputStream = new FileStream(targetPath, FileMode.Create))
            {
                foreach (var chunk in chunks)
                {
                    var buffer = File.ReadAllBytes(chunk);
                    outputStream.Write(buffer, 0, buffer.Length);
                    Console.WriteLine($"Adicionado chunk: {chunk} ({buffer.Length} bytes)");
                    
                    // Excluir arquivo de chunk após processamento
                    try 
                    {
                        File.Delete(chunk);
                        Console.WriteLine($"Chunk excluído: {chunk}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Erro ao excluir chunk {chunk}: {ex.Message}");
                        // Ignorar erros ao excluir chunks temporários
                    }
                }
            }
            
            Console.WriteLine($"Arquivo {fileName} processado com sucesso");
        }
        
        Console.WriteLine("Processamento de chunks concluído");
    }

    private static string CreateSiteBackup(string siteName, string physicalPath)
    {
        try
        {
            var backupDir = Path.Combine(physicalPath, "../backups");
            if (!Directory.Exists(backupDir))
            {
                Directory.CreateDirectory(backupDir);
            }
            
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupFileName = $"{siteName}_backup_{timestamp}.zip";
            var backupPath = Path.Combine(backupDir, backupFileName);
            
            if (Directory.Exists(physicalPath))
            {
                ZipFile.CreateFromDirectory(physicalPath, backupPath, CompressionLevel.Fastest, false);
            }
            
            return backupPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao criar backup: {ex.ToString()}");
            return string.Empty;
        }
    }

    private static void MoveFiles(string sourcePath, string targetPath)
    {
        var uploadedFiles = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories);
        
        foreach (var sourceFile in uploadedFiles)
        {
            try
            {
                var relativePath = Path.GetRelativePath(sourcePath, sourceFile);
                var targetFilePath = Path.Combine(targetPath, relativePath);
                var targetDir = Path.GetDirectoryName(targetFilePath);
                
                if (!Directory.Exists(targetDir) && targetDir != null)
                {
                    Directory.CreateDirectory(targetDir);
                }
                
                if (File.Exists(targetFilePath))
                {
                    File.Delete(targetFilePath);
                }
                
                File.Move(sourceFile, targetFilePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao mover arquivo {sourceFile}: {ex.ToString()}");
                // Continuar com os próximos arquivos
            }
        }
    }

    private static void CopyFiles(string sourcePath, string targetPath)
    {
        var uploadedFiles = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories);
        
        foreach (var sourceFile in uploadedFiles)
        {
            Console.WriteLine($"Copiando arquivo {sourceFile} para {targetPath}");
            try
            {
                var relativePath = Path.GetRelativePath(sourcePath, sourceFile);
                
                // Corrigir arquivos que ainda possuem .part_0 no nome
                if (relativePath.EndsWith(".part_0"))
                {
                    relativePath = relativePath.Substring(0, relativePath.Length - 7); // Remove ".part_0"
                    Console.WriteLine($"Corrigindo nome de arquivo com .part_0: {relativePath}");
                }
                
                var targetFilePath = Path.Combine(targetPath, relativePath);
                var targetDir = Path.GetDirectoryName(targetFilePath);
                
                if (!Directory.Exists(targetDir) && targetDir != null)
                {
                    Directory.CreateDirectory(targetDir);
                }
                
                if (File.Exists(targetFilePath))
                {
                    File.Delete(targetFilePath);
                }
                
                File.Copy(sourceFile, targetFilePath, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao copiar arquivo {sourceFile}: {ex.ToString()}");
                // Continuar com os próximos arquivos
            }
        }
    }

    private static void SetDates(string sourcePath, string targetPath, string uploadId)
    {
        var uploadedFiles = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories);
        
        foreach (var sourceFile in uploadedFiles)
        {
            try
            {
                Console.WriteLine($"Definindo data de modificação para o arquivo {sourceFile}");
                var relativePath = Path.GetRelativePath(sourcePath, sourceFile);
                var targetFilePath = Path.Combine(targetPath, relativePath);
                
                // Definir data de última modificação original, se disponível
                // Verificar pelo caminho relativo completo
                if (FileLastModifiedTimes.TryGetValue(uploadId, out var dateDict))
                {
                    // Tenta pelo caminho relativo normalizado (caminho com barras)
                    var normalizedPath = relativePath.Replace('\\', '/');
                    Console.WriteLine($"Tentando aplicar data de modificação ao arquivo {normalizedPath}");
                    if (dateDict.TryGetValue(normalizedPath, out var lastModifiedDate))
                    {
                        Console.WriteLine($"Aplicando data de modificação {lastModifiedDate} ao arquivo {targetFilePath}");
                        File.SetLastWriteTime(targetFilePath, lastModifiedDate);
                    }
                    // Se não encontrar, tenta pelo nome do arquivo com o caminho original
                    else if (dateDict.TryGetValue(relativePath, out lastModifiedDate))
                    {
                        Console.WriteLine($"Aplicando data de modificação {lastModifiedDate} ao arquivo {targetFilePath}");
                        File.SetLastWriteTime(targetFilePath, lastModifiedDate);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao definir data do arquivo {sourceFile}: {ex.ToString()}");
                // Continuar com os próximos arquivos
            }
        }
    }
}


// Implementação concreta do serviço de grupos
public class GroupService
{
    private readonly string _groupsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "site-groups.json");
    
    public async Task<SiteGroupDto> GetGroupAsync(string name)
    {
        if (!File.Exists(_groupsFilePath))
            return null;

        var json = await File.ReadAllTextAsync(_groupsFilePath);
        var groups = System.Text.Json.JsonSerializer.Deserialize<List<SiteGroupDto>>(json);
        return groups?.FirstOrDefault(g => g.Name == name);
    }
} 