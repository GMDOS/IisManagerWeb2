using System.Text.Json;
using IisManagerWeb.Shared.Models;
using Microsoft.Web.Administration;

namespace IisManagerWeb.Api.Controllers;

public static class SiteGroupController
{
    private static readonly string GroupsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "site-groups.json");

    public static void GetSiteGroupRoutes(this WebApplication app)
    {
        var groupApi = app.MapGroup("/site-groups");

        // Obter todos os grupos
        groupApi.MapGet("/", () =>
        {
            if (!File.Exists(GroupsFilePath))
                return Results.Ok(new List<SiteGroupDto>());

            var json = File.ReadAllText(GroupsFilePath);
            var groups = JsonSerializer.Deserialize<List<SiteGroupDto>>(json);
            return Results.Ok(groups);
        });

        // Obter grupo específico
        groupApi.MapGet("/{name}", (string name) =>
        {
            if (!File.Exists(GroupsFilePath))
                return Results.NotFound();

            var json = File.ReadAllText(GroupsFilePath);
            var groups = JsonSerializer.Deserialize<List<SiteGroupDto>>(json);
            var group = groups?.FirstOrDefault(g => g.Name == name);
            
            if (group == null)
                return Results.NotFound();

            return Results.Ok(group);
        });

        // Criar novo grupo
        groupApi.MapPost("/", (SiteGroupDto group) =>
        {
            var groups = new List<SiteGroupDto>();
            
            if (File.Exists(GroupsFilePath))
            {
                var json = File.ReadAllText(GroupsFilePath);
                groups = JsonSerializer.Deserialize<List<SiteGroupDto>>(json) ?? new List<SiteGroupDto>();
            }

            if (groups.Any(g => g.Name == group.Name))
                return Results.BadRequest("Já existe um grupo com este nome");

            group.CreatedAt = DateTime.UtcNow;
            group.LastModifiedAt = DateTime.UtcNow;
            groups.Add(group);

            var newJson = JsonSerializer.Serialize(groups);
            File.WriteAllText(GroupsFilePath, newJson);

            return Results.Ok(group);
        });

        // Atualizar grupo
        groupApi.MapPut("/{name}", (string name, SiteGroupDto group) =>
        {
            if (!File.Exists(GroupsFilePath))
                return Results.NotFound();

            var json = File.ReadAllText(GroupsFilePath);
            var groups = JsonSerializer.Deserialize<List<SiteGroupDto>>(json);
            var existingGroup = groups?.FirstOrDefault(g => g.Name == name);
            
            if (existingGroup == null)
                return Results.NotFound();

            existingGroup.SiteNames = group.SiteNames;
            existingGroup.LastModifiedAt = DateTime.UtcNow;

            var newJson = JsonSerializer.Serialize(groups);
            File.WriteAllText(GroupsFilePath, newJson);

            return Results.Ok(existingGroup);
        });

        // Deletar grupo
        groupApi.MapDelete("/{name}", (string name) =>
        {
            if (!File.Exists(GroupsFilePath))
                return Results.NotFound();

            var json = File.ReadAllText(GroupsFilePath);
            var groups = JsonSerializer.Deserialize<List<SiteGroupDto>>(json);
            var group = groups?.FirstOrDefault(g => g.Name == name);
            
            if (group == null)
                return Results.NotFound();

            groups.Remove(group);
            var newJson = JsonSerializer.Serialize(groups);
            File.WriteAllText(GroupsFilePath, newJson);

            return Results.Ok();
        });

        // Iniciar todos os sites do grupo
        groupApi.MapPost("/{name}/start", async (string name) =>
        {
            if (!File.Exists(GroupsFilePath))
                return Results.NotFound();

            var json = File.ReadAllText(GroupsFilePath);
            var groups = JsonSerializer.Deserialize<List<SiteGroupDto>>(json);
            var group = groups?.FirstOrDefault(g => g.Name == name);
            
            if (group == null)
                return Results.NotFound();

            using var serverManager = new ServerManager();
            foreach (var siteName in group.SiteNames)
            {
                var site = serverManager.Sites[siteName];
                if (site != null)
                    site.Start();
            }

            return Results.Ok();
        });

        // Parar todos os sites do grupo
        groupApi.MapPost("/{name}/stop", async (string name) =>
        {
            if (!File.Exists(GroupsFilePath))
                return Results.NotFound();

            var json = File.ReadAllText(GroupsFilePath);
            var groups = JsonSerializer.Deserialize<List<SiteGroupDto>>(json);
            var group = groups?.FirstOrDefault(g => g.Name == name);
            
            if (group == null)
                return Results.NotFound();

            using var serverManager = new ServerManager();
            foreach (var siteName in group.SiteNames)
            {
                var site = serverManager.Sites[siteName];
                if (site != null)
                    site.Stop();
            }

            return Results.Ok();
        });
    }
}