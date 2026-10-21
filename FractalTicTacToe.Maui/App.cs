using Microsoft.Maui;
using Microsoft.Maui.Controls;

namespace FractalTicTacToe.Maui
{
    public class App : Application
    {
        public App()
        {
            UserAppTheme = AppTheme.Dark;
            MainPage = new MainPage();
        }

        protected override Window CreateWindow(IActivationState activationState)
        {
            Window window = base.CreateWindow(activationState);
            window.Title = "Gatote Fractal • Muerte Súbita";
            // En escritorio (Windows/Mac), abrir en proporción vertical móvil (9:16)
            window.Width = 460;
            window.Height = 880;
            return window;
        }
    }
}
