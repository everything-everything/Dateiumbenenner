using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Control = System.Windows.Controls.Control;
using SystemColors = System.Windows.SystemColors;
using SystemFonts = System.Windows.SystemFonts;

namespace Dateiumbenenner
{
    /// <summary>Programmweites Erscheinungsbild (Standard oder Windows-XP-Stil), Auswahl in den Einstellungen.</summary>
    public static class ThemeManager
    {
        public enum Theme { Standard, WindowsXp }

        private static readonly Uri XpUri = new("pack://application:,,,/Dateiumbenenner;component/Themes/XpTheme.xaml");
        private static readonly string SettingsFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dateiumbenenner", "theme.txt");
        private static bool _hooked;

        public static Theme Current { get; private set; } = Theme.WindowsXp;

        public static void Initialize()
        {
            if (!_hooked)
            {
                _hooked = true;
                // Fensterhintergrund/Schrift als Ressourcenverweis: ohne Design fällt der Wert auf den Standard zurück
                EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) =>
                {
                    if (s is not Window w) return;
                    if (w.ReadLocalValue(Control.BackgroundProperty) == DependencyProperty.UnsetValue)
                        w.SetResourceReference(Control.BackgroundProperty, "XpWindowBackground");
                    if (w.ReadLocalValue(Control.FontFamilyProperty) == DependencyProperty.UnsetValue)
                        w.SetResourceReference(Control.FontFamilyProperty, "XpWindowFont");
                }));
            }
            var t = Theme.WindowsXp;
            try { if (File.Exists(SettingsFile)) Enum.TryParse(File.ReadAllText(SettingsFile).Trim(), out t); } catch { }
            Apply(t, save: false);
        }

        public static void Apply(Theme theme, bool save = true)
        {
            var app = System.Windows.Application.Current;
            var dicts = app.Resources.MergedDictionaries;
            foreach (var d in dicts.Where(d => d.Source == XpUri).ToList()) dicts.Remove(d);
            if (theme == Theme.WindowsXp) dicts.Add(new ResourceDictionary { Source = XpUri });
            app.Resources["XpWindowBackground"] = theme == Theme.WindowsXp
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEC, 0xE9, 0xD8))
                : SystemColors.WindowBrush;
            app.Resources["XpWindowFont"] = theme == Theme.WindowsXp
                ? new System.Windows.Media.FontFamily("Tahoma")
                : SystemFonts.MessageFontFamily;
            Current = theme;
            if (!save) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
                File.WriteAllText(SettingsFile, theme.ToString());
            }
            catch { }
        }
    }
}
