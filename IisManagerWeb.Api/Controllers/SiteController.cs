using Microsoft.Web.Administration;
using IisManagerWeb.Shared.Models;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace IisManagerWeb.Api.Controllers;

public static class SiteController
{
    public static void GetSiteRoutes(this WebApplication app)
    {
        var siteApi = app.MapGroup("/sites");

        siteApi.MapGet("/", () =>
        {
            using var serverManager = new ServerManager();
            var sites = serverManager.Sites.Select(SiteMapper.FromSite).ToList();
            return Results.Ok(sites);
        });

        siteApi.MapGet("/{name}", (string name) =>
        {
            using var serverManager = new ServerManager();
            var site = serverManager.Sites[name];
            if (site == null) return Results.StatusCode(204);

            var siteDto = SiteMapper.FromSite(site);
            return Results.Ok(siteDto);
        });

        // Iniciar site
        siteApi.MapPost("/{name}/start", (string name) =>
        {
            using var serverManager = new ServerManager();
            var site = serverManager.Sites[name];
            if (site == null) return Results.StatusCode(204);

            site.Start();
            return Results.Ok();
        });

        // Parar site
        siteApi.MapPost("/{name}/stop", (string name) =>
        {
            using var serverManager = new ServerManager();
            var site = serverManager.Sites[name];
            if (site == null) return Results.StatusCode(204);

            site.Stop();
            return Results.Ok();
        });

        // Reiniciar site
        siteApi.MapPost("/{name}/restart", (string name) =>
        {
            using var serverManager = new ServerManager();
            var site = serverManager.Sites[name];
            if (site == null) return Results.StatusCode(204);

            site.Stop();
            site.Start();
            return Results.Ok();
        });

        siteApi.MapPut("/{name}/physical-path", (string name, string physicalPath) =>
        {
            using var serverManager = new ServerManager();
            var site = serverManager.Sites[name];
            if (site == null) return Results.StatusCode(204);

            site.Applications["/"].VirtualDirectories["/"].PhysicalPath = physicalPath;
            serverManager.CommitChanges();
            return Results.Ok();
        });

        // Atualizar certificado
        siteApi.MapPut("/{name}/certificate", (string name, string certificateHash, string certificateStoreName) =>
        {
            using var serverManager = new ServerManager();
            var site = serverManager.Sites[name];
            if (site == null) return Results.StatusCode(204);

            try
            {
                var hashBytes = Convert.FromHexString(certificateHash);
                foreach (var binding in site.Bindings.Where(b => b.Protocol == "https"))
                {
                    binding.CertificateHash = hashBytes;
                    binding.CertificateStoreName = certificateStoreName;
                }

                serverManager.CommitChanges();
                return Results.Ok();
            }
            catch (FormatException)
            {
                return Results.BadRequest("O certificateHash deve ser uma string hexadecimal válida");
            }
        });

        siteApi.MapPut("/{name}/application-pool", (string name, string applicationPool) =>
        {
            using var serverManager = new ServerManager();
            var site = serverManager.Sites[name];
            if (site == null) return Results.StatusCode(204);

            site.Applications["/"].ApplicationPoolName = applicationPool;
            serverManager.CommitChanges();
            return Results.Ok();
        });

        siteApi.MapPost("/{name}/bindings", (string name, string protocol, string bindingInformation) =>
        {
            using var serverManager = new ServerManager();
            var site = serverManager.Sites[name];
            if (site == null) return Results.StatusCode(204);

            site.Bindings.Add(bindingInformation, protocol);
            serverManager.CommitChanges();
            return Results.Ok();
        });

        siteApi.MapDelete("/{name}/bindings/{bindingInformation}", (string name, string bindingInformation) =>
        {
            using var serverManager = new ServerManager();
            var site = serverManager.Sites[name];
            if (site == null) return Results.StatusCode(204);

            var binding = site.Bindings.FirstOrDefault(b => b.BindingInformation == bindingInformation);
            if (binding == null) return Results.StatusCode(204);

            site.Bindings.Remove(binding);
            serverManager.CommitChanges();
            return Results.Ok();
        });

        // Atualizar arquivos do site
        siteApi.MapPost("/{name}/update-files", async (string name, HttpRequest request) =>
        {
            try
            {
                using var serverManager = new ServerManager();
                var site = serverManager.Sites[name];
                if (site == null) return Results.StatusCode(204);

                var physicalPath = site.Applications["/"].VirtualDirectories["/"].PhysicalPath;
                if (string.IsNullOrEmpty(physicalPath))
                    return Results.BadRequest("Caminho físico do site não encontrado");

                if (site.State == ObjectState.Started)
                    site.Stop();

                var backupFileName = CreateSiteBackup(name, physicalPath);

                var tempZipPath = Path.Combine(Path.GetTempPath(), $"update_{name}_{DateTime.Now.ToString("yyyyMMdd_HHmmss")}.zip");
                using (var fileStream = File.Create(tempZipPath))
                {
                    await request.Body.CopyToAsync(fileStream);
                }

                System.IO.Compression.ZipFile.ExtractToDirectory(tempZipPath, physicalPath, true);

                using (var archive = ZipFile.OpenRead(tempZipPath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name))
                            continue;

                        var destPath = Path.Combine(physicalPath, entry.FullName);
                        if (File.Exists(destPath))
                        {
                            File.SetLastWriteTime(destPath, entry.LastWriteTime.DateTime);
                        }
                    }
                }

                File.Delete(tempZipPath);

                site.Start();

                return Results.Ok($"Site '{name}' atualizado com sucesso. Backup criado em {backupFileName}");
            }
            catch (Exception ex)
            {
                return Results.BadRequest($"Erro ao atualizar os arquivos do site: {ex.Message}");
            }
        });

        // Verificar quais arquivos precisam ser atualizados
        siteApi.MapPost("/{name}/check-files", async (string name, HttpRequest request) =>
        {
            try
            {
                Console.WriteLine("Verificando arquivos do site");
                using var serverManager = new ServerManager();
                var site = serverManager.Sites[name];
                if (site == null) return Results.StatusCode(204);

                var physicalPath = site.Applications["/"].VirtualDirectories["/"].PhysicalPath;
                Console.WriteLine($"Caminho físico do site: {physicalPath}");
                if (string.IsNullOrEmpty(physicalPath))
                {
                    Console.WriteLine("Caminho físico do site não encontrado");
                    return Results.BadRequest("Caminho físico do site não encontrado");
                }

                var settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ManagerSettings.json");
                List<string> ignoredPatterns = new List<string>();

                if (File.Exists(settingsPath))
                {
                    try
                    {
                        var settingsJson = File.ReadAllText(settingsPath);
                        var settings = JsonSerializer.Deserialize(settingsJson,
                            AppJsonSerializerContext.Default.ManagerSettings);
                        if (settings != null && settings.IgnoredFiles.Count > 0)
                        {
                            ignoredPatterns = settings.IgnoredFiles;
                            Console.WriteLine($"Aplicando {ignoredPatterns.Count} padrões de arquivos ignorados");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Erro ao carregar configurações: {ex.Message}");
                    }
                }

                List<ClientFileInfo>? clientFiles = null;
                try
                {
                    clientFiles = await request.ReadFromJsonAsync<List<ClientFileInfo>>();
                    Console.WriteLine($"Deserialização concluída. Quantidade de arquivos: {clientFiles?.Count ?? 0}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erro ao deserializar a lista de arquivos: {ex.Message} - {ex.StackTrace}");
                    return Results.BadRequest("Erro ao deserializar a lista de arquivos enviada pelo cliente");
                }

                if (clientFiles == null || clientFiles.Count == 0)
                {
                    Console.WriteLine("Nenhum arquivo enviado pelo cliente");
                    return Results.Ok(new FileCheckResponse());
                }

                var filesToUpdate = new List<string>();
                var response = new FileCheckResponse();

                foreach (var clientFile in clientFiles)
                {
                    Console.WriteLine(
                        $"Verificando arquivo: {clientFile.RelativePath} - {clientFile.IsDirectory} - {clientFile.LastModified}");
                    var relativePath = clientFile.RelativePath;

                    var fileInfo = new FileUpdateInfo
                    {
                        RelativePath = relativePath,
                        ClientFile = clientFile,
                        IsIgnored = false
                    };

                    if (ShouldIgnoreFile(relativePath, ignoredPatterns))
                    {
                        Console.WriteLine($"Arquivo ignorado de acordo com as regras configuradas: {relativePath}");
                        fileInfo.IsIgnored = true;
                        fileInfo.IgnoreReason = "Arquivo corresponde a um padrão de exclusão";
                        fileInfo.UpdateReason = FileUpdateReason.Ignored;
                        response.IgnoredFiles.Add(fileInfo);
                        response.Files.Add(fileInfo);
                        continue;
                    }

                    if (clientFile.IsDirectory)
                    {
                        Console.WriteLine("É um diretório");
                        fileInfo.IsIgnored = true;
                        fileInfo.IgnoreReason = "É um diretório";
                        fileInfo.UpdateReason = FileUpdateReason.Ignored;
                        response.IgnoredFiles.Add(fileInfo);
                        response.Files.Add(fileInfo);
                        continue;
                    }

                    var serverFilePath = Path.Combine(physicalPath, relativePath);
                    Console.WriteLine($"Caminho do arquivo no servidor: {serverFilePath}");

                    if (!File.Exists(serverFilePath))
                    {
                        Console.WriteLine($"Arquivo não existe no servidor: {serverFilePath}");
                        filesToUpdate.Add(relativePath);
                        fileInfo.UpdateReason = FileUpdateReason.FileNotExistsOnServer;
                        response.Files.Add(fileInfo);
                        response.FilesToUpdate.Add(relativePath);
                    }
                    else
                    {
                        var fileInfo2 = new FileInfo(serverFilePath);
                        var serverFile = new ServerFileInfo
                        {
                            RelativePath = relativePath,
                            FileName = Path.GetFileName(serverFilePath),
                            Size = fileInfo2.Length,
                            LastModified = fileInfo2.LastWriteTime,
                            IsDirectory = false
                        };

                        fileInfo.ServerFile = serverFile;

                        if (File.GetLastWriteTime(serverFilePath) != clientFile.LastModified)
                        {
                            Console.WriteLine($"Data de modificação do arquivo é diferente: {serverFilePath}");
                            filesToUpdate.Add(relativePath);
                            fileInfo.UpdateReason = FileUpdateReason.modifiedDateDifferent;
                            response.Files.Add(fileInfo);
                            response.FilesToUpdate.Add(relativePath);
                        }
                        else if (fileInfo2.Length != clientFile.Size)
                        {
                            Console.WriteLine($"Tamanho do arquivo é diferente: {serverFilePath}");
                            filesToUpdate.Add(relativePath);
                            fileInfo.UpdateReason = FileUpdateReason.DifferentSize;
                            response.Files.Add(fileInfo);
                            response.FilesToUpdate.Add(relativePath);
                        }
                        else
                        {
                            fileInfo.IsIgnored = true;
                            fileInfo.IgnoreReason = "Arquivo já está atualizado";
                            fileInfo.UpdateReason = FileUpdateReason.Ignored;
                            response.IgnoredFiles.Add(fileInfo);
                            response.Files.Add(fileInfo);
                        }
                    }
                }

                Console.WriteLine($"Arquivos que precisam ser atualizados:\n {string.Join("\n   ", filesToUpdate)}");
                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                return Results.BadRequest($"Erro ao verificar arquivos: {ex.Message}");
            }
        });

        // Atualizar arquivos específicos do site
        siteApi.MapPost("/{name}/update-specific-files", async (string name, HttpRequest request) =>
        {
            try
            {
                using var serverManager = new ServerManager();
                var site = serverManager.Sites[name];
                if (site == null) return Results.StatusCode(204);

                var physicalPath = site.Applications["/"].VirtualDirectories["/"].PhysicalPath;
                if (string.IsNullOrEmpty(physicalPath))
                    return Results.BadRequest("Caminho físico do site não encontrado");

                var settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ManagerSettings.json");
                List<string> ignoredPatterns = new List<string>();

                if (File.Exists(settingsPath))
                {
                    try
                    {
                        var settingsJson = File.ReadAllText(settingsPath);
                        var settings = JsonSerializer.Deserialize(settingsJson,
                            AppJsonSerializerContext.Default.ManagerSettings);
                        if (settings != null && settings.IgnoredFiles.Count > 0)
                        {
                            ignoredPatterns = settings.IgnoredFiles;
                            Console.WriteLine($"Aplicando {ignoredPatterns.Count} padrões de arquivos ignorados");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Erro ao carregar configurações: {ex.Message}");
                    }
                }

                if (site.State == ObjectState.Started)
                    site.Stop();

                // Criar backup do site antes de atualizar
                var backupFileName = CreateSiteBackup(name, physicalPath);

                var form = await request.ReadFormAsync();
                var files = form.Files;

                if (files.Count == 0)
                    return Results.BadRequest("Nenhum arquivo enviado");

                var directories = new HashSet<string>();

                foreach (var file in files)
                {
                    if (ShouldIgnoreFile(file.FileName, ignoredPatterns))
                    {
                        Console.WriteLine($"Arquivo ignorado de acordo com as regras configuradas: {file.FileName}");
                        continue;
                    }

                    var dirPath = Path.GetDirectoryName(Path.Combine(physicalPath, file.FileName));
                    if (!string.IsNullOrEmpty(dirPath))
                    {
                        directories.Add(dirPath);
                    }
                }

                foreach (var dir in directories)
                {
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                }

                foreach (var file in files)
                {
                    if (ShouldIgnoreFile(file.FileName, ignoredPatterns))
                    {
                        continue;
                    }

                    var destPath = Path.Combine(physicalPath, file.FileName);

                    await using (var stream = new FileStream(destPath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    var hasValue = form.TryGetValue($"lastModified_{file.FileName}", out var lastModifiedValues);

                    if (hasValue && lastModifiedValues.Count > 0 &&
                        DateTime.TryParse(lastModifiedValues[0], out var lastModified))
                    {
                        Console.WriteLine($"Data de modificação original: {lastModified.ToLocalTime()}");
                        File.SetLastWriteTime(destPath, lastModified.ToLocalTime());
                    }
                }

                site.Start();

                return Results.Ok($"Arquivos do site '{name}' atualizados com sucesso. Backup criado em {backupFileName}");
            }
            catch (Exception ex)
            {
                return Results.BadRequest($"Erro ao atualizar os arquivos do site: {ex.Message}");
            }
        });

        // Atualizar arquivos em múltiplos sites
        siteApi.MapPost("/update-multiple", async (HttpRequest request) =>
        {
            try
            {
                var form = await request.ReadFormAsync();
                var files = form.Files;

                if (files.Count == 0)
                    return Results.BadRequest("Nenhum arquivo enviado");

                var siteNames = new List<string>();
                for (int i = 0;; i++)
                {
                    if (form.TryGetValue($"siteNames[{i}]", out var siteName) && siteName.Count > 0)
                    {
                        siteNames.Add(siteName[0]);
                    }
                    else
                    {
                        break;
                    }
                }

                if (siteNames.Count == 0)
                    return Results.BadRequest("Nenhum site especificado");

                Console.WriteLine($"Sites a serem atualizados: {string.Join(", ", siteNames)}");

                var settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ManagerSettings.json");
                List<string> ignoredPatterns = new List<string>();

                if (File.Exists(settingsPath))
                {
                    try
                    {
                        var settingsJson = File.ReadAllText(settingsPath);
                        var settings = JsonSerializer.Deserialize(settingsJson,
                            AppJsonSerializerContext.Default.ManagerSettings);
                        if (settings != null && settings.IgnoredFiles.Count > 0)
                        {
                            ignoredPatterns = settings.IgnoredFiles;
                            Console.WriteLine($"Aplicando {ignoredPatterns.Count} padrões de arquivos ignorados");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Erro ao carregar configurações: {ex.Message}");
                    }
                }

                var sitesAtualizados = 0;
                var sitesComErro = new List<string>();
                var backupsCriados = new Dictionary<string, string>();

                using var serverManager = new ServerManager();

                foreach (var siteName in siteNames)
                {
                    try
                    {
                        var site = serverManager.Sites[siteName];
                        if (site == null)
                        {
                            Console.WriteLine($"Site não encontrado: {siteName}");
                            sitesComErro.Add(siteName);
                            continue;
                        }

                        var physicalPath = site.Applications["/"].VirtualDirectories["/"].PhysicalPath;
                        if (string.IsNullOrEmpty(physicalPath))
                        {
                            Console.WriteLine($"Caminho físico não encontrado para o site: {siteName}");
                            sitesComErro.Add(siteName);
                            continue;
                        }

                        var siteWasRunning = false;
                        if (site.State == ObjectState.Started)
                        {
                            siteWasRunning = true;
                            site.Stop();
                        }

                        // Criar backup do site antes de atualizar
                        var backupFileName = CreateSiteBackup(siteName, physicalPath);
                        backupsCriados.Add(siteName, backupFileName);

                        var directories = new HashSet<string>();

                        foreach (var file in files)
                        {
                            if (ShouldIgnoreFile(file.FileName, ignoredPatterns))
                            {
                                Console.WriteLine(
                                    $"Arquivo ignorado de acordo com as regras configuradas: {file.FileName}");
                                continue;
                            }

                            var dirPath = Path.GetDirectoryName(Path.Combine(physicalPath, file.FileName));
                            if (!string.IsNullOrEmpty(dirPath))
                            {
                                directories.Add(dirPath);
                            }
                        }

                        foreach (var dir in directories)
                        {
                            if (!Directory.Exists(dir))
                            {
                                Directory.CreateDirectory(dir);
                            }
                        }

                        foreach (var file in files)
                        {
                            if (ShouldIgnoreFile(file.FileName, ignoredPatterns))
                            {
                                continue;
                            }

                            var destPath = Path.Combine(physicalPath, file.FileName);

                            file.OpenReadStream().Seek(0, SeekOrigin.Begin);

                            using var stream = new FileStream(destPath, FileMode.Create);
                            await file.CopyToAsync(stream);

                            var hasValue = form.TryGetValue($"lastModified_{file.FileName}",
                                out var lastModifiedValues);

                            if (hasValue && lastModifiedValues.Count > 0)
                            {
                                if (DateTime.TryParse(lastModifiedValues[0], out var lastModified))
                                {
                                    File.SetLastWriteTime(destPath, lastModified);
                                }
                            }
                        }

                        if (siteWasRunning)
                        {
                            site.Start();
                        }

                        sitesAtualizados++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Erro ao atualizar o site {siteName}: {ex.Message}");
                        sitesComErro.Add(siteName);
                    }
                }

                var mensagemBackups = backupsCriados.Count > 0
                    ? $" Backups criados: {string.Join(", ", backupsCriados.Select(b => $"{b.Key} em {b.Value}"))}"
                    : "";

                if (sitesComErro.Count > 0)
                {
                    return Results.BadRequest(
                        $"Alguns sites apresentaram erros durante a atualização: {string.Join(", ", sitesComErro)}. " +
                        $"Sites atualizados com sucesso: {sitesAtualizados} de {siteNames.Count}.{mensagemBackups}");
                }

                return Results.Ok($"{sitesAtualizados} site(s) atualizado(s) com sucesso.{mensagemBackups}");
            }
            catch (Exception ex)
            {
                return Results.BadRequest($"Erro ao atualizar os sites: {ex.Message}");
            }
        });
    }

    private static string CreateSiteBackup(string siteName, string physicalPath)
    {
        var backupDir = Path.Combine(Path.GetDirectoryName(physicalPath) ?? "", "Backups");
        if (!Directory.Exists(backupDir))
            Directory.CreateDirectory(backupDir);

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupFileName = Path.Combine(backupDir, $"backup_{siteName}_{timestamp}.zip");

        System.IO.Compression.ZipFile.CreateFromDirectory(physicalPath, backupFileName);
        Console.WriteLine($"Backup criado em: {backupFileName} para o site: {siteName}");
        return backupFileName;
    }

    private static bool ShouldIgnoreFile(string filePath, List<string> ignoredPatterns)
    {
        if (ignoredPatterns.Count == 0)
            return false;

        filePath = filePath.Replace('\\', '/');

        foreach (var pattern in ignoredPatterns)
        {
            if (pattern == filePath)
                return true;

            if (pattern.EndsWith("/*") && filePath.StartsWith(pattern.TrimEnd('*')))
                return true;

            if (pattern.StartsWith("*") && filePath.EndsWith(pattern.TrimStart('*')))
                return true;

            if (pattern.StartsWith("*") && pattern.EndsWith("*") &&
                pattern.Length > 2 && filePath.Contains(pattern.Trim('*')))
                return true;
        }

        return false;
    }
}