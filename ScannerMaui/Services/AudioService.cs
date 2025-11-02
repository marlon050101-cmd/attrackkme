using System.Diagnostics;
using Microsoft.Maui.Controls;

#if ANDROID
using Android.Media;
#endif

namespace ScannerMaui.Services
{
    public class AudioService
    {
        public static async Task PlayBeepSoundAsync(bool isSuccess = true)
        {
            try
            {
                if (DeviceInfo.Platform == DevicePlatform.Android)
                {
                    await PlayAndroidBeepAsync(isSuccess);
                }
                else if (DeviceInfo.Platform == DevicePlatform.iOS)
                {
                    await PlayiOSBeepAsync(isSuccess);
                }
                else
                {
                    // Desktop/Windows - use console beep
                    PlayDesktopBeep(isSuccess);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error playing beep sound: {ex.Message}");
            }
        }
        
        private static async Task PlayAndroidBeepAsync(bool isSuccess)
        {
            try
            {
                // For Android, we'll use ToneGenerator to play a beep sound
                // This requires the audio permissions we added
                Debug.WriteLine($"Playing Android beep sound - Success: {isSuccess}");
                
                if (isSuccess)
                {
                    // Success beep - higher frequency, shorter duration
                    await PlayToneAsync(null, 800, 200); // 800Hz for 200ms
                }
                else
                {
                    // Error beep - lower frequency, longer duration
                    await PlayToneAsync(null, 400, 300); // 400Hz for 300ms
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error playing Android beep: {ex.Message}");
            }
        }
        
        private static async Task PlayToneAsync(object mediaPlayer, int frequency, int duration)
        {
            try
            {
#if ANDROID

                // Generate a simple tone using Android's ToneGenerator
                var toneGenerator = new ToneGenerator(Android.Media.Stream.System, 100);
                
                if (frequency >= 600)
                {
                    // Success tone - use a higher pitch tone
                    toneGenerator.StartTone(Tone.PropBeep, duration);
                }
                else
                {
                    // Error tone - use a lower pitch tone
                    toneGenerator.StartTone(Tone.CdmaAbbrAlert, duration);
                }
                
                await Task.Delay(duration);
                toneGenerator.Release();
#else
                // Fallback for non-Android platforms
                await Task.Delay(duration);
#endif
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error generating tone: {ex.Message}");
            }
        }
        
        private static async Task PlayiOSBeepAsync(bool isSuccess)
        {
            try
            {
                Debug.WriteLine($"Playing iOS beep sound - Success: {isSuccess}");
                
                // For iOS, we can use system sounds
                if (isSuccess)
                {
                    // Success sound
                    await PlayiOSSystemSoundAsync(1000); // System sound ID for success
                }
                else
                {
                    // Error sound
                    await PlayiOSSystemSoundAsync(1001); // System sound ID for error
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error playing iOS beep: {ex.Message}");
            }
        }
        
        private static async Task PlayiOSSystemSoundAsync(int soundId)
        {
            try
            {
                // iOS system sound implementation
                // This would use AudioServicesPlaySystemSound in a real implementation
                Debug.WriteLine($"Playing iOS system sound: {soundId}");
                await Task.Delay(100);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error playing iOS system sound: {ex.Message}");
            }
        }
        
        private static void PlayDesktopBeep(bool isSuccess)
        {
            try
            {
                if (isSuccess)
                {
                    // Success beep - higher frequency
                    Console.Beep(800, 200); // 800Hz for 200ms
                }
                else
                {
                    // Error beep - lower frequency
                    Console.Beep(400, 300); // 400Hz for 300ms
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error playing desktop beep: {ex.Message}");
            }
        }
        
        public static async Task PlaySuccessBeepAsync()
        {
            await PlayBeepSoundAsync(true);
        }
        
        public static async Task PlayErrorBeepAsync()
        {
            await PlayBeepSoundAsync(false);
        }
    }
}
