using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using System.Diagnostics;

namespace NewscannerMAUI.Services
{
    public class PermissionService
    {
        public static async Task<bool> RequestCameraPermissionAsync()
        {
            try
            {
                var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
                
                if (status == PermissionStatus.Granted)
                {
                    Debug.WriteLine("Camera permission already granted");
                    return true;
                }
                
                if (status == PermissionStatus.Denied)
                {
                    Debug.WriteLine("Camera permission denied");
                    return false;
                }
                
                // Request permission
                status = await Permissions.RequestAsync<Permissions.Camera>();
                
                if (status == PermissionStatus.Granted)
                {
                    Debug.WriteLine("Camera permission granted");
                    return true;
                }
                else
                {
                    Debug.WriteLine("Camera permission denied by user");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error requesting camera permission: {ex.Message}");
                return false;
            }
        }
        
        public static async Task<bool> RequestAudioPermissionAsync()
        {
            try
            {
                var status = await Permissions.CheckStatusAsync<Permissions.Microphone>();
                
                if (status == PermissionStatus.Granted)
                {
                    Debug.WriteLine("Audio permission already granted");
                    return true;
                }
                
                if (status == PermissionStatus.Denied)
                {
                    Debug.WriteLine("Audio permission denied");
                    return false;
                }
                
                // Request permission
                status = await Permissions.RequestAsync<Permissions.Microphone>();
                
                if (status == PermissionStatus.Granted)
                {
                    Debug.WriteLine("Audio permission granted");
                    return true;
                }
                else
                {
                    Debug.WriteLine("Audio permission denied by user");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error requesting audio permission: {ex.Message}");
                return false;
            }
        }
        
        public static async Task<bool> RequestStoragePermissionAsync()
        {
            try
            {
                var status = await Permissions.CheckStatusAsync<Permissions.StorageRead>();
                
                if (status == PermissionStatus.Granted)
                {
                    Debug.WriteLine("Storage permission already granted");
                    return true;
                }
                
                if (status == PermissionStatus.Denied)
                {
                    Debug.WriteLine("Storage permission denied");
                    return false;
                }
                
                // Request permission
                status = await Permissions.RequestAsync<Permissions.StorageRead>();
                
                if (status == PermissionStatus.Granted)
                {
                    Debug.WriteLine("Storage permission granted");
                    return true;
                }
                else
                {
                    Debug.WriteLine("Storage permission denied by user");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error requesting storage permission: {ex.Message}");
                return false;
            }
        }
        
        public static async Task<bool> RequestNetworkPermissionAsync()
        {
            try
            {
                // Network permissions are typically granted automatically in .NET MAUI
                // We'll just check if we can access network connectivity
                Debug.WriteLine("Network permissions are automatically granted in .NET MAUI");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking network permissions: {ex.Message}");
                return true; // Assume network is available
            }
        }
        
        public static async Task<bool> RequestVibrationPermissionAsync()
        {
            try
            {
                var status = await Permissions.CheckStatusAsync<Permissions.Vibrate>();
                
                if (status == PermissionStatus.Granted)
                {
                    Debug.WriteLine("Vibration permission already granted");
                    return true;
                }
                
                if (status == PermissionStatus.Denied)
                {
                    Debug.WriteLine("Vibration permission denied");
                    return false;
                }
                
                // Request permission
                status = await Permissions.RequestAsync<Permissions.Vibrate>();
                
                if (status == PermissionStatus.Granted)
                {
                    Debug.WriteLine("Vibration permission granted");
                    return true;
                }
                else
                {
                    Debug.WriteLine("Vibration permission denied by user");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error requesting vibration permission: {ex.Message}");
                return false;
            }
        }
        
        public static async Task<bool> RequestAllRequiredPermissionsAsync()
        {
            try
            {
                Debug.WriteLine("=== REQUESTING ALL REQUIRED PERMISSIONS ===");
                
                var results = new List<bool>();
                
                // Request all permissions in sequence
                results.Add(await RequestCameraPermissionAsync());
                results.Add(await RequestAudioPermissionAsync());
                results.Add(await RequestStoragePermissionAsync());
                results.Add(await RequestNetworkPermissionAsync());
                results.Add(await RequestVibrationPermissionAsync());
                
                var allGranted = results.All(r => r);
                
                if (allGranted)
                {
                    Debug.WriteLine("✅ ALL PERMISSIONS GRANTED SUCCESSFULLY!");
                }
                else
                {
                    Debug.WriteLine("⚠️ Some permissions were denied. App functionality may be limited.");
                }
                
                return allGranted;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error requesting permissions: {ex.Message}");
                return false;
            }
        }
        
        public static async Task<bool> CheckAllPermissionsAsync()
        {
            try
            {
                var cameraStatus = await Permissions.CheckStatusAsync<Permissions.Camera>();
                var audioStatus = await Permissions.CheckStatusAsync<Permissions.Microphone>();
                var storageStatus = await Permissions.CheckStatusAsync<Permissions.StorageRead>();
                var vibrationStatus = await Permissions.CheckStatusAsync<Permissions.Vibrate>();
                
                var allGranted = cameraStatus == PermissionStatus.Granted && 
                                audioStatus == PermissionStatus.Granted &&
                                storageStatus == PermissionStatus.Granted &&
                                vibrationStatus == PermissionStatus.Granted;
                
                Debug.WriteLine($"Permission status - Camera: {cameraStatus}, Audio: {audioStatus}, Storage: {storageStatus}, Vibration: {vibrationStatus}");
                
                return allGranted;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking permissions: {ex.Message}");
                return false;
            }
        }
        
        public static async Task<bool> AutoRequestPermissionsOnAppStart()
        {
            try
            {
                Debug.WriteLine("=== AUTO-REQUESTING PERMISSIONS ON APP START ===");
                
                // Wait a moment for app to initialize
                await Task.Delay(2000);
                
                // Check if all permissions are already granted
                var allGranted = await CheckAllPermissionsAsync();
                if (allGranted)
                {
                    Debug.WriteLine("All permissions already granted - no need to request");
                    return true;
                }
                
                // Request all permissions automatically
                var result = await RequestAllRequiredPermissionsAsync();
                
                if (result)
                {
                    Debug.WriteLine("🎉 All permissions granted automatically!");
                }
                else
                {
                    Debug.WriteLine("⚠️ Some permissions denied - user may need to grant manually");
                }
                
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in auto-request permissions: {ex.Message}");
                return false;
            }
        }
    }
}
