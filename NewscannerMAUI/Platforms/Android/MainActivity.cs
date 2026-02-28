using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using AndroidX.Core.OS;

namespace NewscannerMAUI
{
    // Use the main MAUI theme here instead of the splash theme so that
    // the correct root layout (including the NavigationRootManager host views)
    // is applied to this activity. This avoids "No view found for id ... id/left"
    // when MAUI tries to attach its navigation fragment.
    [Activity(Theme = "@style/Maui.MainTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density, WindowSoftInputMode = SoftInput.AdjustResize)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            // Always start with a fresh fragment/navigation state to avoid
            // 'No view found for id ... id/left' crashes after process recreation.
            base.OnCreate(null);

            // Hide the native Android action bar that shows the app title (black bar with "NewscannerMAUI")
            try
            {
                SupportActionBar?.Hide();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error hiding action bar: {ex.Message}");
            }

            // Fix keyboard lag on Android 14
            if (Build.VERSION.SdkInt >= BuildVersionCodes.UpsideDownCake) // Android 14+
            {
                Window?.SetSoftInputMode(SoftInput.AdjustResize | SoftInput.StateHidden);
            }
            
            // Auto-setup permissions with better handling
            SetupPermissions();
            
            // Also request permissions when activity resumes
            RequestPermissionsOnResume();
        }

        protected override void OnSaveInstanceState(Bundle outState)
        {
            // Intentionally DO NOT call base.OnSaveInstanceState here.
            // Saving fragment state is what leads to the NavigationRootManager
            // trying to restore into the non-existent 'left' container.
        }
        
        protected override void OnResume()
        {
            base.OnResume();
            
            // Check and request permissions every time the app comes to foreground
            RequestPermissionsOnResume();
        }

        private void SetupPermissions()
        {
            try
            {
                // Define required runtime permissions
                var permissions = new List<string>
                {
                    "android.permission.CAMERA",
                    "android.permission.INTERNET",
                    "android.permission.ACCESS_NETWORK_STATE",
                    "android.permission.RECORD_AUDIO",
                    "android.permission.MODIFY_AUDIO_SETTINGS",
                    "android.permission.VIBRATE"
                };

                // Storage/media permissions: prompt on startup like audio
                if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu) // Android 13+
                {
                    permissions.Add("android.permission.READ_MEDIA_IMAGES");
                    permissions.Add("android.permission.READ_MEDIA_VIDEO");
                    permissions.Add("android.permission.READ_MEDIA_AUDIO");
                }
                else
                {
                    // Android 12 and below
                    permissions.Add("android.permission.READ_EXTERNAL_STORAGE");
                    permissions.Add("android.permission.WRITE_EXTERNAL_STORAGE");
                }

                // Check Android version for different permission handling
                if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu) // Android 13+
                {
                    // For Android 13+, some permissions are automatically granted
                    RequestPermissionsForAndroid13Plus(permissions.ToArray());
                }
                else
                {
                    // For older Android versions
                    RequestPermissionsLegacy(permissions.ToArray());
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting up permissions: {ex.Message}");
            }
        }

        private void RequestPermissionsForAndroid13Plus(string[] permissions)
        {
            var permissionsToRequest = new List<string>();
            
            foreach (var permission in permissions)
            {
                if (ContextCompat.CheckSelfPermission(this, permission) != Permission.Granted)
                {
                    permissionsToRequest.Add(permission);
                }
            }

            if (permissionsToRequest.Count > 0)
            {
                // Request permissions with better user experience
                ActivityCompat.RequestPermissions(this, permissionsToRequest.ToArray(), 100);
            }
        }

        private void RequestPermissionsLegacy(string[] permissions)
        {
            var permissionsToRequest = new List<string>();
            
            foreach (var permission in permissions)
            {
                if (ContextCompat.CheckSelfPermission(this, permission) != Permission.Granted)
                {
                    permissionsToRequest.Add(permission);
                }
            }

            if (permissionsToRequest.Count > 0)
            {
                ActivityCompat.RequestPermissions(this, permissionsToRequest.ToArray(), 100);
            }
        }

        private void RequestPermissionsOnResume()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Checking permissions on app resume...");
                
                // Define required runtime permissions (include storage/media as appropriate)
                var permissions = new List<string>
                {
                    "android.permission.CAMERA",
                    "android.permission.INTERNET",
                    "android.permission.ACCESS_NETWORK_STATE",
                    "android.permission.RECORD_AUDIO",
                    "android.permission.MODIFY_AUDIO_SETTINGS",
                    "android.permission.VIBRATE"
                };

                if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
                {
                    permissions.Add("android.permission.READ_MEDIA_IMAGES");
                    permissions.Add("android.permission.READ_MEDIA_VIDEO");
                    permissions.Add("android.permission.READ_MEDIA_AUDIO");
                }
                else
                {
                    permissions.Add("android.permission.READ_EXTERNAL_STORAGE");
                    permissions.Add("android.permission.WRITE_EXTERNAL_STORAGE");
                }

                var permissionsToRequest = new List<string>();
                
                foreach (var permission in permissions)
                {
                    if (ContextCompat.CheckSelfPermission(this, permission) != Permission.Granted)
                    {
                        permissionsToRequest.Add(permission);
                    }
                }

            if (permissionsToRequest.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"Requesting {permissionsToRequest.Count} missing permissions on resume");
                ActivityCompat.RequestPermissions(this, permissionsToRequest.ToArray(), 200);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("All permissions already granted on resume");
            }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error requesting permissions on resume: {ex.Message}");
            }
        }

        // Handle permission results automatically
        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            
            if (requestCode == 100 || requestCode == 200)
            {
                var allGranted = true;
                for (int i = 0; i < permissions.Length; i++)
                {
                    if (grantResults[i] != Permission.Granted)
                    {
                        allGranted = false;
                        System.Diagnostics.Debug.WriteLine($"Permission denied: {permissions[i]}");
                    }
                }
                
                if (allGranted)
                {
                    System.Diagnostics.Debug.WriteLine("🎉 All permissions granted successfully!");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ Some permissions were denied. App may not function properly.");
                }
            }
        }
        
        // Override to handle fragment lifecycle issues
        protected override void OnDestroy()
        {
            try
            {
                // Clean up any fragments that might be causing issues
                if (SupportFragmentManager != null)
                {
                    var fragments = SupportFragmentManager.Fragments;
                    if (fragments != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Cleaning up {fragments.Count} fragments on destroy");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during OnDestroy: {ex.Message}");
            }
            finally
            {
                base.OnDestroy();
            }
        }
        
        // Handle configuration changes that might cause fragment issues
        public override void OnConfigurationChanged(Android.Content.Res.Configuration newConfig)
        {
            try
            {
                base.OnConfigurationChanged(newConfig);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during configuration change: {ex.Message}");
                // Continue execution even if there's an error
            }
        }
    }
}
