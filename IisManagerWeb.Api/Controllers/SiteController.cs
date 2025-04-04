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

        // Obter todos os sites
        siteApi.MapGet("/", () =>
        {
            using var serverManager = new ServerManager();
            var sites = serverManager.Sites.Select(SiteMapper.FromSite).ToList();
            return Results.Ok(sites);
        });

        // Obter site específico
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

        // Atualizar caminho físico
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

        // Atualizar pool de aplicação
        siteApi.MapPut("/{name}/application-pool", (string name, string applicationPool) =>
        {
            using var serverManager = new ServerManager();
            var site = serverManager.Sites[name];
            if (site == null) return Results.StatusCode(204);
            
            site.Applications["/"].ApplicationPoolName = applicationPool;
            serverManager.CommitChanges();
            return Results.Ok();
        });

        // Adicionar binding
        siteApi.MapPost("/{name}/bindings", (string name, string protocol, string bindingInformation) =>
        {
            using var serverManager = new ServerManager();
            var site = serverManager.Sites[name];
            if (site == null) return Results.StatusCode(204);
            
            site.Bindings.Add(bindingInformation, protocol);
            serverManager.CommitChanges();
            return Results.Ok();
        });

        // Remover binding
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
                
                // Obter o caminho físico do site
                var physicalPath = site.Applications["/"].VirtualDirectories["/"].PhysicalPath;
                if (string.IsNullOrEmpty(physicalPath))
                    return Results.BadRequest("Caminho físico do site não encontrado");
                
                // Parar o site antes de atualizar os arquivos
                if (site.State == ObjectState.Started)
                    site.Stop();
                
                // Criar pasta de backup se não existir
                var backupDir = Path.Combine(Path.GetDirectoryName(physicalPath) ?? "", "Backups");
                if (!Directory.Exists(backupDir))
                    Directory.CreateDirectory(backupDir);
                
                // Criar nome de arquivo para o backup
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backupFileName = Path.Combine(backupDir, $"backup_{name}_{timestamp}.zip");
                
                // Criar backup do site atual
                System.IO.Compression.ZipFile.CreateFromDirectory(physicalPath, backupFileName);
                
                // Recebe e salva o arquivo ZIP temporariamente
                var tempZipPath = Path.Combine(Path.GetTempPath(), $"update_{name}_{timestamp}.zip");
                using (var fileStream = File.Create(tempZipPath))
                {
                    await request.Body.CopyToAsync(fileStream);
                }
                
                // Extrair o arquivo ZIP para o caminho do site
                System.IO.Compression.ZipFile.ExtractToDirectory(tempZipPath, physicalPath, true);
                
                // Preservar a data/hora original dos arquivos do ZIP
                using (var archive = ZipFile.OpenRead(tempZipPath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name))
                            continue; // Ignora entradas de diretório
                            
                        var destPath = Path.Combine(physicalPath, entry.FullName);
                        if (File.Exists(destPath))
                        {
                            File.SetLastWriteTime(destPath, entry.LastWriteTime.DateTime);
                        }
                    }
                }
                
                // Excluir o arquivo ZIP temporário
                File.Delete(tempZipPath);
                
                // Iniciar o site novamente
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
                // add logs
                Console.WriteLine("Verificando arquivos do site");
                using var serverManager = new ServerManager();
                var site = serverManager.Sites[name];
                if (site == null) return Results.StatusCode(204);
                
                // Obter o caminho físico do site
                var physicalPath = site.Applications["/"].VirtualDirectories["/"].PhysicalPath;
                Console.WriteLine($"Caminho físico do site: {physicalPath}");
                if (string.IsNullOrEmpty(physicalPath))
                {
                    Console.WriteLine("Caminho físico do site não encontrado");
                    return Results.BadRequest("Caminho físico do site não encontrado");
                }
                
                // Carregar as configurações para obter os padrões de arquivos ignorados
                var settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ManagerSettings.json");
                List<string> ignoredPatterns = new List<string>();
                
                if (File.Exists(settingsPath))
                {
                    try
                    {
                        var settingsJson = File.ReadAllText(settingsPath);
                        var settings = JsonSerializer.Deserialize(settingsJson, AppJsonSerializerContext.Default.ManagerSettings);
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

                // Deserializar a lista de arquivos enviada pelo cliente
                
                List<ClientFileInfo>? clientFiles = null;
                try 
                {
                    clientFiles = await request.ReadFromJsonAsync<List<ClientFileInfo>>();
                    Console.WriteLine($"Deserialização concluída. Quantidade de arquivos: {clientFiles?.Count ?? 0}");
                    
                    if (clientFiles != null && clientFiles.Count > 0)
                    {
                        var primeiroArquivo = clientFiles[0];
                        Console.WriteLine($"Primeiro arquivo: RelativePath={primeiroArquivo.RelativePath}, " +
                                         $"FileName={primeiroArquivo.FileName}, " +
                                         $"IsDirectory={primeiroArquivo.IsDirectory}, " +
                                         $"LastModified={primeiroArquivo.LastModified}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erro ao deserializar a lista de arquivos: {ex.Message} - {ex.StackTrace}");
                    return Results.BadRequest("Erro ao deserializar a lista de arquivos enviada pelo cliente");
                }
                
                if (clientFiles == null || clientFiles.Count == 0)
                {
                    Console.WriteLine("Nenhum arquivo enviado pelo cliente");
                    return Results.Ok(new List<string>());
                }
                
                // Lista para armazenar os arquivos que precisam ser atualizados
                var filesToUpdate = new List<string>();
                
                // Verificar cada arquivo do cliente
                foreach (var clientFile in clientFiles)
                {
                    Console.WriteLine($"Verificando arquivo: {clientFile.RelativePath} - {clientFile.IsDirectory} - {clientFile.LastModified}");
                    var relativePath = clientFile.RelativePath;
                    
                    // Verificar se o arquivo deve ser ignorado com base nos padrões configurados
                    if (ShouldIgnoreFile(relativePath, ignoredPatterns))
                    {
                        Console.WriteLine($"Arquivo ignorado de acordo com as regras configuradas: {relativePath}");
                        continue;
                    }
                    
                    // Se for um diretório, verificamos se ele existe no servidor
                    if (clientFile.IsDirectory)
                    {
                        Console.WriteLine("É um diretório");
                        continue;
                    }
                    
                    var serverFilePath = Path.Combine(physicalPath, relativePath);
                    Console.WriteLine($"Caminho do arquivo no servidor: {serverFilePath}");
                    // Se o arquivo não existe no servidor ou é mais recente no cliente, adiciona à lista
                    if (!File.Exists(serverFilePath) || File.GetLastWriteTime(serverFilePath) != clientFile.LastModified)
                    {
                        Console.WriteLine($"Arquivo não existe no servidor ou é mais recente no cliente: {serverFilePath}");
                        filesToUpdate.Add(relativePath);
                    }
                }
                Console.WriteLine($"Arquivos que precisam ser atualizados: {string.Join(", ", filesToUpdate)}");
                return Results.Ok(filesToUpdate);
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
                
                // Obter o caminho físico do site
                var physicalPath = site.Applications["/"].VirtualDirectories["/"].PhysicalPath;
                if (string.IsNullOrEmpty(physicalPath))
                    return Results.BadRequest("Caminho físico do site não encontrado");
                
                // Carregar as configurações para obter os padrões de arquivos ignorados
                var settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ManagerSettings.json");
                List<string> ignoredPatterns = new List<string>();
                
                if (File.Exists(settingsPath))
                {
                    try
                    {
                        var settingsJson = File.ReadAllText(settingsPath);
                        var settings = JsonSerializer.Deserialize(settingsJson, AppJsonSerializerContext.Default.ManagerSettings);
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
                
                // Parar o site antes de atualizar os arquivos
                if (site.State == ObjectState.Started)
                    site.Stop();
                
                // Processar o formulário multipart
                var form = await request.ReadFormAsync();
                var files = form.Files;
                
                if (files.Count == 0)
                    return Results.BadRequest("Nenhum arquivo enviado");
                
                // Lista para armazenar os diretórios que precisam ser criados
                var directories = new HashSet<string>();
                
                // Primeiro identificamos todos os diretórios necessários
                foreach (var file in files)
                {
                    // Verificar se o arquivo deve ser ignorado com base nos padrões configurados
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
                
                // Criar diretórios necessários
                foreach (var dir in directories)
                {
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                }
                
                // Agora, copiamos os arquivos
                foreach (var file in files)
                {
                    // Verificar se o arquivo deve ser ignorado com base nos padrões configurados
                    if (ShouldIgnoreFile(file.FileName, ignoredPatterns))
                    {
                        continue;
                    }
                    
                    var destPath = Path.Combine(physicalPath, file.FileName);
                    
                    using var stream = new FileStream(destPath, FileMode.Create);
                    await file.CopyToAsync(stream);
                    
                    // Obter data e hora original do arquivo a partir dos headers do formulário
                    var hasValue = form.TryGetValue($"lastModified_{file.FileName}", out var lastModifiedValues);
                    
                    if (hasValue && lastModifiedValues.Count > 0 && DateTime.TryParse(lastModifiedValues[0], out var lastModified))
                    {
                        Console.WriteLine($"Data de modificação original: {lastModified}");
                        Console.WriteLine($"Data de modificação original: {lastModified.ToLocalTime()}");
                        File.SetLastWriteTime(destPath, lastModified);
                    }
                }
                
                // Iniciar o site novamente
                site.Start();
                
                return Results.Ok($"Arquivos do site '{name}' atualizados com sucesso");
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
                // Processar o formulário multipart
                var form = await request.ReadFormAsync();
                var files = form.Files;
                
                if (files.Count == 0)
                    return Results.BadRequest("Nenhum arquivo enviado");
                
                // Obter os nomes dos sites do formulário
                var siteNames = new List<string>();
                for (int i = 0; ; i++)
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
                
                // Carregar as configurações para obter os padrões de arquivos ignorados
                var settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ManagerSettings.json");
                List<string> ignoredPatterns = new List<string>();
                
                if (File.Exists(settingsPath))
                {
                    try
                    {
                        var settingsJson = File.ReadAllText(settingsPath);
                        var settings = JsonSerializer.Deserialize(settingsJson, AppJsonSerializerContext.Default.ManagerSettings);
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
                
                using var serverManager = new ServerManager();
                
                // Para cada site, atualize os arquivos
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
                        
                        // Obter o caminho físico do site
                        var physicalPath = site.Applications["/"].VirtualDirectories["/"].PhysicalPath;
                        if (string.IsNullOrEmpty(physicalPath))
                        {
                            Console.WriteLine($"Caminho físico não encontrado para o site: {siteName}");
                            sitesComErro.Add(siteName);
                            continue;
                        }
                        
                        // Parar o site antes de atualizar os arquivos
                        var siteWasRunning = false;
                        if (site.State == ObjectState.Started)
                        {
                            siteWasRunning = true;
                            site.Stop();
                        }
                        
                        // Lista para armazenar os diretórios que precisam ser criados
                        var directories = new HashSet<string>();
                        
                        // Primeiro identificamos todos os diretórios necessários
                        foreach (var file in files)
                        {
                            // Verificar se o arquivo deve ser ignorado com base nos padrões configurados
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
                        
                        // Criar diretórios necessários
                        foreach (var dir in directories)
                        {
                            if (!Directory.Exists(dir))
                            {
                                Directory.CreateDirectory(dir);
                            }
                        }
                        
                        // Agora, copiamos os arquivos
                        foreach (var file in files)
                        {
                            // Verificar se o arquivo deve ser ignorado com base nos padrões configurados
                            if (ShouldIgnoreFile(file.FileName, ignoredPatterns))
                            {
                                continue;
                            }
                            
                            var destPath = Path.Combine(physicalPath, file.FileName);
                            
                            // Para cada arquivo, precisamos abrir um novo stream porque já foi lido para o site anterior
                            file.OpenReadStream().Seek(0, SeekOrigin.Begin);
                            
                            using var stream = new FileStream(destPath, FileMode.Create);
                            await file.CopyToAsync(stream);
                            
                            // Obter data e hora original do arquivo a partir dos headers do formulário
                            var hasValue = form.TryGetValue($"lastModified_{file.FileName}", out var lastModifiedValues);
                            
                            if (hasValue && lastModifiedValues.Count > 0)
                            {
                                if (DateTime.TryParse(lastModifiedValues[0], out var lastModified))
                                {
                                    // Aplicar a data de modificação original ao arquivo
                                    File.SetLastWriteTime(destPath, lastModified);
                                }
                            }
                        }
                        
                        // Iniciar o site novamente se estava rodando antes
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
                
                if (sitesComErro.Count > 0)
                {
                    return Results.BadRequest($"Alguns sites apresentaram erros durante a atualização: {string.Join(", ", sitesComErro)}. " +
                                              $"Sites atualizados com sucesso: {sitesAtualizados} de {siteNames.Count}.");
                }
                
                return Results.Ok($"{sitesAtualizados} site(s) atualizado(s) com sucesso");
            }
            catch (Exception ex)
            {
                return Results.BadRequest($"Erro ao atualizar os sites: {ex.Message}");
            }
        });
    }

    // Função auxiliar para verificar se um arquivo deve ser ignorado
    private static bool ShouldIgnoreFile(string filePath, List<string> ignoredPatterns)
    {
        if (ignoredPatterns.Count == 0)
            return false;
        
        // Converter barras para o formato universal para maior compatibilidade
        filePath = filePath.Replace('\\', '/');
        
        foreach (var pattern in ignoredPatterns)
        {
            // Padrão exato
            if (pattern == filePath)
                return true;
            
            // Padrão com wildcard no final (ex: "logs/*")
            if (pattern.EndsWith("/*") && filePath.StartsWith(pattern.TrimEnd('*')))
                return true;
            
            // Padrão com wildcard no início (ex: "*.log")
            if (pattern.StartsWith("*") && filePath.EndsWith(pattern.TrimStart('*')))
                return true;
            
            // Padrão com wildcards em ambos os lados (ex: "*.min.*")
            if (pattern.StartsWith("*") && pattern.EndsWith("*") && 
                pattern.Length > 2 && filePath.Contains(pattern.Trim('*')))
                return true;
        }
        
        return false;
    }
}