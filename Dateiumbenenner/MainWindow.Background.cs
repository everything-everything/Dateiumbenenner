using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Dateiumbenenner.Engine;

namespace Dateiumbenenner
{
    /// <summary>Hintergrunddienste: Firmenverzeichnis (eigene DB, Gelerntes, GLEIF) und Gegenprüfung Datum/Betrag.</summary>
    public partial class MainWindow
    {
        private readonly List<Plugins.ICompanyDirectorySource> _companySources = new();
        private DispatcherTimer? _companySourceTimer;
        private string _bgCrossCheck = "", _bgCompany = "";
        private DispatcherTimer? _bgCompanyClear;

        private void SetBackgroundText(bool company, string text)
        {
            if (company) _bgCompany = text; else _bgCrossCheck = text;
            if (TxtBackground != null)
                TxtBackground.Text = string.Join("   |   ", new[] { _bgCompany, _bgCrossCheck }.Where(s => !string.IsNullOrEmpty(s)));
        }

        /// <summary>Firmen-Meldung nach einigen Sekunden wieder ausblenden.</summary>
        private void FlashCompanyText(string text)
        {
            SetBackgroundText(true, text);
            _bgCompanyClear?.Stop();
            _bgCompanyClear = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _bgCompanyClear.Tick += (_, _) => { _bgCompanyClear?.Stop(); if (CompanyDirectory.GleifJob is not { IsCompleted: false }) SetBackgroundText(true, ""); };
            _bgCompanyClear.Start();
        }

        private void InitBackgroundServices()
        {
            _ = CompanyDirectory.EnsureLoadedAsync();
            CompanyDirectory.GleifProgressChanged += OnGleifProgress;
            Closed += (_, _) =>
            {
                CompanyDirectory.GleifProgressChanged -= OnGleifProgress;
                _crossTimer?.Stop();
                CrossCheck.Save();
            };
            if (MenuCrossCheck != null) MenuCrossCheck.IsChecked = CrossCheckEnabled;
            if (CrossCheckEnabled) StartCrossCheck();
        }

        private void OnGleifProgress(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var s = CompanyDirectory.GleifStatus;
                if (CompanyDirectory.GleifProgress >= 100 || s.Contains("Fehler") || s.Contains("abgebrochen")) FlashCompanyText(s);
                else SetBackgroundText(true, s);
            }), DispatcherPriority.Background);
        }

        /// <summary>Adress-Provider, die auch alle Firmen liefern, speisen das Firmenverzeichnis (kurz nach dem Start im Hintergrund).</summary>
        private void RegisterCompanySource(Plugins.IAddressProvider provider)
        {
            if (provider is not Plugins.ICompanyDirectorySource src || _companySources.Contains(src)) return;
            _companySources.Add(src);
            if (_companySourceTimer != null) return;
            _companySourceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _companySourceTimer.Tick += (_, _) => { _companySourceTimer!.Stop(); _ = RefreshCompanyDirectoryAsync(false); };
            _companySourceTimer.Start();
        }

        private bool _companyRefreshRunning;

        /// <summary>Firmen aus der eigenen Datenbank (Plugin) neu einlesen. Liefert eine Meldung.</summary>
        public async Task<string> RefreshCompanyDirectoryAsync(bool manual)
        {
            if (_companySources.Count == 0) return "Keine Datenbank angebunden (INI-Plugin mit Adresstabelle nötig).";
            if (_companyRefreshRunning) return "Wird bereits eingelesen…";
            _companyRefreshRunning = true;
            try
            {
                await CompanyDirectory.EnsureLoadedAsync();
                SetBackgroundText(true, "Firmenverzeichnis: eigene Datenbank wird gelesen…");
                var all = new List<Plugins.CompanyRecord>();
                string? error = null;
                foreach (var src in _companySources.ToList())
                {
                    try { all.AddRange(await Task.Run(() => src.LoadCompaniesAsync())); }
                    catch (Exception ex) { error = ex.Message; }
                }
                // Datenbank nicht erreichbar oder abgeschaltet: letzten Stand behalten (außer bei manuellem Neueinlesen ohne Fehler)
                if (all.Count > 0 || (manual && error == null)) CompanyDirectory.SetDatabase(all);
                var msg = error != null && all.Count == 0
                    ? $"Firmenverzeichnis: Datenbank nicht lesbar ({error}) – letzter Stand bleibt ({CompanyDirectory.DbCount:N0} Firmen)."
                    : $"Firmenverzeichnis: {CompanyDirectory.DbCount:N0} Firmen aus eigener Datenbank.";
                FlashCompanyText(msg);
                return msg;
            }
            finally { _companyRefreshRunning = false; }
        }

        private void MenuCompanyDirectory_Click(object sender, RoutedEventArgs e)
        {
            new CompanyDirectoryWindow(this) { Owner = this }.ShowDialog();
        }

        // ---------- Gegenprüfung (Microsoft Recognizers-Text) ----------

        private static string CrossCheckFile => Path.Combine(Plugins.PluginManager.BaseDirectory, "crosscheck.flag");
        private static bool CrossCheckEnabled => File.Exists(CrossCheckFile);
        private DispatcherTimer? _crossTimer;
        private bool _crossRunning;
        private int _crossRun;
        private readonly Dictionary<string, string?> _crossAmounts = new(StringComparer.Ordinal);

        private void MenuCrossCheck_Click(object sender, RoutedEventArgs e)
        {
            bool on = MenuCrossCheck.IsChecked;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CrossCheckFile)!);
                if (on) File.WriteAllText(CrossCheckFile, "1"); else if (File.Exists(CrossCheckFile)) File.Delete(CrossCheckFile);
            }
            catch { }
            if (on) StartCrossCheck(); else StopCrossCheck();
        }

        private void StartCrossCheck()
        {
            _crossTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => { if (!_crossRunning) _ = CrossCheckPassAsync(); }, Dispatcher);
            _crossTimer.Start();
            if (!_crossRunning) _ = CrossCheckPassAsync();
        }

        private void StopCrossCheck()
        {
            _crossTimer?.Stop();
            _crossRun++;
            foreach (var m in _items) { m.CrossCheckFlag = null; m.CrossCheckInfo = null; m.CrossCheckText = null; m.CrossCheckDate = null; }
            CrossCheck.Save();
            SetBackgroundText(false, "");
        }

        /// <summary>
        /// Ein Durchlauf wie ein Suchindex: nur neue oder geänderte Belege werden ausgewertet (Ergebnis je Textinhalt zwischengespeichert),
        /// Datum-Änderungen werden ohne neue Auswertung nur neu verglichen. Rechnet mit niedriger Priorität im Hintergrund.
        /// </summary>
        private async Task CrossCheckPassAsync()
        {
            _crossRunning = true;
            int run = _crossRun;
            try
            {
                var pending = _items.Where(m => m.Loaded && !string.IsNullOrEmpty(m.FullText)
                    && (!ReferenceEquals(m.CrossCheckText, m.FullText) || m.CrossCheckDate != m.Date)).ToList();
                if (pending.Count == 0) return;
                int done = 0, analyzed = 0;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                foreach (var m in pending)
                {
                    if (run != _crossRun) return;
                    var text = m.FullText!;
                    var key = CrossCheck.KeyOf(text);
                    if (!CrossCheck.TryGet(key, out var analysis))
                    {
                        if (analyzed == 0) SetBackgroundText(false, $"Gegenprüfung: 0 / {pending.Count:N0} …");
                        var a = await Task.Run(() => LowPriority(() => { try { return CrossCheck.Analyze(key, text); } catch { return null; } }));
                        analyzed++;
                        if (a == null)
                        {
                            m.CrossCheckFlag = "–"; m.CrossCheckInfo = "Gegenprüfung für diesen Beleg fehlgeschlagen.";
                            m.CrossCheckText = text; m.CrossCheckDate = m.Date; done++;
                            continue;
                        }
                        analysis = a;
                    }
                    if (!_crossAmounts.TryGetValue(key, out var amount))
                    {
                        amount = await Task.Run(() => LowPriority(() => { try { return Engine.ExtractAmount(text).Value; } catch { return null; } }));
                        _crossAmounts[key] = amount;
                    }
                    done++;
                    if (run != _crossRun) return;
                    if (!ReferenceEquals(m.FullText, text)) continue;   // Text hat sich inzwischen geändert – nächster Durchlauf
                    var v = CrossCheck.Compare(analysis, m.Date, amount);
                    m.CrossCheckFlag = v.Flag;
                    m.CrossCheckInfo = v.Info;
                    m.CrossCheckText = text;
                    m.CrossCheckDate = m.Date;
                    // Abweichendes Datum nur als zusätzlichen Vorschlag anbieten, nie übernehmen
                    if (v.SuggestedDate != null && !m.DateCandidates.Contains(v.SuggestedDate))
                    {
                        m.DateCandidates.Add(v.SuggestedDate);
                        if (m == _currentMeta && CmbDateCandidates != null && !CmbDateCandidates.IsKeyboardFocusWithin) CmbDateCandidates.Items.Refresh();
                    }
                    if (analyzed > 0 && sw.ElapsedMilliseconds > 300)
                    {
                        SetBackgroundText(false, $"Gegenprüfung: {done:N0} / {pending.Count:N0} …");
                        sw.Restart();
                    }
                }
                if (analyzed > 0)
                {
                    CrossCheck.Save();
                    int checkedCount = _items.Count(x => !string.IsNullOrEmpty(x.CrossCheckFlag));
                    int dev = _items.Count(x => x.CrossCheckFlag == "⚠");
                    SetBackgroundText(false, $"Gegenprüfung fertig: {checkedCount:N0} Belege, {dev:N0} Abweichung(en) ⚠");
                }
            }
            catch (Exception ex) { SetBackgroundText(false, "Gegenprüfung: Fehler – " + ex.Message); }
            finally { _crossRunning = false; }
        }

        private static T LowPriority<T>(Func<T> f)
        {
            var t = Thread.CurrentThread;
            var old = t.Priority;
            try { t.Priority = ThreadPriority.BelowNormal; return f(); }
            finally { t.Priority = old; }
        }
    }
}
