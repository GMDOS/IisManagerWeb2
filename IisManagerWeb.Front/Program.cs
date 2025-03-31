using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using IisManagerWeb.Front;
using IisManagerWeb.Front.Services;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Services.AddMudServices();

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri("http://localhost:5135/") });
builder.Services.AddScoped<SiteService>();
builder.Services.AddScoped<IUploadFileService, UploadFileService>();

await builder.Build().RunAsync();
