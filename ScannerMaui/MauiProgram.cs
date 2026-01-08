using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using ZXing.Net.Maui.Controls;

namespace ScannerMaui
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
                client.BaseAddress = new Uri("https://attrack.onrender.com/");
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                client.DefaultRequestHeaders.Add("User-Agent", "ScannerMaui/1.0");
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
            
            // Register services
            builder.Services.AddSingleton<ScannerMaui.Services.OfflineDataService>();
            builder.Services.AddSingleton<ScannerMaui.Services.AuthService>();
            builder.Services.AddSingleton<ScannerMaui.Services.ConnectionStatusService>();
            builder.Services.AddSingleton<ScannerMaui.Services.HybridQRValidationService>();
            builder.Services.AddSingleton<ScannerMaui.Services.QRScannerService>();

#if DEBUG
    		builder.Services.AddBlazorWebViewDeveloperTools();
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
