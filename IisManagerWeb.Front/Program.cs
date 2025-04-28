using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components;
using IisManagerWeb.Front;
using MudBlazor;
using MudBlazor.Services;
using IisManagerWeb.Front.Services;
using IisManagerWeb.Front.Components;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
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

builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
    config.SnackbarConfiguration.PreventDuplicates = false;
    config.SnackbarConfiguration.NewestOnTop = false;
    config.SnackbarConfiguration.ShowCloseIcon = true;
    config.SnackbarConfiguration.VisibleStateDuration = 5000;
    config.SnackbarConfiguration.HideTransitionDuration = 500;
    config.SnackbarConfiguration.ShowTransitionDuration = 500;
    config.SnackbarConfiguration.SnackbarVariant = Variant.Filled;
});

builder.Services.AddScoped<IisManagerWeb.Front.Services.SettingsService>();
builder.Services.AddScoped<IisManagerWeb.Front.Services.MetricsService>();
builder.Services.AddScoped<IisManagerWeb.Front.Services.SiteService>();
builder.Services.AddScoped<IisManagerWeb.Front.Services.UploadFileService>();

await builder.Build().RunAsync();
