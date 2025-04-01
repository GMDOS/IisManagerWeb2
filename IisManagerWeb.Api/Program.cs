using System.Text.Json.Serialization;
using IisManagerWeb.Api.Controllers;
using System.Security.Principal;
using System.Diagnostics;
using Microsoft.Web.Administration;
using IisManagerWeb.Shared.Models;
using System.Collections.Generic;

var builder = WebApplication.CreateSlimBuilder(args);

// Adicionar suporte a CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(
        policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
});

// Verificar privilégios de administrador
if (!PrivilegeHelper.IsRunningAsAdministrator())
{
    Console.WriteLine("A aplicação precisa ser executada como administrador.");
    Console.WriteLine("Solicitando elevação de privilégios...");
    
    try
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = true,
            WorkingDirectory = Environment.CurrentDirectory,
            FileName = Process.GetCurrentProcess().MainModule?.FileName,
            Verb = "runas"
        };

        Process.Start(startInfo);
        Environment.Exit(0);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro ao tentar elevar privilégios: {ex.Message}");
        Environment.Exit(1);
    }
}

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

var app = builder.Build();

// Habilitar CORS
app.UseCors();

app.GetSiteRoutes();
app.GetSiteGroupRoutes();

app.Run();

[JsonSerializable(typeof(SiteCollection))]
[JsonSerializable(typeof(Site))]
[JsonSerializable(typeof(ApplicationPoolCollection))]
[JsonSerializable(typeof(ApplicationPool))]
[JsonSerializable(typeof(List<SiteDto>))]
[JsonSerializable(typeof(SiteDto))]
[JsonSerializable(typeof(List<ApplicationDto>))]
[JsonSerializable(typeof(ApplicationDto))]
[JsonSerializable(typeof(List<VirtualDirectoryDto>))]
[JsonSerializable(typeof(VirtualDirectoryDto))]
[JsonSerializable(typeof(List<BindingDto>))]
[JsonSerializable(typeof(BindingDto))]
[JsonSerializable(typeof(ObjectState))]
[JsonSerializable(typeof(List<SiteGroupDto>))]
[JsonSerializable(typeof(SiteGroupDto))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}

public static class PrivilegeHelper
{
    public static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}