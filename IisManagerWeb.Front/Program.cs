using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components;
using IisManagerWeb.Front;
using IisManagerWeb.Front.Services;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Services.AddMudServices();

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => 
{
    var navigationManager = sp.GetRequiredService<NavigationManager>();
#if DEBUG
    return new HttpClient { BaseAddress = new Uri("https://localhost:5135/") };
#else
    return new HttpClient { BaseAddress = new Uri(navigationManager.BaseUri + "api/") };
#endif
});
builder.Services.AddScoped<IisManagerWeb.Front.Services.SettingsService>();
builder.Services.AddScoped<IisManagerWeb.Front.Services.MetricsService>();
builder.Services.AddScoped<IisManagerWeb.Front.Services.SiteService>();
builder.Services.AddScoped<IisManagerWeb.Front.Services.IUploadFileService, IisManagerWeb.Front.Services.UploadFileService>();

await builder.Build().RunAsync();
