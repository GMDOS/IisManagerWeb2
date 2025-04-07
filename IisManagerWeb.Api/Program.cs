using System.Text.Json.Serialization;
using IisManagerWeb.Api.Controllers;
using System.Security.Principal;
using System.Diagnostics;
using Microsoft.Web.Administration;
using IisManagerWeb.Shared.Models;
using System.Collections.Generic;
using IisManagerWeb.Api.Services;

var builder = WebApplication.CreateSlimBuilder(args);

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

if (!PrivilegeHelper.IsPrivilegedUser())
{
    Console.WriteLine("A aplicação precisa ser executada como administrador.");
    Console.WriteLine("Solicitando elevação de privilégios...");
    
    try
    {
        var startInfo = new ProcessStartInfo();
        if (OperatingSystem.IsWindows())
        {
            startInfo.UseShellExecute = true;
            startInfo.WorkingDirectory = Environment.CurrentDirectory;
            startInfo.FileName = Process.GetCurrentProcess().MainModule?.FileName;
            startInfo.Verb = "runas";
        }
        if (OperatingSystem.IsLinux())
        {
            startInfo.UseShellExecute = true;
            startInfo.WorkingDirectory = Environment.CurrentDirectory;
            startInfo.FileName = "sudo";
            startInfo.Arguments = Process.GetCurrentProcess().MainModule?.FileName;
        }

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
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

builder.Services.AddSingleton<ServerMonitorService>();
builder.Services.AddHostedService<ServerMonitorBackgroundService>();

var app = builder.Build();

app.UseCors();
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

app.GetSiteRoutes();
app.GetSiteGroupRoutes();
app.GetSettingsRoutes();
app.GetMetricsRoutes();

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
[JsonSerializable(typeof(List<ClientFileInfo>))]
[JsonSerializable(typeof(List<ClientFileInfo?>))]
[JsonSerializable(typeof(ClientFileInfo))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTime?))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(ManagerSettings))]
[JsonSerializable(typeof(ServerMetrics))]
[JsonSerializable(typeof(List<ServerMetrics>))]
[JsonSerializable(typeof(ServerMetricsHistory))]
[JsonSerializable(typeof(SiteMetrics))]
[JsonSerializable(typeof(List<SiteMetrics>))]
[JsonSerializable(typeof(MetricsWebSocketPacket))]
[JsonSerializable(typeof(FileCheckResponse))]

internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}

public static class PrivilegeHelper
{
    public static bool IsPrivilegedUser()
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                return Environment.UserName == "root";
            }
            catch
            {
                return false;
            }
        }
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator) || principal.IsInRole(WindowsBuiltInRole.PowerUser) || principal.IsInRole(WindowsBuiltInRole.SystemOperator);
        }
        return false;
    }
}