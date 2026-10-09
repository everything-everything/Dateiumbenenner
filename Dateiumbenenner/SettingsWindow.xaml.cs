using System;
using System.Windows;
using Dateiumbenenner.Engine;

namespace Dateiumbenenner
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow(bool askTemplates)
        {
            InitializeComponent();
            ChkDebug.IsChecked = DocumentEngine.DebugEnabled;
            ChkAskTemplates.IsChecked = askTemplates;
            TxtExclusions.Text = string.Join(Environment.NewLine, CompanyExclusions.Items);
            var own = OwnCompany.Current;
            TxtThreshold.Text = Math.Round(CompanyMatcher.Threshold * 100).ToString();
            TxtListThreshold.Text = Math.Round(CompanyMatcher.ListThreshold * 100).ToString();
            TxtOwnName.Text = own.Name; TxtOwnStreet.Text = own.Street; TxtOwnZip.Text = own.Zip; TxtOwnCity.Text = own.City;
        }

        public bool AskTemplates => ChkAskTemplates.IsChecked == true;

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            DocumentEngine.DebugEnabled = ChkDebug.IsChecked == true;
            if (!int.TryParse(TxtThreshold.Text.Trim().TrimEnd('%'), out var pct) || pct < 50 || pct > 100)
            {
                System.Windows.MessageBox.Show("Übereinstimmung bitte als Zahl zwischen 50 und 100 angeben.", "Einstellungen");
                TxtThreshold.Focus();
                return;
            }
            if (!int.TryParse(TxtListThreshold.Text.Trim().TrimEnd('%'), out var listPct) || listPct < 0 || listPct > 100)
            {
                System.Windows.MessageBox.Show("Übereinstimmung der Auswahllisten bitte als Zahl zwischen 0 und 100 angeben.", "Einstellungen");
                TxtListThreshold.Focus();
                return;
            }
            CompanyMatcher.Threshold = pct / 100.0;
            CompanyMatcher.ListThreshold = listPct / 100.0;
            try
            {
                CompanyExclusions.Save(TxtExclusions.Text.Split('\n'));
                OwnCompany.Save(new OwnCompany { Name = TxtOwnName.Text.Trim(), Street = TxtOwnStreet.Text.Trim(), Zip = TxtOwnZip.Text.Trim(), City = TxtOwnCity.Text.Trim() });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Ausschlüsse konnten nicht gespeichert werden:\n" + ex.Message, "Einstellungen");
                return;
            }
            DialogResult = true;
        }
    }
}
