using ScannerMaui.Pages;
using ScannerMaui.Services;

namespace ScannerMaui
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
        }

        public static async Task NavigateToCameraPage()
        {
            try
            {
                var cameraPage = new NativeQRScannerPage();
                
                // Use NavigationPage for proper navigation
                if (Application.Current?.MainPage is NavigationPage navPage)
                {
                    await navPage.PushAsync(cameraPage);
                }
                else
                {
                    // Fallback: create a new navigation page
                    var navigationPage = new NavigationPage(cameraPage);
                    Application.Current!.MainPage = navigationPage;
                }
            }
#if ANDROID
            catch (Java.Lang.IllegalArgumentException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Navigation fragment error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine("This is likely the line1 view ID error - attempting recovery...");
                
                // Try to recover by recreating the main page
                try
                {
                    Application.Current!.MainPage = new MainPage();
                    System.Diagnostics.Debug.WriteLine("Recovery: Recreated MainPage");
                }
                catch (Exception recoveryEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Recovery failed: {recoveryEx.Message}");
                }
            }
#endif
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error navigating to camera: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
    }
}
