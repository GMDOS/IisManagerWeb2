using Microsoft.AspNetCore.Mvc;
using Microsoft.Web.Administration;
using System.IO;
using System.IO.Compression;
using IisManagerWeb.Shared.Models;

namespace IisManagerWeb.Api.Controllers;

public static class UploadController
{
    private static readonly string UploadsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "uploads");

    public static void GetUploadRoutes(this WebApplication app)
    {
        var uploadApi = app.MapGroup("/uploads");

        // Rota para iniciar um upload
        uploadApi.MapPost("/iniciar", () =>
        {
            try
            {
                // Criar a pasta de uploads se não existir
                if (!Directory.Exists(UploadsDirectory))
                {
                    Directory.CreateDirectory(UploadsDirectory);
                }

                // Gerar um UUID para o upload
                var uploadId = Guid.NewGuid().ToString();
                
                // Criar uma pasta temporária com o nome do UUID
                var uploadPath = Path.Combine(UploadsDirectory, uploadId);
                Directory.CreateDirectory(uploadPath);

                return Results.Ok(new { UploadId = uploadId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest($"Erro ao iniciar o upload: {ex.Message}");
            }
        });

        // Rota para enviar arquivos
        uploadApi.MapPost("/{uploadId}/arquivo", async (string uploadId, HttpRequest request) =>
        {
            try
            {
                var uploadPath = Path.Combine(UploadsDirectory, uploadId);
                
                // Verificar se a pasta de upload existe
                if (!Directory.Exists(uploadPath))
                {
                    return Results.BadRequest("ID de upload inválido ou expirado");
                }

                if (!request.HasFormContentType || request.Form.Files.Count == 0)
                {
                    return Results.BadRequest("Nenhum arquivo foi enviado");
                }

                var file = request.Form.Files[0];
                var filePath = Path.Combine(uploadPath, file.FileName);

                // Salvar o arquivo na pasta temporária
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                return Results.Ok(new { FileName = file.FileName });
            }
            catch (Exception ex)
            {
                return Results.BadRequest($"Erro ao enviar arquivo: {ex.Message}");
            }
        });

        // Rota para finalizar o upload e atualizar o site
        uploadApi.MapPost("/{uploadId}/finalizar/{siteName}", (string uploadId, string siteName) =>
        {
            try
            {
                var uploadPath = Path.Combine(UploadsDirectory, uploadId);
                
                // Verificar se a pasta de upload existe
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

                // 1. Criar backup da versão atual
                var backupFileName = CreateSiteBackup(siteName, physicalPath);
                
                // 2. Parar o site
                var siteState = site.State;
                if (siteState == ObjectState.Started)
                {
                    site.Stop();
                }
                
                // 3. Parar o pool de aplicações
                var appPoolName = site.Applications["/"].ApplicationPoolName;
                var appPool = serverManager.ApplicationPools[appPoolName];
                var appPoolState = appPool.State;
                
                if (appPoolState == ObjectState.Started)
                {
                    appPool.Stop();
                }
                
                // 4. Mover os arquivos do upload para a pasta do site
                MoveFiles(uploadPath, physicalPath);
                
                // 5. Iniciar o pool de aplicações
                if (appPoolState == ObjectState.Started)
                {
                    appPool.Start();
                }
                
                // 6. Iniciar o site
                if (siteState == ObjectState.Started)
                {
                    site.Start();
                }
                
                // Limpar a pasta temporária de upload
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

    private static void MoveFiles(string sourcePath, string destinationPath)
    {
        foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relativePath = file.Substring(sourcePath.Length + 1);
            var destinationFile = Path.Combine(destinationPath, relativePath);
            var destinationDir = Path.GetDirectoryName(destinationFile);

            if (!Directory.Exists(destinationDir))
            {
                Directory.CreateDirectory(destinationDir);
            }

            // Substituir arquivo existente
            if (File.Exists(destinationFile))
            {
                File.Delete(destinationFile);
            }

            File.Move(file, destinationFile);
        }
    }
} 