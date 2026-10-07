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
        }

        public bool AskTemplates => ChkAskTemplates.IsChecked == true;

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            DocumentEngine.DebugEnabled = ChkDebug.IsChecked == true;
            try { CompanyExclusions.Save(TxtExclusions.Text.Split('\n')); }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Ausschlüsse konnten nicht gespeichert werden:\n" + ex.Message, "Einstellungen");
                return;
            }
            DialogResult = true;
        }
    }
}
