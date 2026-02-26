using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using ZXing.Net.Maui.Controls;
using NewscannerMAUI.Services;

namespace NewscannerMAUI
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseBarcodeReader()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();

            // Configure HttpClient with base URL and SSL handling
            builder.Services.AddHttpClient("AttrakAPI", client =>
            {
                // Use HTTPS with SSL bypass to handle certificate issues
                client.BaseAddress = new Uri("https://attrack-sr9l.onrender.com/" +
                    "");
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                client.DefaultRequestHeaders.Add("User-Agent", "NewscannerMAUI/1.0");
                // Add timeout
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
#if ANDROID
                // Use Android-specific handler with SSL bypass
                var handler = new Xamarin.Android.Net.AndroidMessageHandler();
                // Disable SSL verification for development
                handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
                return handler;
#else
                var handler = new HttpClientHandler();
                handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
                return handler;
#endif
            });
            
            // Also add a default HttpClient
            builder.Services.AddHttpClient();
            
            // Register services
            builder.Services.AddSingleton<OfflineDataService>();
            builder.Services.AddSingleton<AuthService>();
            builder.Services.AddSingleton<ConnectionStatusService>();
            builder.Services.AddSingleton<HybridQRValidationService>();
            builder.Services.AddSingleton<QRScannerService>();

#if DEBUG
    		builder.Services.AddBlazorWebViewDeveloperTools();
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
