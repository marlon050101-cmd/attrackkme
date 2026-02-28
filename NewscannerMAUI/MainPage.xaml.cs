using NewscannerMAUI.Pages;
using NewscannerMAUI.Services;

namespace NewscannerMAUI
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();

            // Ensure the native navigation bar is fully hidden for this page
            NavigationPage.SetHasNavigationBar(this, false);
        }

        public static async Task NavigateToCameraPage()
        {
            try
            {
                // Use the service-based approach for consistency and lock management
                var scannerService = IPlatformApplication.Current?.Services.GetService<QRScannerService>();
                if (scannerService != null)
                {
                    await scannerService.OpenNativeQRScanner();
                }
                else
                {
                    // Fallback if service resolution fails
                    var cameraPage = new NativeQRScannerPage();
                    if (Application.Current?.MainPage is NavigationPage navPage)
                    {
                        await navPage.PushAsync(cameraPage);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error navigating to camera: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
    }
}
