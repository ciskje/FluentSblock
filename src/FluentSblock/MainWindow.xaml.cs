using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FluentSblock.Models;
using FluentSblock.Services;
using Microsoft.Win32;

namespace FluentSblock;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<RemovableDrive> _drives = new();
    private readonly ObservableCollection<LockHolder> _holders = new();
    private readonly ObservableCollection<LockHolder> _fileHolders = new();
    private CancellationTokenSource? _cts;
    private AppSettings.Data _settings = AppSettings.Load();

    public MainWindow()
    {
        InitializeComponent();
        if (!string.IsNullOrEmpty(_settings.Lang) && string.IsNullOrEmpty(App.StartupLang))
            L10n.SetLanguage(_settings.Lang);
        ThemeService.ApplyMica(this);
        DriveList.ItemsSource = _drives;
        DriveBox.ItemsSource = _drives;
        DriveBox.DisplayMemberPath = "Display";
        HoldersGrid.ItemsSource = _holders;
        System.Windows.Data.CollectionViewSource.GetDefaultView(_holders).Filter = o =>
            o is LockHolder h && (string.IsNullOrEmpty(_holderFilter) ||
            h.ProcessName.Contains(_holderFilter, StringComparison.OrdinalIgnoreCase) ||
            h.FilePath.Contains(_holderFilter, StringComparison.OrdinalIgnoreCase));
        FileHoldersGrid.ItemsSource = _fileHolders;
        Loaded += async (_, _) => { ApplyTexts(); RestoreColumns(); await RefreshDrivesAsync(); };
        Closing += (_, _) => SaveSettings();
        var refreshCommand = new RoutedCommand();
        CommandBindings.Add(new CommandBinding(refreshCommand, async (_, _) => await ScanSelectedDriveAsync()));
        InputBindings.Add(new KeyBinding(refreshCommand, Key.F5, ModifierKeys.None));
        var focusFileCommand = new RoutedCommand();
        CommandBindings.Add(new CommandBinding(focusFileCommand, (_, _) => FilePathBox.Focus()));
        InputBindings.Add(new KeyBinding(focusFileCommand, Key.L, ModifierKeys.Control));
    }

    private void ApplyTexts()
    {
        Title = L10n.Get("AppTitle");
        MiFile.Header = L10n.Get("MenuFile");
        MiView.Header = L10n.Get("MenuView");
        MiHelp.Header = L10n.Get("MenuHelp");
        MiRefresh.Header = L10n.Get("MenuRefresh");
        MiExportCsv.Header = L10n.Get("MenuExportCsv");
        MiExportTsv.Header = L10n.Get("MenuExportTsv");
        MiEject.Header = L10n.Get("MenuEject");
        MiExit.Header = L10n.Get("MenuExit");
        MiTheme.Header = L10n.Get("MenuTheme");
        MiLanguage.Header = L10n.Get("MenuLanguage");
        MiAbout.Header = L10n.Get("MenuAbout");
        MiAutostart.Header = L10n.Get("MenuAutostart");
        MiAutostart.IsChecked = IsAutostart();
        MiShowLog.Header = L10n.Get("MenuShowLog");
        MiThemeSystem.Header = L10n.Get("ThemeSystem");
        MiThemeLight.Header = L10n.Get("ThemeLight");
        MiThemeDark.Header = L10n.Get("ThemeDark");
        DriveGroup.Header = L10n.Get("DriveGroup");
        HoldersGroup.Header = L10n.Get("HoldersGroup");
        CheckFileGroup.Header = L10n.Get("CheckFileGroup");
        DropHint.Text = L10n.Get("ElevatedDropHint");
        DropHint.Visibility = Visibility.Collapsed;
        PaintStatus("SuccessBrush");
        RefreshBtnText.Text = L10n.Get("MenuRefresh");
        EjectBtnText.Text = L10n.Get("MenuEject");
        BrowseBtnText.Text = L10n.Get("Browse");
        CheckFileBtnText.Text = L10n.Get("CheckFileButton");
        FilePathBox.ToolTip = L10n.Get("CheckFileHint");
        ColProcess.Header = L10n.Get("ColProcess");
        ColPid.Header = L10n.Get("ColPid");
        ColUser.Header = L10n.Get("ColUser");
        ColHandle.Header = L10n.Get("ColHandle");
        ColAccess.Header = L10n.Get("ColAccess");
        ColFile.Header = L10n.Get("ColFile");
        FilterBox.ToolTip = L10n.Get("FilterHint");
        MiCopyPath.Header = L10n.Get("MenuCopyPath");
        MiOpenLocation.Header = L10n.Get("MenuOpenLocation");
        StatusText.Text = L10n.Get("StatusReady");
        AdminText.Text = ThemeService.IsElevated() ? L10n.Get("Elevated") : L10n.Get("NotElevated");
        try { VersionText.Text = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?"; }
        catch { VersionText.Text = "?"; }
        if (_drives.Count == 0 && DriveList.Items.Count == 0)
            ProgressText.Text = L10n.Get("NoDrives");
    }

    private void PaintStatus(string brushKey)
    {
        try { StatusDot.Fill = (System.Windows.Media.Brush)FindResource(brushKey); }
        catch { /* keep current dot */ }
    }

    /// <summary>Rotates button icons on a UI timer until <see cref="StopSpinners"/>.</summary>
    private DispatcherTimer? _spinTimer;

    private void StartSpinners(params System.Windows.Media.RotateTransform[] targets)
    {
        StopSpinners();
        _spinTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(60), DispatcherPriority.Render,
            (_, _) => { foreach (var t in targets) t.Angle = (t.Angle + 21.6) % 360; }, Dispatcher);
        _spinTimer.Start();
    }

    private void StopSpinners(params System.Windows.Media.RotateTransform[] targets)
    {
        _spinTimer?.Stop();
        _spinTimer = null;
        foreach (var t in targets) t.Angle = 0;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is System.Windows.Interop.HwndSource src)
            src.AddHook(WndProc);
    }

    private bool _autoRefreshing;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_DEVICECHANGE = 0x0219;
        if (msg == WM_DEVICECHANGE && !_autoRefreshing)
        {
            _autoRefreshing = true;
            _ = AutoRefreshAsync();
        }
        return IntPtr.Zero;
    }

    private async Task AutoRefreshAsync()
    {
        try { await RefreshDrivesAsync(); }
        finally { _autoRefreshing = false; }
    }

    private string _holderFilter = "";

    private void OnFilterChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _holderFilter = FilterBox.Text.Trim();
        System.Windows.Data.CollectionViewSource.GetDefaultView(_holders).Refresh();
    }

    private LockHolder? SelectedHolder() => HoldersGrid.SelectedItem as LockHolder;

    private void OnCopyPathClick(object sender, RoutedEventArgs e)
    {
        if (SelectedHolder() is { } h)
        {
            try { Clipboard.SetText(h.FilePath); }
            catch { }
        }
    }

    private void OnOpenLocationClick(object sender, RoutedEventArgs e)
    {
        if (SelectedHolder() is { } h)
        {
            try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{h.FilePath}\""); }
            catch (Exception ex) { ProgressText.Text = ex.Message; }
        }
    }

    private void RestoreColumns()
    {
        try
        {
            var cols = new Dictionary<string, DataGridColumn>
            {
                ["Process"] = ColProcess, ["PID"] = ColPid, ["User"] = ColUser,
                ["Handle"] = ColHandle, ["Access"] = ColAccess, ["File"] = ColFile
            };
            foreach (var kv in _settings.Columns)
            {
                if (!cols.TryGetValue(kv.Key, out var col)) continue;
                col.DisplayIndex = kv.Value.DisplayIndex;
                if (kv.Value.Width > 20) col.Width = new DataGridLength(kv.Value.Width);
            }
        }
        catch { /* keep default layout */ }
    }

    private void SaveSettings()
    {
        try
        {
            var cols = new Dictionary<string, AppSettings.ColumnState>();
            foreach (var (key, col) in new (string, DataGridColumn)[]
                     { ("Process", ColProcess), ("PID", ColPid), ("User", ColUser),
                       ("Handle", ColHandle), ("Access", ColAccess), ("File", ColFile) })
                cols[key] = new AppSettings.ColumnState(col.DisplayIndex, col.ActualWidth);
            AppSettings.Save(_settings with { Columns = cols });
        }
        catch { }
    }

    private async Task RefreshDrivesAsync()
    {
        _drives.Clear();
        foreach (var d in await Task.Run(DriveEnumerator.GetRemovableDrives))
            _drives.Add(d);
        if (_drives.Count == 0)
        {
            ProgressText.Text = L10n.Get("NoDrives");
            EjectBtn.IsEnabled = false;
            MiEject.IsEnabled = false;
            return;
        }
        EjectBtn.IsEnabled = true;
        MiEject.IsEnabled = true;
        // Set both selections without firing scans, then scan once.
        DriveList.SelectionChanged -= OnDriveSelectionChanged;
        DriveBox.SelectionChanged -= OnDriveSelectionChanged;
        try
        {
            DriveList.SelectedIndex = 0;
            DriveBox.SelectedIndex = 0;
        }
        finally
        {
            DriveList.SelectionChanged += OnDriveSelectionChanged;
            DriveBox.SelectionChanged += OnDriveSelectionChanged;
        }
        await ScanSelectedDriveAsync();
    }

    private string? SelectedLetter()
    {
        if (DriveList.SelectedItem is RemovableDrive d) return d.Letter;
        if (DriveBox.SelectedItem is RemovableDrive b) return b.Letter;
        return _drives.FirstOrDefault()?.Letter;
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await ScanSelectedDriveAsync();

    private async void OnDriveSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // Keep the two selectors in sync without reentrancy.
        if (sender == DriveList && DriveList.SelectedItem is RemovableDrive d)
            DriveBox.SelectedItem = d;
        else if (sender == DriveBox && DriveBox.SelectedItem is RemovableDrive b)
            DriveList.SelectedItem = b;
        await ScanSelectedDriveAsync();
    }

    private async Task ScanSelectedDriveAsync()
    {
        var letter = SelectedLetter();
        if (letter == null) return;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var progress = new Progress<string>(s => ProgressText.Text = s);
        StatusText.Text = L10n.Get("StatusScanning");
        PaintStatus("WarningBrush");
        StartSpinners(RefreshSpin);
        RefreshBtn.IsEnabled = false;
        _holders.Clear();
        Log.Info($"scan start drive={letter}");
        try
        {
            var holders = await HandleEnumerator.GetHoldersForDriveAsync(letter, progress, ct);
            foreach (var h in holders) _holders.Add(h);
            ProgressText.Text = holders.Count == 0 ? L10n.Get("NoLocks") : string.Format(L10n.Get("LocksFound"), holders.Count);
            Log.Info($"scan done drive={letter} holders={holders.Count}");
        }
        catch (OperationCanceledException) { Log.Info($"scan cancelled drive={letter}"); }
        catch (Exception ex) { ProgressText.Text = ex.Message; PaintStatus("ErrorBrush"); Log.Info($"scan fail drive={letter}: {ex.Message}"); }
        finally
        {
            StopSpinners(RefreshSpin);
            StatusText.Text = L10n.Get("StatusReady");
            RefreshBtn.IsEnabled = true;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFileDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            FilePathBox.Text = paths[0];
            OnCheckFileClick(sender, new RoutedEventArgs());
        }
    }

    private async void OnCheckFileClick(object sender, RoutedEventArgs e)
    {
        string path = FilePathBox.Text.Trim().Trim('"');
        if (string.IsNullOrEmpty(path)) return;
        _fileHolders.Clear();
        var progress = new Progress<string>(s => ProgressText.Text = s);
        StatusText.Text = L10n.Get("StatusScanning");
        PaintStatus("WarningBrush");
        CheckFileIcon.Visibility = Visibility.Collapsed;
        CheckFileRing.Visibility = Visibility.Visible;
        StartSpinners(CheckFileSpin);
        CheckFileBtn.IsEnabled = false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var holders = await FileLockChecker.WhoLocksAsync(path, progress, cts.Token);
            foreach (var h in holders) _fileHolders.Add(h);
            ProgressText.Text = holders.Count == 0 ? L10n.Get("NoLocks") : string.Format(L10n.Get("LocksFound"), holders.Count);
        }
        catch (Exception ex) { ProgressText.Text = ex.Message; PaintStatus("ErrorBrush"); }
        finally
        {
            StopSpinners(CheckFileSpin);
            CheckFileRing.Visibility = Visibility.Collapsed;
            CheckFileIcon.Visibility = Visibility.Visible;
            StatusText.Text = L10n.Get("StatusReady");
            CheckFileBtn.IsEnabled = true;
        }
    }

    private void OnFilePathGotFocus(object sender, RoutedEventArgs e)
    {
        // UIPI blocks the drop itself from reaching us when elevated, so the
        // field getting focus is the closest observable proxy of a drop attempt.
        DropHint.Visibility = ThemeService.IsElevated() ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnFilePathLostFocus(object sender, RoutedEventArgs e) =>
        DropHint.Visibility = Visibility.Collapsed;

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        // One dialog for files AND folders: with existence checks off,
        // Open on a highlighted folder returns the folder path.
        var dlg = new OpenFileDialog
        {
            Title = L10n.Get("BrowseTitle"),
            CheckFileExists = false,
            CheckPathExists = true,
            ValidateNames = false
        };
        try
        {
            if (SelectedLetter() is string l && Directory.Exists(l + "\\"))
                dlg.InitialDirectory = l + "\\";
        }
        catch { }
        if (dlg.ShowDialog(this) == true)
            FilePathBox.Text = dlg.FileName;
    }

    private async void OnEjectClick(object sender, RoutedEventArgs e)
    {
        var letter = SelectedLetter();
        if (letter == null) return;
        if (_holders.Count > 0)
        {
            var confirm = MessageBox.Show(this,
                string.Format(L10n.Get("EjectConfirmText"), _holders.Count),
                L10n.Get("EjectConfirmTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;
        }
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        StatusText.Text = L10n.Get("StatusScanning");
        PaintStatus("WarningBrush");
        try
        {
            var result = await EjectService.TryEjectAsync(letter, cts.Token);
            if (result.Ejected)
            {
                ProgressText.Text = L10n.Get("EjectOk");
                PaintStatus("SuccessBrush");
                Log.Info($"eject drive={letter} ok");
                await RefreshDrivesAsync();
                return;
            }
            string cause, remedy;
            if (result.VetoType != null)
            {
                cause = string.Format(L10n.Get("EjectVetoCause"), VetoDisplayName(result.VetoType), result.VetoName);
                remedy = L10n.Get("EjectVetoRemedy");
            }
            else
            {
                cause = string.Format(L10n.Get("EjectErrorCause"), result.ErrorCode);
                remedy = L10n.Get("EjectErrorRemedy");
            }
            ProgressText.Text = cause;
            PaintStatus("ErrorBrush");
            Log.Info($"eject drive={letter} blocked: {cause}");
            new EjectErrorDialog(L10n.Get("EjectDialogTitle"), cause, remedy) { Owner = this }.ShowDialog();
        }
        catch (Exception ex) { ProgressText.Text = ex.Message; PaintStatus("ErrorBrush"); }
        finally { StatusText.Text = L10n.Get("StatusReady"); }
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    private void OnThemeClick(object sender, RoutedEventArgs e)
    {
        MiThemeSystem.IsChecked = sender == MiThemeSystem;
        MiThemeLight.IsChecked = sender == MiThemeLight;
        MiThemeDark.IsChecked = sender == MiThemeDark;
        // v0.2: follows system; explicit light/dark via TODO 0.2 (resource dictionaries).
        _settings = _settings with
        {
            Theme = sender == MiThemeLight ? "light" : sender == MiThemeDark ? "dark" : "system"
        };
        SaveSettings();
    }

    private async void OnLanguageClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem mi && mi.Tag is string lang)
        {
            L10n.SetLanguage(lang);
            _settings = _settings with { Lang = lang };
            SaveSettings();
            ApplyTexts();
            await RefreshDrivesAsync();
        }
    }

    private static string VetoDisplayName(string veto)
    {
        // Localizes known PNP veto types; unknown ones keep the raw enum name.
        string key = "Veto" + veto;
        string s = L10n.Get(key);
        return s == key ? veto : s;
    }

    private void OnShowLogClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!System.IO.File.Exists(Log.FilePath))
                Log.Info("FluentSblock log started");
            System.Diagnostics.Process.Start("notepad.exe", Log.FilePath);
        }
        catch (Exception ex) { ProgressText.Text = ex.Message; }
    }

    private void OnExportCsvClick(object sender, RoutedEventArgs e) => ExportHolders(",", false);

    private void OnExportTsvClick(object sender, RoutedEventArgs e) => ExportHolders("\t", true);

    private void ExportHolders(string sep, bool tsv)
    {
        if (_holders.Count == 0) { ProgressText.Text = L10n.Get("NoLocks"); return; }
        var dlg = new OpenFileDialog();
        // Save dialog via OpenFileDialog with CheckFileExists=false (no extra dependency).
        dlg.CheckFileExists = false;
        dlg.CheckPathExists = true;
        dlg.ValidateNames = true;
        dlg.Filter = tsv ? "TSV (*.tsv)|*.tsv" : "CSV (*.csv)|*.csv";
        dlg.FileName = tsv ? "holders.tsv" : "holders.csv";
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(string.Join(sep, "Process", "PID", "User", "Handle", "Type", "Access", "File"));
            foreach (var h in _holders)
            {
                string Cell(string s) => tsv ? s.Replace("\t", " ") : (s.Contains('"') || s.Contains(',') ? '"' + s.Replace("\"", "\"\"") + '"' : s);
                sb.AppendLine(string.Join(sep, Cell(h.ProcessName), h.Pid.ToString(), Cell(h.User),
                    h.HandleHex, "File", h.Access, Cell(h.FilePath)));
            }
            System.IO.File.WriteAllText(dlg.FileName, sb.ToString(), System.Text.Encoding.UTF8);
            ProgressText.Text = dlg.FileName;
            Log.Info($"exported {_holders.Count} holders to {dlg.FileName}");
        }
        catch (Exception ex) { ProgressText.Text = ex.Message; }
    }

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private void OnAutostartClick(object sender, RoutedEventArgs e)
    {
        SetAutostart(!IsAutostart());
        MiAutostart.IsChecked = IsAutostart();
    }

    private static bool IsAutostart()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return k?.GetValue("FluentSblock") != null;
        }
        catch { return false; }
    }

    private void SetAutostart(bool on)
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (k == null) return;
            if (on)
                k.SetValue("FluentSblock", "\"" + Environment.ProcessPath + "\"");
            else
                k.DeleteValue("FluentSblock", false);
        }
        catch (Exception ex) { ProgressText.Text = ex.Message; }
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
        => new AboutWindow { Owner = this }.ShowDialog();
}
