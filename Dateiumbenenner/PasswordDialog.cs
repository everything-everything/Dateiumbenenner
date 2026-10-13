using System.Windows;
using System.Windows.Controls;
using Dateiumbenenner.Engine;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace Dateiumbenenner
{
    /// <summary>Einfacher Dialog für Anmeldung, Passwort setzen/ändern/entfernen und Wiederherstellung.</summary>
    public class PasswordDialog : Window
    {
        public enum Mode { Login, Set, Change, Remove, Recover }

        private readonly Mode _mode;
        private readonly PasswordBox _pw1 = new() { Margin = new Thickness(0, 2, 0, 8) };
        private readonly PasswordBox _pw2 = new() { Margin = new Thickness(0, 2, 0, 8) };
        private readonly PasswordBox _pw3 = new() { Margin = new Thickness(0, 2, 0, 8) };
        private readonly TextBox _recovery = new() { Margin = new Thickness(0, 2, 0, 8) };
        private readonly TextBlock _error = new() { Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };

        /// <summary>Nach „Set“: der erzeugte Wiederherstellungsschlüssel.</summary>
        public string? RecoveryCode { get; private set; }

        public PasswordDialog(Mode mode)
        {
            _mode = mode;
            Title = mode switch
            {
                Mode.Login => "Dateiumbenenner – Anmeldung",
                Mode.Set => "Passwortschutz einrichten",
                Mode.Change => "Passwort ändern",
                Mode.Remove => "Passwortschutz entfernen",
                _ => "Mit Wiederherstellungsschlüssel entsperren"
            };
            Width = 420; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var panel = new StackPanel { Margin = new Thickness(14) };
            void Label(string t) => panel.Children.Add(new TextBlock { Text = t, TextWrapping = TextWrapping.Wrap });
            switch (mode)
            {
                case Mode.Login:
                    Label("Datenbank und gelernte Daten sind verschlüsselt. Bitte Passwort eingeben:");
                    panel.Children.Add(_pw1);
                    break;
                case Mode.Set:
                    Label("Alle Profile (Dokument-Datenbank, Lerndaten, Einstellungen, Plugin-Daten) werden verschlüsselt.\n\nNeues Passwort (mind. 8 Zeichen):");
                    panel.Children.Add(_pw1);
                    Label("Passwort wiederholen:");
                    panel.Children.Add(_pw2);
                    break;
                case Mode.Change:
                    Label("Aktuelles Passwort:"); panel.Children.Add(_pw3);
                    Label("Neues Passwort (mind. 8 Zeichen):"); panel.Children.Add(_pw1);
                    Label("Neues Passwort wiederholen:"); panel.Children.Add(_pw2);
                    break;
                case Mode.Remove:
                    Label("Alle Daten werden wieder unverschlüsselt gespeichert.\n\nAktuelles Passwort:");
                    panel.Children.Add(_pw3);
                    break;
                case Mode.Recover:
                    Label("Wiederherstellungsschlüssel (XXXX-XXXX-XXXX-XXXX-XXXX-XXXX):"); panel.Children.Add(_recovery);
                    Label("Neues Passwort (mind. 8 Zeichen):"); panel.Children.Add(_pw1);
                    Label("Neues Passwort wiederholen:"); panel.Children.Add(_pw2);
                    break;
            }
            panel.Children.Add(_error);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            if (mode == Mode.Login)
            {
                var forgot = new Button { Content = "Passwort vergessen…", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 8, 0) };
                forgot.Click += (_, _) =>
                {
                    var r = new PasswordDialog(Mode.Recover) { Owner = this };
                    if (r.ShowDialog() == true) { DialogResult = true; }
                };
                buttons.Children.Add(forgot);
            }
            var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            ok.Click += (_, _) => OnOk();
            var cancel = new Button { Content = mode == Mode.Login ? "Beenden" : "Abbrechen", Width = 80, IsCancel = true };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            Content = panel;
            Loaded += (_, _) => { if (mode == Mode.Recover) _recovery.Focus(); else if (mode is Mode.Change or Mode.Remove) _pw3.Focus(); else _pw1.Focus(); };
        }

        private bool CheckNew()
        {
            if (_pw1.Password.Length < 8) { _error.Text = "Das Passwort muss mindestens 8 Zeichen lang sein."; return false; }
            if (_pw1.Password != _pw2.Password) { _error.Text = "Die Passwörter stimmen nicht überein."; return false; }
            return true;
        }

        private void OnOk()
        {
            _error.Text = "";
            Cursor = System.Windows.Input.Cursors.Wait;
            try
            {
                switch (_mode)
                {
                    case Mode.Login:
                        if (!SecureStorage.Unlock(_pw1.Password)) { _error.Text = "Falsches Passwort."; _pw1.Clear(); return; }
                        break;
                    case Mode.Set:
                        if (!CheckNew()) return;
                        RecoveryCode = SecureStorage.Enable(_pw1.Password);
                        break;
                    case Mode.Change:
                        if (!CheckNew()) return;
                        if (!SecureStorage.ChangePassword(_pw3.Password, _pw1.Password)) { _error.Text = "Aktuelles Passwort ist falsch."; return; }
                        break;
                    case Mode.Remove:
                        if (!SecureStorage.Disable(_pw3.Password)) { _error.Text = "Passwort ist falsch."; return; }
                        break;
                    case Mode.Recover:
                        if (!CheckNew()) return;
                        if (!SecureStorage.Recover(_recovery.Text, _pw1.Password)) { _error.Text = "Wiederherstellungsschlüssel ist ungültig."; return; }
                        break;
                }
                DialogResult = true;
            }
            catch (System.Exception ex)
            {
                _error.Text = "Fehler: " + ex.Message;
            }
            finally
            {
                Cursor = null;
            }
        }
    }
}
