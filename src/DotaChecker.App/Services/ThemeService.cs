using DotaChecker.Core.Configuration;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace DotaChecker.App.Services;

/// <summary>Применение выбранной темы через WPF-UI с синхронизацией Mica-подложки.</summary>
internal static class ThemeService
{
    public static void Apply(ThemeMode mode)
    {
        switch (mode)
        {
            case ThemeMode.Light:
                ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.Mica);
                break;

            case ThemeMode.Dark:
                ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.Mica);
                break;

            default:
                ApplicationThemeManager.ApplySystemTheme();
                ApplicationThemeManager.Apply(
                    ApplicationThemeManager.GetAppTheme(),
                    WindowBackdropType.Mica);
                break;
        }
    }
}
