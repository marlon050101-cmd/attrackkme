using Microsoft.JSInterop;

namespace Attrak.Services
{
    public class ThemeService
    {
        private readonly IJSRuntime _jsRuntime;
        private bool _isDarkMode;

        public bool IsDarkMode => _isDarkMode;
        public event Action? OnThemeChanged;

        public ThemeService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        public async Task InitializeAsync()
        {
            var savedTheme = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "theme");
            _isDarkMode = savedTheme == "dark";
            NotifyThemeChanged();
        }

        public async Task ToggleThemeAsync()
        {
            _isDarkMode = !_isDarkMode;
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "theme", _isDarkMode ? "dark" : "light");
            NotifyThemeChanged();
        }

        private void NotifyThemeChanged()
        {
            OnThemeChanged?.Invoke();
        }
    }
}
