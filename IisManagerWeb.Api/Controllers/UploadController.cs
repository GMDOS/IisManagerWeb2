using Microsoft.AspNetCore.Mvc;
using Microsoft.Web.Administration;
using System.IO;
using System.IO.Compression;
using IisManagerWeb.Shared.Models;

namespace IisManagerWeb.Api.Controllers;

public static class UploadController
{
    private static readonly string UploadsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
    private static readonly Dictionary<string, Dictionary<string, DateTime>> FileLastModifiedTimes = new();

    public static void GetUploadRoutes(this WebApplication app)
    {
        var uploadApi = app.MapGroup("/uploads");

        uploadApi.MapPost("/iniciar", () =>
        {
            try
            {
                if (!Directory.Exists(UploadsDirectory))
                {
                    Directory.CreateDirectory(UploadsDirectory);
                }

                var uploadId = Guid.NewGuid().ToString();
                
                var uploadPath = Path.Combine(UploadsDirectory, uploadId);
                Directory.CreateDirectory(uploadPath);
                
                FileLastModifiedTimes[uploadId] = new Dictionary<string, DateTime>();

                return Results.Ok(new { UploadId = uploadId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest($"Erro ao iniciar o upload: {ex.Message}");
            }
        });

        uploadApi.MapPost("/{uploadId}/arquivo", async (string uploadId, HttpRequest request) =>
        {
            try
            {
                var uploadPath = Path.Combine(UploadsDirectory, uploadId);
                
                if (!Directory.Exists(uploadPath))
                {
                    return Results.BadRequest("ID de upload inválido ou expirado");
                }

                if (!request.HasFormContentType || request.Form.Files.Count == 0)
                {
                    return Results.BadRequest("Nenhum arquivo foi enviado");
                }

                var file = request.Form.Files[0];
                
                DateTime? lastModified = null;
                if (request.Form.TryGetValue("lastModified", out var lastModifiedValues) && 
                    lastModifiedValues.Count > 0 && 
                    DateTime.TryParse(lastModifiedValues[0], out var parsedDate))
                {
                    lastModified = parsedDate;
                    
                    if (!FileLastModifiedTimes.ContainsKey(uploadId))
                    {
                        FileLastModifiedTimes[uploadId] = new Dictionary<string, DateTime>();
                    }
                    
                    FileLastModifiedTimes[uploadId][file.FileName] = parsedDate;
                    Console.WriteLine($"Data de modificação registrada para {file.FileName}: {parsedDate}");
                }
                
                bool isChunk = request.Form.ContainsKey("chunkIndex");
                
                if (isChunk)
                {
                    return await ProcessChunkUpload(uploadId, uploadPath, file, request);
                }
                else
                {
                    return await ProcessRegularUpload(uploadPath, file);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro detalhado ao enviar arquivo: {ex}");
                return Results.BadRequest($"Erro ao enviar arquivo: {ex.Message}");
            }
        });

        uploadApi.MapPost("/{uploadId}/finalizar/{siteName}", (string uploadId, string siteName) =>
        {
            try
            {
                var uploadPath = Path.Combine(UploadsDirectory, uploadId);
                
                if (!Directory.Exists(uploadPath))
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
                
                MoveFilesAndSetDates(uploadPath, physicalPath, uploadId);
                
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
                
                Directory.Delete(uploadPath, true);

                return Results.Ok(new { 
                    Mensagem = $"Site '{siteName}' atualizado com sucesso",
                    BackupFile = backupFileName
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest($"Erro ao finalizar upload: {ex.Message}");
            }
        });
    }

    private static async Task<IResult> ProcessRegularUpload(string uploadPath, IFormFile file)
    {
        var filePath = Path.Combine(uploadPath, file.FileName);
        
        var fileDirectory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(fileDirectory) && !Directory.Exists(fileDirectory))
        {
            Directory.CreateDirectory(fileDirectory);
        }

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        return Results.Ok(new { FileName = file.FileName });
    }

    private static async Task<IResult> ProcessChunkUpload(string uploadId, string uploadPath, IFormFile file, HttpRequest request)
    {
        try
        {
            if (!int.TryParse(request.Form["chunkIndex"], out int chunkIndex))
            {
                return Results.BadRequest("Índice do chunk inválido");
            }

            string fileName = file.FileName;
            string chunksDir = Path.Combine(uploadPath, "__chunks__", fileName);
            
            if (!Directory.Exists(chunksDir))
            {
                Directory.CreateDirectory(chunksDir);
            }

            string chunkFile = Path.Combine(chunksDir, $"{chunkIndex}.part");
            using (var stream = new FileStream(chunkFile, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            return Results.Ok(new 
            { 
                FileName = fileName,
                ChunkIndex = chunkIndex,
                Message = "Chunk recebido com sucesso"
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao processar chunk: {ex}");
            return Results.BadRequest($"Erro ao processar chunk: {ex.Message}");
        }
    }

    private static void ProcessPendingChunks(string uploadPath)
    {
        string chunksBaseDir = Path.Combine(uploadPath, "__chunks__");
        
        if (!Directory.Exists(chunksBaseDir))
            return;
            
        foreach (var fileChunksDir in Directory.GetDirectories(chunksBaseDir))
        {
            try
            {
                string fileName = Path.GetFileName(fileChunksDir);
                string targetFilePath = Path.Combine(uploadPath, fileName);
                
                var fileDirectory = Path.GetDirectoryName(targetFilePath);
                if (!string.IsNullOrEmpty(fileDirectory) && !Directory.Exists(fileDirectory))
                {
                    Directory.CreateDirectory(fileDirectory);
                }
                
                // Se o arquivo já existe, removê-lo primeiro
                if (File.Exists(targetFilePath))
                {
                    try 
                    {
                        File.Delete(targetFilePath);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Erro ao excluir arquivo existente {targetFilePath}: {ex.Message}");
                        // Tentar com nome alternativo se não conseguir excluir
                        targetFilePath = Path.Combine(uploadPath, $"{Path.GetFileNameWithoutExtension(fileName)}_novo{Path.GetExtension(fileName)}");
                    }
                }
                
                try
                {
                    using (var targetStream = new FileStream(targetFilePath, FileMode.Create))
                    {
                        int chunkIndex = 0;
                        string chunkPath;
                        
                        while (File.Exists(chunkPath = Path.Combine(fileChunksDir, $"{chunkIndex}.part")))
                        {
                            using (var sourceStream = new FileStream(chunkPath, FileMode.Open))
                            {
                                sourceStream.CopyTo(targetStream);
                            }
                            chunkIndex++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erro ao processar e combinar chunks para {fileName}: {ex.Message}");
                    continue; // Ir para o próximo arquivo se falhar
                }
                
                try
                {
                    Directory.Delete(fileChunksDir, true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erro ao excluir diretório de chunks para {fileName}: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao processar chunks para {fileChunksDir}: {ex.Message}");
            }
        }
        
        try
        {
            Directory.Delete(chunksBaseDir, true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao excluir diretório base de chunks: {ex.Message}");
        }
    }

    private static string CreateSiteBackup(string siteName, string physicalPath)
    {
        var backupDir = Path.Combine(Directory.GetCurrentDirectory(), "backups");
        if (!Directory.Exists(backupDir))
        {
            Directory.CreateDirectory(backupDir);
        }

        var backupFileName = $"{siteName}_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
        var backupPath = Path.Combine(backupDir, backupFileName);

        ZipFile.CreateFromDirectory(physicalPath, backupPath);
        
        return backupPath;
    }

    private static void MoveFilesAndSetDates(string sourcePath, string destinationPath, string uploadId)
    {
        // Pegar a referência para a lista de datas de modificação
        var lastModifiedTimes = FileLastModifiedTimes.ContainsKey(uploadId) 
            ? FileLastModifiedTimes[uploadId] 
            : new Dictionary<string, DateTime>();

        foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            // Pular os arquivos e diretórios especiais
            if (file.Contains("__chunks__"))
                continue;
                
            var relativePath = file.Substring(sourcePath.Length + 1);
            var destinationFile = Path.Combine(destinationPath, relativePath);
            var destinationDir = Path.GetDirectoryName(destinationFile);

            try
            {
                if (!Directory.Exists(destinationDir))
                {
                    Directory.CreateDirectory(destinationDir);
                }

                if (File.Exists(destinationFile))
                {
                    try
                    {
                        File.Delete(destinationFile);
                    }
                    catch (IOException ex)
                    {
                        Console.WriteLine($"Erro ao excluir arquivo existente {destinationFile}: {ex.Message}");
                        
                        var fileName = Path.GetFileName(destinationFile);
                        var alternativeDestination = Path.Combine(
                            destinationDir,
                            $"{Path.GetFileNameWithoutExtension(fileName)}_novo{Path.GetExtension(fileName)}");
                        
                        destinationFile = alternativeDestination;
                    }
                }

                File.Copy(file, destinationFile, true);
                try
                {
                    File.Delete(file);
                }
                catch(Exception ex)
                {
                    Console.WriteLine($"Não foi possível excluir o arquivo fonte após cópia: {ex.Message}");
                }
                
                if (lastModifiedTimes.TryGetValue(relativePath, out var lastModified))
                {
                    try 
                    {
                        Console.WriteLine($"Atualizando data de modificação para {relativePath}: {lastModified}");
                        File.SetLastWriteTime(destinationFile, lastModified.ToLocalTime());
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Erro ao atualizar data de modificação para {relativePath}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao processar arquivo {relativePath}: {ex.Message}");
            }
        }
    }
} 