using Microsoft.Web.Administration;
using IisManagerWeb.Shared.Models;
using System.IO;
using System.IO.Compression;

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
    }
}