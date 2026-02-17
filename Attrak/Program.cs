using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

namespace Attrak
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.RootComponents.Add<App>("#app");
            builder.RootComponents.Add<HeadOutlet>("head::after");

           //builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
            builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.Configuration["Url:Address"] ) });

            // Register frontend services
            builder.Services.AddScoped<Attrak.Services.IAuthService, Attrak.Services.AuthService>();
            builder.Services.AddScoped<Attrak.Services.ThemeService>();

            await builder.Build().RunAsync();
        }
    }
}
