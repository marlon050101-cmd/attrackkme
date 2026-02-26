using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace NewscannerMAUI.Pages
{
    public partial class SimpleCameraPage : ContentPage
    {
        private bool _isScanning = true;
        private CameraLocation _currentCameraLocation = CameraLocation.Rear;
        private bool _isTorchOn = false;

        public event EventHandler<string>? QRCodeScanned;

        public SimpleCameraPage()
        {
            InitializeComponent();
        }

        private void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
        {
            if (e.Results?.Any() == true)
            {
                var result = e.Results.FirstOrDefault();
                if (result != null && !string.IsNullOrEmpty(result.Value))
                {
                    // Update status
                    statusLabel.Text = $"QR Code detected: {result.Value}";
                    statusLabel.TextColor = Colors.Green;
                    
                    // Notify parent
                    QRCodeScanned?.Invoke(this, result.Value);
                    
                    // Show alert
                    Application.Current?.MainPage?.DisplayAlert("QR Code Scanned", $"Code: {result.Value}", "OK");
                }
            }
        }

        private void OnStopClicked(object? sender, EventArgs e)
        {
            _isScanning = false;
            cameraView.IsDetecting = false;
            statusLabel.Text = "Scanning stopped";
            statusLabel.TextColor = Colors.Orange;
        }

        private void OnTorchClicked(object? sender, EventArgs e)
        {
            _isTorchOn = !_isTorchOn;
            cameraView.IsTorchOn = _isTorchOn;
            
            torchButton.Text = _isTorchOn ? "Flashlight ON" : "Flashlight";
            torchButton.BackgroundColor = _isTorchOn ? Colors.Yellow : Colors.Orange;
            
            statusLabel.Text = _isTorchOn ? "Flashlight turned ON" : "Flashlight turned OFF";
            statusLabel.TextColor = Colors.Blue;
        }

        private void OnSwitchCameraClicked(object? sender, EventArgs e)
        {
            _currentCameraLocation = _currentCameraLocation == CameraLocation.Rear 
                ? CameraLocation.Front 
                : CameraLocation.Rear;
            
            cameraView.CameraLocation = _currentCameraLocation;
            
            var cameraType = _currentCameraLocation == CameraLocation.Rear ? "rear" : "front";
            statusLabel.Text = $"Switched to {cameraType} camera";
            statusLabel.TextColor = Colors.Blue;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            if (!_isScanning)
            {
                _isScanning = true;
                cameraView.IsDetecting = true;
                statusLabel.Text = "Camera ready - point at QR code";
                statusLabel.TextColor = Colors.Green;
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _isScanning = false;
            cameraView.IsDetecting = false;
        }
    }
}
