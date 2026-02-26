using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace NewscannerMAUI.Pages
{
    public partial class CameraPage : ContentPage
    {
        private bool _isScanning = true;
        private CameraLocation _currentCameraLocation = CameraLocation.Rear;

        public event EventHandler<string>? QRCodeScanned;

        public CameraPage()
        {
            InitializeComponent();
            UpdateUI();
        }

        private void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
        {
            if (e.Results?.Any() == true)
            {
                var result = e.Results.FirstOrDefault();
                if (result != null && !string.IsNullOrEmpty(result.Value))
                {
                    // Show the scanned code
                    scannedCodeLabel.Text = $"Scanned: {result.Value}";
                    scannedCodeLabel.IsVisible = true;
                    
                    // Update status
                    statusLabel.Text = "QR Code detected!";
                    statusLabel.TextColor = Colors.Green;
                    
                    // Notify parent
                    QRCodeScanned?.Invoke(this, result.Value);
                    
                    // Auto-hide after 3 seconds
                    Task.Delay(3000).ContinueWith(_ =>
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            scannedCodeLabel.IsVisible = false;
                            statusLabel.Text = "Camera ready - point at QR code";
                            statusLabel.TextColor = Colors.Green;
                        });
                    });
                }
            }
        }

        private void OnStopClicked(object? sender, EventArgs e)
        {
            _isScanning = false;
            cameraBarcodeReaderView.IsDetecting = false;
            statusLabel.Text = "Scanning stopped";
            statusLabel.TextColor = Colors.Orange;
            UpdateUI();
        }

        private void OnSwitchCameraClicked(object? sender, EventArgs e)
        {
            _currentCameraLocation = _currentCameraLocation == CameraLocation.Rear 
                ? CameraLocation.Front 
                : CameraLocation.Rear;
            
            cameraBarcodeReaderView.CameraLocation = _currentCameraLocation;
            
            var cameraType = _currentCameraLocation == CameraLocation.Rear ? "rear" : "front";
            statusLabel.Text = $"Switched to {cameraType} camera";
            statusLabel.TextColor = Colors.Blue;
            
            // Reset status after 2 seconds
            Task.Delay(2000).ContinueWith(_ =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    statusLabel.Text = "Camera ready - point at QR code";
                    statusLabel.TextColor = Colors.Green;
                });
            });
        }

        private void UpdateUI()
        {
            stopButton.IsVisible = _isScanning;
            switchButton.IsVisible = _isScanning;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            if (!_isScanning)
            {
                _isScanning = true;
                cameraBarcodeReaderView.IsDetecting = true;
                statusLabel.Text = "Camera ready - point at QR code";
                statusLabel.TextColor = Colors.Green;
                UpdateUI();
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _isScanning = false;
            cameraBarcodeReaderView.IsDetecting = false;
        }
    }
}
