using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using PrivacyAudit.Core;
using PrivacyAudit.PeopleDetection;
using PrivacyAudit.Scanners;
using PrivacyAudit.Storage;

namespace PrivacyAudit;

public partial class MainWindow : Window
{
    readonly List<Finding> _findings = [];
    readonly ObservableRangeCollection<Finding> _visibleFindings = [];
    readonly List<Finding> _mediaFindings = [];
    readonly ObservableRangeCollection<Finding> _visibleMediaFindings = [];
    readonly AuditDatabase _db;
    readonly ModelManager _modelManager;
    readonly PeopleScanRepository _peopleRepository;
    readonly PersonalAttentionModelService _personalModel;
    readonly AppDataCleanupService _cleanupService;
    readonly string _snapshotPath;
    readonly string _introShownPath;
    CancellationTokenSource? _cts;
    CancellationTokenSource? _provenanceCts;
    FileProvenanceResult? _provenanceResult;
    Finding? _selected;
    enum DetailsReturnSource { FindingsGrid, FindingsTiles, MediaTiles, ApplicationHistory }
    sealed record DetailsReturnState(DetailsReturnSource Source, Finding Finding, double VerticalOffset, double HorizontalOffset);
    DetailsReturnState? _detailsReturnState;
    HwndSource? _windowSource;
    const int WmXButtonDown = 0x020B;
    const int WmXButtonUp = 0x020C;
    const int WmAppCommand = 0x0319;
    const int WmKeyDown = 0x0100;
    const int WmSysKeyDown = 0x0104;
    const int XButton1 = 1;
    const int XButton2 = 2;
    const int BrowserBackwardCommand = 1;
    const int BrowserBackVirtualKey = 0xA6;
    DateTime _scanStart;
    int _findingsPage;
    Finding[] _sortedFindings = [];
    readonly Queue<int> _loadedFindingBatches = new();
    bool _loadingFindingBatch;
    CancellationTokenSource? _pageLoadCts;
    int _firstLoadedPage;
    int _lastLoadedPage;
    bool _userScrollPending;
    bool _restoreStarted;
    bool _imageTileMode;
    bool _updatingFindingsUi;
    CancellationTokenSource? _mediaFilterCts;
    Finding[] _sortedMediaFindings = [];
    readonly Queue<int> _loadedMediaBatches = new();
    int _firstLoadedMediaPage;
    int _lastLoadedMediaPage;
    bool _loadingMediaBatch;
    int _completedPeopleResults;
    string _sortProperty = nameof(Finding.ExposureScore);
    bool _sortDescending = true;
    CancellationTokenSource? _textScanCts;
    CancellationTokenSource? _documentScanCts;
    CancellationTokenSource? _similarCts;
    CancellationTokenSource? _personalTrainingCts;
    CancellationTokenSource? _exifScanCts;
    CancellationTokenSource? _applicationHistoryCts;
    readonly ObservableCollection<ApplicationHistoryApplication> _applicationHistoryApplications = [];
    readonly ObservableRangeCollection<ApplicationHistoryApplication> _visibleApplicationHistoryApplications = [];
    readonly HashSet<string> _expandedApplicationHistoryKeys = new(StringComparer.OrdinalIgnoreCase);
    double _applicationHistoryScrollOffset;
    bool _auditAvailableForApplicationHistory;
    readonly System.Windows.Controls.ComboBox _driveBox = new() { Width = 330, Margin = new Thickness(0, 0, 0, 0), Visibility = Visibility.Collapsed };
    System.Windows.Controls.Button? _chooseButton;
    readonly object _heavyTaskLock = new();
    string? _activeHeavyTaskName;

    sealed class ActionDisposable(Action action) : IDisposable
    {
        int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                action();
            }
        }
    }

    static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is null) yield break;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in FindVisualChildren<T>(child)) yield return nested;
        }
    }

    IDisposable? TryAcquireHeavyTask(string taskName)
    {
        lock (_heavyTaskLock)
        {
            if (_activeHeavyTaskName is not null)
            {
                System.Windows.MessageBox.Show(
                    LocalizationService.Get("OperationAlreadyRunning"),
                    LocalizationService.Get("AppTitle"),
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return null;
            }
            _activeHeavyTaskName = taskName;
        }

        SetGlobalScanControlsEnabled(false);
        SidebarFooterControl.SetGlobalBusy(true, taskName);

        return new ActionDisposable(() =>
        {
            lock (_heavyTaskLock)
            {
                _activeHeavyTaskName = null;
            }
            Dispatcher.InvokeAsync(() =>
            {
                ClearCancellationProgress();
                SetGlobalScanControlsEnabled(true);
                SidebarFooterControl.SetGlobalBusy(false);
            });
        });
    }

    void SetGlobalScanControlsEnabled(bool enabled)
    {
        if (ScanButton is not null) ScanButton.IsEnabled = enabled;
        if (PiiScanButton is not null) PiiScanButton.IsEnabled = enabled;
        if (SecretsScanButton is not null) SecretsScanButton.IsEnabled = enabled;
        if (ConfigsScanButton is not null) ConfigsScanButton.IsEnabled = enabled;
        if (IdentityScanButton is not null) IdentityScanButton.IsEnabled = enabled;
        if (ArchivesScanButton is not null) ArchivesScanButton.IsEnabled = enabled;
        if (DocumentScanButton is not null) DocumentScanButton.IsEnabled = enabled;
        if (ExifScanButton is not null) ExifScanButton.IsEnabled = enabled;
        if (FindSimilarButton is not null) FindSimilarButton.IsEnabled = enabled;
        if (PeopleScanButton is not null) PeopleScanButton.IsEnabled = enabled;
        if (DownloadPeopleModelButton is not null) DownloadPeopleModelButton.IsEnabled = enabled;
        if (RemovePeopleModelButton is not null) RemovePeopleModelButton.IsEnabled = enabled;
        if (ApplicationHistoryAnalyzeButton is not null) ApplicationHistoryAnalyzeButton.IsEnabled = enabled && _auditAvailableForApplicationHistory;
    }

    void RequestCancellation(CancellationTokenSource? source)
    {
        if (source is null || source.IsCancellationRequested) return;
        CancellationProgress.Visibility = Visibility.Visible;
        CancellationProgress.IsIndeterminate = true;
        CancellationText.Visibility = Visibility.Visible;
        CancellationText.Text = LocalizationService.Get("CancellationInProgress");
        _ = source.CancelAsync();
    }

    void ClearCancellationProgress()
    {
        if (CancellationProgress is null) return;
        CancellationProgress.Visibility = Visibility.Collapsed;
        CancellationText.Visibility = Visibility.Collapsed;
    }

    public MainWindow(bool suppressRestorePrompt = false)
    {
        InitializeComponent();
        var findingsRowStyle = new Style(typeof(DataGridRow));
        findingsRowStyle.Setters.Add(new Setter(System.Windows.Controls.Control.FocusVisualStyleProperty, null));
        findingsRowStyle.Setters.Add(new Setter(System.Windows.Controls.Control.BorderThicknessProperty, new Thickness(0)));
        FindingsGrid.RowStyle = findingsRowStyle;
        CountersText.Margin = new Thickness(16, 0, 0, 0);
        AdminDot.VerticalAlignment = VerticalAlignment.Center;
        AdminText.VerticalAlignment = VerticalAlignment.Center;
        ElevateButton.VerticalAlignment = VerticalAlignment.Center;
        Loaded += (_, _) =>
        {
            foreach (var element in FindVisualChildren<FrameworkElement>(this))
            {
                if (element is TextBlock text && text.Text.Contains("SCAN") && text.Text.Contains("REPORT"))
                    text.Visibility = Visibility.Collapsed;
                if (element is System.Windows.Controls.Button button && button.Content?.ToString() == LocalizationService.Get("ChooseFolder"))
                    button.MinWidth = 112;
            }
        };
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NotBadPrivacyDetectorAgent");
        _db = new(Path.Combine(data, "privacy-audit.db"));
        try { _db.PruneAuditHistory(DateTime.UtcNow - StorageLimits.AuditRetention); } catch (Exception ex) { CrashLogger.LogException(ex, "Retention cleanup"); }
        _modelManager = new(data);
        _peopleRepository = new(Path.Combine(data, "privacy-audit.db"));
        _personalModel = new(data);
        _cleanupService = new(data);
        _snapshotPath = SnapshotStore.PathFor(data);
        _introShownPath = Path.Combine(data, "intro-seen.flag");
        RootsBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        RootsBox.Width = 255;
        _chooseButton = (RootsBox.Parent as System.Windows.Controls.Panel)?.Children.OfType<System.Windows.Controls.Button>().FirstOrDefault();
        _chooseButton?.SetCurrentValue(FrameworkElement.WidthProperty, 90d);
        _driveBox.SelectionChanged += DriveBox_SelectionChanged;
        if (RootsBox.Parent is System.Windows.Controls.Panel targetPanel) targetPanel.Children.Insert(0, _driveBox);
        PresetBox.SelectionChanged += PresetBox_SelectionChanged;
        ConfigureScanTarget();
        ConfigureStatusLayout();
        var elevated = Elevation.IsAdministrator();
        AdminText.Text = elevated ? LocalizationService.Get("AdminMode") : LocalizationService.Get("NormalMode");
        AdminDot.Fill = elevated ? (System.Windows.Media.Brush)FindResource("Green") : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 159, 10));
        ElevateButton.Visibility = elevated ? Visibility.Collapsed : Visibility.Visible;
        FindingsGrid.ItemsSource = _visibleFindings;
        FindingsTileList.ItemsSource = _visibleFindings;
        MediaTileList.ItemsSource = _visibleMediaFindings;
        ApplicationHistoryList.ItemsSource = _visibleApplicationHistoryApplications;
        MediaTileList.PreviewMouseWheel += MediaTileList_MouseWheel;
        MediaTileList.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(MediaTileList_ScrollChanged));
        StatusText.Text = LocalizationService.Get("Ready");
        UpdateFindingsPresentation();
        UpdateModelControls();
        RefreshApplicationHistorySummary();
        RefreshFindingsPage(true);
        ApplyPersonalState();
        Loaded += async (_, _) =>
        {
            if (!suppressRestorePrompt && !_restoreStarted)
            {
                _restoreStarted = true;
                await TryRestoreSnapshotAsync();
                ShowIntroIfNeeded();
            }
            var stats = _db.GetPersonalModelStats(_personalModel.Metadata?.TrainedSamples ?? 0);
            if (!_personalModel.IsReady && stats.CanTrain) await TrainPersonalModelAsync();
        };
        SourceInitialized += MainWindow_SourceInitialized;
        Closed += MainWindow_Closed;
        Closing += (_, _) => CancelAllBackgroundWork();
    }

    void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        _windowSource = PresentationSource.FromVisual(this) as HwndSource;
        _windowSource?.AddHook(WindowMessageHook);
    }

    void MainWindow_Closed(object? sender, EventArgs e)
    {
        _windowSource?.RemoveHook(WindowMessageHook);
        _windowSource = null;
    }

    IntPtr WindowMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!TabDetails.IsSelected || _detailsReturnState is null) return IntPtr.Zero;

        var xButton = (wParam.ToInt64() >> 16) & 0xFFFF;
        var isMouseBack = (msg == WmXButtonDown || msg == WmXButtonUp) && (xButton == XButton1 || xButton == XButton2);
        var isBrowserBack = msg == WmAppCommand && ((lParam.ToInt64() >> 16) & 0x0FFF) == BrowserBackwardCommand;
        var isBrowserBackKey = (msg == WmKeyDown || msg == WmSysKeyDown) && wParam.ToInt64() == BrowserBackVirtualKey;
        if (isMouseBack || isBrowserBack || isBrowserBackKey)
        {
            handled = true;
            ReturnFromDetails();
        }
        return IntPtr.Zero;
    }

    async Task TryRestoreSnapshotAsync()
    {
        if (!File.Exists(_snapshotPath)) return;
        var info = new FileInfo(_snapshotPath);
        var prompt = string.Format(LocalizationService.Get("RestoreSnapshotAvailablePrompt"), info.LastWriteTime.ToString("g"), Format.Bytes(info.Length));
        if (System.Windows.MessageBox.Show(prompt, LocalizationService.Get("AppTitle"), System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question, System.Windows.MessageBoxResult.No) != System.Windows.MessageBoxResult.Yes) return;

        using var busy = TryAcquireHeavyTask(LocalizationService.Get("SnapshotRestoring"));
        if (busy is null) return;
        MainTabs.IsEnabled = false;
        try
        {
            var progress = new Progress<string>(stage =>
            {
                StatusText.Text = stage;
                SidebarFooterControl.SetGlobalBusy(true, stage);
            });
            var snapshot = await SnapshotStore.LoadAsync(_snapshotPath, progress);
            if (snapshot is null || snapshot.Findings.Count == 0) return;
            StatusText.Text = string.Format(LocalizationService.Get("SnapshotIndexing"), snapshot.Findings.Count);
            SidebarFooterControl.SetGlobalBusy(true, StatusText.Text);
            _findings.Clear();
            _findings.AddRange(snapshot.Findings);
            _auditAvailableForApplicationHistory = true;
            RefreshApplicationHistorySummary();
            _mediaFindings.AddRange(snapshot.Findings.Where(x => x.Category == "Images" && File.Exists(x.Path)).OrderByDescending(x => x.ModifiedAt ?? DateTime.MinValue));
            UpdatePeoplePresentation();
            BuildDashboard();
            RebuildCategories();
            RefreshFindingsPage(true);
            StatusText.Text = string.Format(LocalizationService.Get("SnapshotLoaded"), snapshot.Findings.Count);
        }
        catch (Exception ex) { StatusText.Text = LocalizationService.Get("SnapshotUnavailable"); CrashLogger.LogException(ex, "Restore snapshot"); }
        finally { MainTabs.IsEnabled = true; }
    }

    void ShowIntroIfNeeded()
    {
        if (File.Exists(_introShownPath)) return;
        var dialog = new IntroDialog();
        dialog.Completed += (_, _) => { var remember = dialog.DoNotShow; HideModal(); if (remember) { try { Directory.CreateDirectory(Path.GetDirectoryName(_introShownPath)!); File.WriteAllText(_introShownPath, DateTime.UtcNow.ToString("O")); } catch { } } };
        ShowModal(dialog, LocalizationService.Get("IntroTitle"));
    }

    void ShowModal(FrameworkElement content, string title)
    {
        ModalTitleText.Text = title;
        ModalContentHost.Content = content;
        ModalOverlay.Visibility = Visibility.Visible;
        Keyboard.Focus(content);
    }
    void HideModal() { ModalContentHost.Content = null; ModalOverlay.Visibility = Visibility.Collapsed; }
    void CloseModal_Click(object sender, RoutedEventArgs e) => HideModal();

    public void ShowDocumentModal(string resourceKey, string title, string icon)
    {
        try
        {
            var viewer = new DocumentViewerWindow();
            viewer.LoadDocument(resourceKey, title, icon);
            viewer.Closed += (_, _) => HideModal();
            ShowModal(viewer, title);
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    public void ShowCleanupModal()
    {
        if (!CanRunCleanup) { StatusText.Text = LocalizationService.Get("CleanupBusy"); return; }
        var dialog = new CleanupDialog();
        dialog.Completed += (_, _) =>
        {
            var choice = dialog.Choice; HideModal(); if (choice is null) return;
            try { if (choice == CleanupChoice.SecondaryOnly) { ClearCachesAndAuditResults(); StatusText.Text = LocalizationService.Get("CleanupSecondaryDone"); } else { DeleteAllApplicationData(); StatusText.Text = LocalizationService.Get("CleanupAllDone"); System.Windows.Application.Current.Shutdown(); } }
            catch (Exception ex) { StatusText.Text = string.Format(LocalizationService.Get("CleanupFailed"), ex.Message); }
        };
        ShowModal(dialog, LocalizationService.Get("CleanupDialogTitle"));
    }

    void ReportIncorrectDetection_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var dialog = new IncorrectDetectionDialog(_selected);
        dialog.Completed += (_, _) => HideModal();
        ShowModal(dialog, LocalizationService.Get("IncorrectDetectionQuestion"));
    }

    ScanPreset GetSelectedPreset()
    {
        if (PresetBox?.SelectedItem is ComboBoxItem item && Enum.TryParse<ScanPreset>(item.Tag?.ToString(), out var preset))
            return preset;
        return ScanPreset.Custom;
    }

    async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (_cts is not null) return;
        var guard = TryAcquireHeavyTask(LocalizationService.Get("StartScan"));
        if (guard is null) return;

        _cts = new(); _findings.Clear(); _visibleFindings.Clear(); _mediaFindings.Clear(); _visibleMediaFindings.Clear(); _completedPeopleResults = 0; DashboardPanel.Children.Clear(); EmptyDashboard.Visibility = Visibility.Visible; _scanStart = DateTime.UtcNow;
        ScanButton.IsEnabled = false; CancelButton.IsEnabled = true; Busy.Visibility = Visibility.Visible;
        try
        {
            var preset = GetSelectedPreset();
            var roots = RootsBox.Text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (roots.Count == 0) throw new InvalidOperationException(LocalizationService.Get("FolderRequired"));
            if (preset == ScanPreset.Full) roots = roots.Select(root => Path.GetPathRoot(root) ?? root).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var progress = new Progress<ScanProgress>(p => { StatusText.Text = $"{LocalizationService.Get("FileCount")}: {p.Files:N0}   {LocalizationService.Get("DataCount")}: {Format.Bytes(p.Bytes)}   {LocalizationService.Get("FindingCount")}: {p.Findings:N0}"; CountersText.Text = $"{p.Scanner}: {p.CurrentPath}"; });
            var context = new ScanContext { Preset = preset, Roots = roots, Exclusions = _db.GetExclusions(), Progress = progress };
            var scanners = ScanPresetPolicy.IncludesSystemScanners(preset)
                ? new List<IPrivacyScanner> { new RecentScanner(), new JumpListScanner(), new ProfileScanner(), new FilesystemScanner() }
                : new List<IPrivacyScanner> { new FilesystemScanner() };
            var result = await new ScanCoordinator(scanners).RunAsync(context, _cts.Token);
            var wasCanceled = _cts.IsCancellationRequested;
            _findings.AddRange(result.Findings.OrderByDescending(x => x.ExposureScore).ThenByDescending(x => x.SizeBytes));
            ApplyPersonalState();
            _mediaFindings.AddRange(result.Findings.Where(x => x.Category == "Images" && File.Exists(x.Path)).OrderByDescending(x => x.ModifiedAt ?? DateTime.MinValue));
            UpdatePeoplePresentation();
            _db.Save(Guid.NewGuid(), _scanStart, _findings);
            SnapshotStore.Save(_snapshotPath, DateTime.UtcNow, _findings);
            BuildDashboard(); RebuildCategories(); RefreshFindingsPage(true);
            _auditAvailableForApplicationHistory = true;
            RefreshApplicationHistorySummary();
            var elapsed = (DateTime.UtcNow - _scanStart).ToString("hh\\:mm\\:ss");
            StatusText.Text = string.Format(LocalizationService.Get(wasCanceled ? "ScanStopped" : "ScanComplete"), wasCanceled ? new object[] { elapsed, _findings.Count } : new object[] { elapsed });
        }
        catch (OperationCanceledException) { StatusText.Text = LocalizationService.Get("ScanCanceled"); }
        catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "NotBad Privacy Detector Agent", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error); StatusText.Text = LocalizationService.Get("ScanFailed"); }
        finally { guard.Dispose(); _cts.Dispose(); _cts = null; ScanButton.IsEnabled = true; CancelButton.IsEnabled = false; Busy.Visibility = Visibility.Collapsed; }
    }

    void RefreshApplicationHistorySummary()
    {
        if (ApplicationHistorySummaryText is null) return;
        if (!_auditAvailableForApplicationHistory)
        {
            ApplicationHistorySummaryText.Text = LocalizationService.Get("ApplicationHistoryAuditRequired");
            ApplicationHistoryStatusText.Text = LocalizationService.Get("ApplicationHistoryAuditRequiredHelp");
            ApplicationHistoryAnalyzeButton.IsEnabled = false;
            ApplicationHistoryFiltersPanel.IsEnabled = false;
            return;
        }
        var summary = ApplicationHistoryDiscovery.Summarize(ApplicationHistoryDiscovery.EnumerateContainers());
        ApplicationHistorySummaryText.Text = summary.Containers == 0
            ? LocalizationService.Get("ApplicationHistoryNone")
            : string.Format(LocalizationService.Get("ApplicationHistoryContainersSummary"), summary.Containers, Format.Bytes(summary.TotalBytes), summary.LastModified?.ToString("g") ?? "—");
        ApplicationHistoryStatusText.Text = LocalizationService.Get("ApplicationHistoryLocalOnly");
        ApplicationHistoryAnalyzeButton.IsEnabled = summary.Containers > 0;
    }

    void ApplicationHistoryLegendToggle_Click(object sender, RoutedEventArgs e) =>
        ApplicationHistoryLegendPanel.Visibility = ApplicationHistoryLegendPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    async void ApplicationHistoryAnalyze_Click(object sender, RoutedEventArgs e)
    {
        if (_applicationHistoryCts is not null || !_auditAvailableForApplicationHistory) return;
        var guard = TryAcquireHeavyTask(LocalizationService.Get("AnalyzeApplicationHistory"));
        if (guard is null) return;
        _applicationHistoryCts = new();
        ApplicationHistoryAnalyzeButton.IsEnabled = false;
        ApplicationHistoryCancelButton.IsEnabled = true;
        ApplicationHistoryProgress.Visibility = Visibility.Visible;
        ApplicationHistoryProgress.IsIndeterminate = false;
        ApplicationHistoryProgress.Value = 0;
        try
        {
            var containers = ApplicationHistoryDiscovery.EnumerateContainers();
            if (containers.Count == 0)
            {
                ApplicationHistoryStatusText.Text = LocalizationService.Get("ApplicationHistoryNone");
                return;
            }
            var progress = new Progress<(int Done, int Total, string Current)>(p =>
            {
                ApplicationHistoryProgress.Maximum = Math.Max(1, p.Total);
                ApplicationHistoryProgress.Value = p.Done;
                ApplicationHistoryStatusText.Text = string.Format(LocalizationService.Get("ApplicationHistoryAnalyzingProgress"), p.Done, p.Total);
            });
            var analysis = await new ApplicationHistoryAnalyzer().AnalyzeAsync(containers, _findings, progress, _applicationHistoryCts.Token);
            _applicationHistoryApplications.Clear();
            foreach (var application in analysis.Applications)
            {
                var localized = application.Identity.Confidence == ApplicationIdentityConfidence.Unknown
                    ? application with { Identity = application.Identity with { DisplayName = $"{LocalizationService.Get("UnknownApplication")} · {application.Identity.AppId}" } }
                    : application;
                _applicationHistoryApplications.Add(localized);
            }
            ApplicationHistoryFiltersPanel.IsEnabled = true;
            ApplicationHistoryRiskFilter.SelectedIndex = 1;
            RefreshApplicationHistoryFilter();
            ApplyApplicationHistoryContext(analysis);
            var knownHistoricalPaths = _findings.Where(x => x.ScannerId == "application-history").Select(x => x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _findings.AddRange(analysis.SignificantFindings.Where(x => knownHistoricalPaths.Add(x.Path)));
            ApplyPersonalState();
            await ApplyApplicationHistoryPersonalStateAsync(_applicationHistoryCts.Token);
            RebuildCategories(); RefreshFindingsPage(true);
            SnapshotStore.Save(_snapshotPath, DateTime.UtcNow, _findings);
            ApplicationHistorySummaryText.Text = string.Format(LocalizationService.Get("ApplicationHistoryAnalysisSummary"), analysis.Applications.Count, analysis.RememberedObjects, analysis.MissingTargets, analysis.SensitiveObjects);
            ApplicationHistoryStatusText.Text = analysis.Warnings == 0
                ? LocalizationService.Get("ApplicationHistoryComplete")
                : string.Format(LocalizationService.Get("ApplicationHistoryCompleteWarnings"), analysis.Warnings);
        }
        catch (OperationCanceledException)
        {
            ApplicationHistoryStatusText.Text = LocalizationService.Get("ApplicationHistoryCanceled");
        }
        catch (Exception ex)
        {
            CrashLogger.LogException(ex, "Application history analysis");
            ApplicationHistoryStatusText.Text = LocalizationService.Get("ApplicationHistoryFailed");
        }
        finally
        {
            guard.Dispose();
            _applicationHistoryCts.Dispose(); _applicationHistoryCts = null;
            ApplicationHistoryAnalyzeButton.IsEnabled = true;
            ApplicationHistoryCancelButton.IsEnabled = false;
            ApplicationHistoryProgress.Visibility = Visibility.Collapsed;
        }
    }

    void ApplicationHistoryCancel_Click(object sender, RoutedEventArgs e) => RequestCancellation(_applicationHistoryCts);

    void ApplicationHistoryFilter_Changed(object sender, SelectionChangedEventArgs e) => RefreshApplicationHistoryFilter();
    void ApplicationHistoryFilter_SliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => RefreshApplicationHistoryFilter();
    void ApplicationHistorySearch_TextChanged(object sender, TextChangedEventArgs e) => RefreshApplicationHistoryFilter();
    void ApplicationHistoryResetFilters_Click(object sender, RoutedEventArgs e)
    {
        ApplicationHistoryAvailabilityFilter.SelectedIndex = 0;
        ApplicationHistoryPinnedFilter.SelectedIndex = 0;
        ApplicationHistoryRiskFilter.SelectedIndex = 0;
        ApplicationHistorySortFilter.SelectedIndex = 0;
        ApplicationHistorySizeSlider.Value = 0;
        ApplicationHistoryAgeSlider.Value = 0;
        ApplicationHistorySearchBox.Clear();
        RefreshApplicationHistoryFilter();
    }

    void RefreshApplicationHistoryFilter()
    {
        if (ApplicationHistoryAvailabilityFilter is null || ApplicationHistoryPinnedFilter is null || ApplicationHistoryRiskFilter is null || ApplicationHistorySortFilter is null ||
            ApplicationHistorySizeSlider is null || ApplicationHistoryAgeSlider is null || ApplicationHistorySearchBox is null ||
            ApplicationHistorySizeLabel is null || ApplicationHistoryAgeLabel is null) return;
        var availability = (ApplicationHistoryAvailabilityFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "All";
        var pinned = (ApplicationHistoryPinnedFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "All";
        var risk = (ApplicationHistoryRiskFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "All";
        var sort = (ApplicationHistorySortFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Default";
        var sizeStep = (int)ApplicationHistorySizeSlider.Value;
        var ageStep = (int)ApplicationHistoryAgeSlider.Value;
        var query = ApplicationHistorySearchBox.Text.Trim();
        ApplicationHistorySizeLabel.Text = LocalizationService.Get(FindingFilter.GetSizeKey(sizeStep));
        ApplicationHistoryAgeLabel.Text = LocalizationService.Get(FindingFilter.GetAgeKey(ageStep));

        var filteredApplications = new List<ApplicationHistoryApplication>();
        foreach (var app in _applicationHistoryApplications)
        {
            var entries = app.Entries.Where(entry =>
            {
                if (availability == "Available" && !entry.ExistsNow) return false;
                if (availability == "Missing" && entry.ExistsNow) return false;
                if (pinned == "Pinned" && !entry.IsPinned) return false;
                if (pinned == "NotPinned" && entry.IsPinned) return false;
                if (risk == "Finding" && entry.EffectiveRisk == RiskLevel.None) return false;
                if (risk == "Important" && entry.EffectiveRisk < RiskLevel.High) return false;
                if (risk == "None" && entry.EffectiveRisk != RiskLevel.None) return false;
                if (sizeStep > 0 && (!entry.ExistsNow || entry.IsDirectory || entry.SizeBytes < FindingFilter.SizeThresholds[sizeStep])) return false;
                if (!MatchesApplicationHistoryAge(entry, ageStep)) return false;
                if (query.Length > 0 && !entry.TargetPath.Contains(query, StringComparison.CurrentCultureIgnoreCase) && !app.Identity.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase)) return false;
                return true;
            }).ToArray();
            if (sort == "AiPriority") entries = entries.OrderByDescending(x => x.PersonalAttentionScore ?? -1).ThenByDescending(x => x.LastInteraction).ToArray();
            if (entries.Length > 0) filteredApplications.Add(app with { Entries = entries });
        }
        if (sort == "AiPriority") filteredApplications = filteredApplications.OrderByDescending(x => x.PersonalAttentionScore ?? -1).ThenByDescending(x => x.Entries.Count).ToList();
        CaptureApplicationHistoryViewState();
        _visibleApplicationHistoryApplications.ReplaceRange(filteredApplications);
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(RestoreApplicationHistoryViewState));
    }

    void CaptureApplicationHistoryViewState()
    {
        if (ApplicationHistoryScrollViewer is not null)
            _applicationHistoryScrollOffset = ApplicationHistoryScrollViewer.VerticalOffset;

        _expandedApplicationHistoryKeys.Clear();
        foreach (var expander in FindVisualChildren<Expander>(ApplicationHistoryList))
        {
            if (!expander.IsExpanded || expander.DataContext is not ApplicationHistoryApplication application) continue;
            _expandedApplicationHistoryKeys.Add(application.Identity.AppId);
        }
    }

    void RestoreApplicationHistoryViewState()
    {
        foreach (var expander in FindVisualChildren<Expander>(ApplicationHistoryList))
        {
            if (expander.DataContext is ApplicationHistoryApplication application &&
                _expandedApplicationHistoryKeys.Contains(application.Identity.AppId))
                expander.IsExpanded = true;
        }
        ApplicationHistoryScrollViewer?.ScrollToVerticalOffset(_applicationHistoryScrollOffset);
    }

    async Task ApplyApplicationHistoryPersonalStateAsync(CancellationToken token = default)
    {
        var items = _applicationHistoryApplications.SelectMany(app => app.Entries).ToArray();
        if (items.Length == 0) { RefreshApplicationHistoryFilter(); return; }
        var feedback = _db.GetPersonalFeedback(PersonalAttentionSchema.Version)
            .GroupBy(x => x.PathKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(v => v.UpdatedAt).First(), StringComparer.OrdinalIgnoreCase);
        var findingsById = _findings.ToDictionary(x => x.Id);
        var features = items.Select(entry => PersonalAttentionFeatureExtractor.Extract(entry,
            entry.RelatedFindingId is Guid id && findingsById.TryGetValue(id, out var related) ? related : null)).ToArray();
        var scores = await _personalModel.PredictManyAsync(features, token);
        for (var i = 0; i < items.Length; i++)
        {
            feedback.TryGetValue(PersonalAttentionFeatureExtractor.ApplicationHistoryFeedbackKey(items[i]), out var rating);
            items[i].PersonalAttentionLabel = rating?.Label;
            items[i].PersonalAttentionScore = scores[i];
        }
        RefreshApplicationHistoryFilter();
    }

    async void ApplicationHistoryPersonalFeedback_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { DataContext: ApplicationHistoryEntry entry } button) return;
        bool? label = button.Tag?.ToString() switch { "True" => true, "False" => false, _ => null };
        var related = entry.RelatedFindingId is Guid id ? _findings.FirstOrDefault(x => x.Id == id) : null;
        var key = PersonalAttentionFeatureExtractor.ApplicationHistoryFeedbackKey(entry);
        _db.SetPersonalFeedback($"history:{entry.ApplicationKey}", key, PersonalAttentionFeatureExtractor.Extract(entry, related, label ?? false), label);
        entry.PersonalAttentionLabel = label;
        UpdatePersonalModelStats();
        var stats = _db.GetPersonalModelStats(_personalModel.Metadata?.TrainedSamples ?? 0);
        if (stats.CanTrain && (!_personalModel.IsReady || stats.Total - stats.TrainedSamples >= PersonalAttentionSchema.RetrainInterval))
            await TrainPersonalModelAsync();
        e.Handled = true;
    }

    static bool MatchesApplicationHistoryAge(ApplicationHistoryEntry entry, int ageStep)
    {
        if (ageStep <= 0) return true;
        if (!entry.ExistsNow || entry.TargetModifiedAt is null) return false;
        return FindingFilter.MatchesAge(new Finding { ModifiedAt = entry.TargetModifiedAt }, ageStep);
    }

    void ApplicationHistoryEntry_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject origin)
            for (var current = origin; current is not null; current = VisualTreeHelper.GetParent(current))
                if (current is System.Windows.Controls.Button) return;
        if (e.ClickCount != 2 || (sender as FrameworkElement)?.DataContext is not ApplicationHistoryEntry entry) return;
        if (entry.IsDirectory && entry.ExistsNow)
        {
            OpenFolderInExplorer(entry.TargetPath);
            e.Handled = true;
            return;
        }
        SelectFindingAndShowDetails(CreateApplicationHistoryFinding(entry), DetailsReturnSource.ApplicationHistory);
        e.Handled = true;
    }

    void ApplicationHistoryContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu || menu.DataContext is not ApplicationHistoryEntry entry || menu.Items.Count < 4) return;
        var open = (MenuItem)menu.Items[0]; var folder = (MenuItem)menu.Items[1]; var delete = (MenuItem)menu.Items[2];
        open.Visibility = entry.ExistsNow && !entry.IsDirectory ? Visibility.Visible : Visibility.Collapsed;
        folder.Visibility = entry.ExistsNow ? Visibility.Visible : Visibility.Collapsed;
        delete.Visibility = entry.ExistsNow && !entry.IsDirectory ? Visibility.Visible : Visibility.Collapsed;
    }

    async void ApplicationHistoryCopyPath_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ApplicationHistoryEntry entry) return;
        if (await TryCopyApplicationHistoryPathAsync(entry.TargetPath))
        {
            StatusText.Text = LocalizationService.Get("ApplicationHistoryPathCopied");
            return;
        }
        StatusText.Text = LocalizationService.Get("ApplicationHistoryClipboardBusy");
    }

    static Task<bool> TryCopyApplicationHistoryPathAsync(string path)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    System.Windows.Clipboard.SetText(path);
                    completion.TrySetResult(true);
                    return;
                }
                catch (System.Runtime.InteropServices.COMException) when (attempt < 4)
                {
                    Thread.Sleep(60);
                }
                catch
                {
                    completion.TrySetResult(false);
                    return;
                }
            }
            completion.TrySetResult(false);
        })
        {
            IsBackground = true,
            Name = "ApplicationHistoryClipboard"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    void ApplicationHistoryOpen_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ApplicationHistoryEntry entry && entry.ExistsNow) OpenMediaFile(entry.TargetPath);
    }

    void ApplicationHistoryShowFolder_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ApplicationHistoryEntry entry || !entry.ExistsNow) return;
        if (entry.IsDirectory) OpenFolderInExplorer(entry.TargetPath); else ShowFindingInFolder(CreateApplicationHistoryFinding(entry));
    }

    void ApplicationHistoryDelete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ApplicationHistoryEntry entry || !entry.ExistsNow || entry.IsDirectory) return;
        DeleteFinding(CreateApplicationHistoryFinding(entry));
        if (!File.Exists(entry.TargetPath))
        {
            for (var i = 0; i < _applicationHistoryApplications.Count; i++)
            {
                var app = _applicationHistoryApplications[i];
                _applicationHistoryApplications[i] = app with { Entries = app.Entries.Select(x => x.TargetPath.Equals(entry.TargetPath, StringComparison.OrdinalIgnoreCase) ? x with { ExistsNow = false } : x).ToArray() };
            }
            RefreshApplicationHistoryFilter();
        }
    }

    Finding CreateApplicationHistoryFinding(ApplicationHistoryEntry entry)
    {
        if (entry.RelatedFindingId is Guid id && _findings.FirstOrDefault(x => x.Id == id) is Finding related) return related;
        return new Finding
        {
            ScannerId = "application-history", Category = entry.IsDirectory ? "Directory" : Classifier.File(entry.TargetPath),
            Subcategory = entry.ExistsNow ? "Remembered by an application" : "Historical path",
            Path = entry.TargetPath, DisplayName = Path.GetFileName(entry.TargetPath.TrimEnd('\\', '/')),
            IsDirectory = entry.IsDirectory, SizeBytes = entry.SizeBytes, ModifiedAt = entry.TargetModifiedAt ?? entry.LastInteraction,
            ExposureScore = entry.HistoricalExposureScore,
            ExposureReasons = [entry.ExistsNow ? "Windows remembers this object in application history" : "Windows retains a historical path to an object that is no longer available"]
        };
    }

    void ApplyApplicationHistoryContext(ApplicationHistoryAnalysis analysis)
    {
        var byId = _findings.ToDictionary(x => x.Id);
        foreach (var group in analysis.Applications
            .SelectMany(app => app.Entries.Where(entry => entry.RelatedFindingId is not null).Select(entry => (app.Identity.DisplayName, Entry: entry)))
            .GroupBy(x => x.Entry.RelatedFindingId!.Value))
        {
            if (!byId.TryGetValue(group.Key, out var finding)) continue;
            finding.ApplicationHistoryReferences = string.Join(", ", group.Select(x => x.DisplayName).Distinct(StringComparer.CurrentCultureIgnoreCase));
            finding.ApplicationHistoryLastSeen = group.Max(x => x.Entry.LastInteraction);
            finding.ApplicationHistoryInteractionCount = group.Max(x => x.Entry.InteractionCount);
        }
    }

    void PresetBox_SelectionChanged(object? sender, SelectionChangedEventArgs e) => ConfigureScanTarget();

    void ConfigureScanTarget()
    {
        if (PresetBox is null || RootsBox is null || _driveBox is null) return;
        var full = GetSelectedPreset() == ScanPreset.Full;
        RootsBox.Visibility = full ? Visibility.Collapsed : Visibility.Visible;
        if (_chooseButton is not null) _chooseButton.Visibility = full ? Visibility.Collapsed : Visibility.Visible;
        _driveBox.Visibility = full ? Visibility.Visible : Visibility.Collapsed;
        if (full && _driveBox.Items.Count == 0)
        {
            foreach (var drive in DriveInfo.GetDrives().Where(x => x.IsReady))
                _driveBox.Items.Add(new ComboBoxItem { Content = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.Name : $"{drive.Name} ({drive.VolumeLabel})", Tag = drive.RootDirectory.FullName });
            if (_driveBox.Items.Count > 0) _driveBox.SelectedIndex = 0;
        }
        if (full) RootsBox.Text = (_driveBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? RootsBox.Text;
    }

    void DriveBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (GetSelectedPreset() == ScanPreset.Full && _driveBox.SelectedItem is ComboBoxItem item && item.Tag is string root) RootsBox.Text = root;
    }

    void ConfigureStatusLayout()
    {
        if (Busy.Parent is not System.Windows.Controls.DockPanel panel) return;
        panel.Children.Remove(StatusText);
        panel.Children.Remove(CountersText);
        CountersText.SetCurrentValue(DockPanel.DockProperty, Dock.Right);
        CountersText.TextTrimming = TextTrimming.CharacterEllipsis;
        panel.Children.Add(StatusText);
        panel.Children.Add(CountersText);
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => RequestCancellation(_cts);
    void BrowseRoot_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = LocalizationService.Get("SelectFolderDescription"),
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(RootsBox.Text) ? RootsBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ShowNewFolderButton = false
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            RootsBox.Text = GetSelectedPreset() == ScanPreset.Full
                ? Path.GetPathRoot(dialog.SelectedPath) ?? dialog.SelectedPath
                : dialog.SelectedPath;
        }
    }
    void Elevate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var executable = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executable)) return;
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" });
            System.Windows.Application.Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception) { StatusText.Text = LocalizationService.Get("CanceledUac"); }
    }
    void BuildDashboard()
    {
        DashboardPanel.Children.Clear();
        EmptyDashboard.Visibility = _findings.Any(x => !x.Ignored) ? Visibility.Collapsed : Visibility.Visible;
        foreach (var g in _findings.Where(x => !x.Ignored).GroupBy(x => x.Category).OrderByDescending(g => g.Sum(x => x.SizeBytes)))
        {
            var b = new System.Windows.Controls.Button { Width = 250, Height = 116, Margin = new(0, 0, 12, 12), Padding = new(20), HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left, VerticalContentAlignment = System.Windows.VerticalAlignment.Center, Background = (System.Windows.Media.Brush)FindResource("Surface"), Content = $"{g.Key}\n\n{g.Count():N0} объектов   ·   {Format.Bytes(g.Sum(x => x.SizeBytes))}" };
            b.Click += (_, _) => { var item = CategoryFilter.Items.OfType<ComboBoxItem>().FirstOrDefault(x => x.Tag?.ToString() == g.Key); if (item is not null) CategoryFilter.SelectedItem = item; MainTabs.SelectedIndex = 1; };
            DashboardPanel.Children.Add(b);
        }
    }
    void RebuildCategories()
    {
        var selectedTag = (CategoryFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "All";
        CategoryFilter.Items.Clear();
        CategoryFilter.Items.Add(new ComboBoxItem { Content = LocalizationService.Get("AllCategories"), Tag = "All" });
        CategoryFilter.Items.Add(new ComboBoxItem { Content = LocalizationService.Get("PiiFilter"), Tag = "PII" });
        CategoryFilter.Items.Add(new ComboBoxItem { Content = LocalizationService.Get("SecretsFilter"), Tag = "Secrets" });
        CategoryFilter.Items.Add(new ComboBoxItem { Content = LocalizationService.Get("ConfigsFilter"), Tag = "Configs" });
        CategoryFilter.Items.Add(new ComboBoxItem { Content = LocalizationService.Get("IdentityFilter"), Tag = "Identity" });

        var categories = _findings.Select(x => x.Category).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (_findings.Any(x => ArchiveInspector.IsSupportedArchive(x.Path)) && !categories.Contains("Archives", StringComparer.OrdinalIgnoreCase)) categories.Add("Archives");
        foreach (var c in categories.Order())
        {
            CategoryFilter.Items.Add(new ComboBoxItem { Content = c == "Archives" ? LocalizationService.Get("ArchivesFilter") : c, Tag = c, ToolTip = c == "Jump Lists" ? LocalizationService.Get("JumpListsHelp") : c == "Archives" ? LocalizationService.Get("ArchivesScanHelp") : null });
        }

        var toSelect = CategoryFilter.Items.OfType<ComboBoxItem>().FirstOrDefault(x => x.Tag?.ToString() == selectedTag) ?? CategoryFilter.Items.OfType<ComboBoxItem>().First();
        CategoryFilter.SelectedItem = toSelect;
    }
    bool MatchesFilter(Finding f)
    {
        if (f.Ignored) return false;
        var q = SearchBox?.Text ?? ""; if (q.Length > 0 && !f.Path.Contains(q, StringComparison.OrdinalIgnoreCase) && !f.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase)) return false;
        var risk = (RiskFilter?.SelectedItem as ComboBoxItem)?.Tag?.ToString(); if (risk is not null and not "All" && !string.Equals(risk, f.RiskLevel.ToString(), StringComparison.OrdinalIgnoreCase)) return false;
        var cat = (CategoryFilter?.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (cat is not null and not "All")
        {
            if (cat == "PII")
            {
                if (!PiiDetectionResult.TryParse(f.MetadataJson, out var pii) || pii!.TotalMatches == 0) return false;
            }
            else if (cat == "Secrets")
            {
                if (!SecretDetectionResult.TryParse(f.MetadataJson, out var sec) || sec!.TotalMatches == 0) return false;
            }
            else if (cat == "Configs")
            {
                if (!CredentialConfigResult.TryParse(f.MetadataJson, out var cfg) || !cfg!.IsCredentialConfig) return false;
            }
            else if (cat == "Identity")
            {
                if (!IdentityTraceResult.TryParse(f.MetadataJson, out var idt) || !idt!.HasIdentityTrace) return false;
            }
            else if (cat == "Archives")
            {
                if (!ArchiveInspectionResult.TryParse(f.MetadataJson, out var arch) || !arch!.IsArchive || arch.SensitiveEntriesCount == 0) return false;
            }
            else if (cat != f.Category) return false;
        }
        if (ExposureOnly?.IsChecked == true && f.ExposureScore < 40 && f.Category is not "Recent" and not "Jump Lists") return false;
        if (PersonalRecommendationOnly?.IsChecked == true && (f.PersonalAttentionScore is null or < 60)) return false;
        var sizeStep = FindingsSizeSlider is not null ? (int)FindingsSizeSlider.Value : 0;
        if (!FindingFilter.MatchesSize(f, sizeStep)) return false;
        var ageStep = FindingsAgeSlider is not null ? (int)FindingsAgeSlider.Value : 0;
        if (!FindingFilter.MatchesAge(f, ageStep)) return false;
        return true;
    }
    void Filter_Changed(object sender, EventArgs e)
    {
        if (!IsInitialized || _updatingFindingsUi) return;
        UpdateFindingsPresentation();
        RefreshFindingsPage(true);
    }
    void FindingsFilter_SliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized || _updatingFindingsUi) return;
        if (FindingsSizeLabel is not null && FindingsSizeSlider is not null)
            FindingsSizeLabel.Text = LocalizationService.Get(FindingFilter.GetSizeKey((int)FindingsSizeSlider.Value));
        if (FindingsAgeLabel is not null && FindingsAgeSlider is not null)
            FindingsAgeLabel.Text = LocalizationService.Get(FindingFilter.GetAgeKey((int)FindingsAgeSlider.Value));
        Filter_Changed(sender, e);
    }
    void MediaFilter_SliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized) return;
        if (MediaSizeLabel is not null && MediaSizeSlider is not null)
            MediaSizeLabel.Text = LocalizationService.Get(FindingFilter.GetSizeKey((int)MediaSizeSlider.Value));
        if (MediaAgeLabel is not null && MediaAgeSlider is not null)
            MediaAgeLabel.Text = LocalizationService.Get(FindingFilter.GetAgeKey((int)MediaAgeSlider.Value));
        UpdatePeoplePresentation();
    }
    async void RefreshFindingsPage(bool resetPage = false)
    {
        if (!IsInitialized || FindingsPageStatus is null) return;
        _pageLoadCts?.Cancel();
        _pageLoadCts?.Dispose();
        _pageLoadCts = new CancellationTokenSource();
        var loadSource = _pageLoadCts;
        var token = loadSource.Token;
        if (resetPage) _findingsPage = 0;
        _sortedFindings = FindingPagination.Sort(_findings.Where(MatchesFilter), _sortProperty, _sortDescending).ToArray();
        var pageSize = _imageTileMode && IsImagesFilter ? FindingPagination.TilePageSize(FindingsTileZoom.Value) : FindingPagination.ListPageSize;
        var page = FindingPagination.Slice(_sortedFindings, _findingsPage, pageSize);
        _findingsPage = page.PageIndex;
        _loadingFindingBatch = true;
        FindingsPageStatus.Text = LocalizationService.Get("PageLoading");
        try
        {
            await Task.Yield();
            token.ThrowIfCancellationRequested();
            await Dispatcher.InvokeAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                _visibleFindings.ReplaceRange(page.Items);
                _loadedFindingBatches.Clear();
                _loadedFindingBatches.Enqueue(page.Items.Count);
                _firstLoadedPage = _lastLoadedPage = page.PageIndex;
                _userScrollPending = false;
                FindingsPageStatus.Text = string.Format(LocalizationService.Get("PageStatus"), page.PageIndex + 1, page.PageCount, page.TotalCount, page.PageSize);
            });
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(_pageLoadCts, loadSource)) _loadingFindingBatch = false; }
    }
    void Findings_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Do not let wheel events accumulate while the next page is being prepared.
        // Otherwise the queued boundary events all fire when loading completes.
        if (_loadingFindingBatch)
        {
            _userScrollPending = false;
            e.Handled = true;
            return;
        }
        _userScrollPending = true;
    }
    void FindingsScroll_Changed(object sender, ScrollChangedEventArgs e)
    {
        if (_loadingFindingBatch) { _userScrollPending = false; return; }
        if (!_userScrollPending || e.ExtentHeight <= e.ViewportHeight) return;
        if (e.VerticalChange > 0 && e.VerticalOffset + (e.ViewportHeight * 1.5) >= e.ExtentHeight)
        {
            _userScrollPending = false;
            AppendNextFindingBatch();
        }
        else if (e.VerticalChange < 0 && e.VerticalOffset <= e.ViewportHeight * 0.5)
        {
            _userScrollPending = false;
            PrependPreviousFindingBatch();
        }
    }
    async void AppendNextFindingBatch()
    {
        if (_sortedFindings.Length == 0) return;
        var pageSize = _imageTileMode && IsImagesFilter ? FindingPagination.TilePageSize(FindingsTileZoom.Value) : FindingPagination.ListPageSize;
        var pageCount = Math.Max(1, (int)Math.Ceiling(_sortedFindings.Length / (double)pageSize));
        if (_lastLoadedPage + 1 >= pageCount) return;
        _loadingFindingBatch = true;
        _pageLoadCts?.Cancel();
        _pageLoadCts = new CancellationTokenSource();
        var loadSource = _pageLoadCts;
        var token = loadSource.Token;
        try
        {
            var nextPage = _lastLoadedPage + 1;
            var page = FindingPagination.Slice(_sortedFindings, nextPage, pageSize);
            FindingsPageStatus.Text = LocalizationService.Get("PageLoading");
            await Task.Yield();
            token.ThrowIfCancellationRequested();
            await Dispatcher.InvokeAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                _lastLoadedPage = nextPage;
                _visibleFindings.AddRange(page.Items);
                _loadedFindingBatches.Enqueue(page.Items.Count);
                while (_loadedFindingBatches.Count > 3)
                {
                    var remove = _loadedFindingBatches.Dequeue();
                    _visibleFindings.RemoveRange(0, remove);
                    _firstLoadedPage++;
                }
                _findingsPage = _lastLoadedPage;
                FindingsPageStatus.Text = string.Format(LocalizationService.Get("PageStatus"), _lastLoadedPage + 1, page.PageCount, page.TotalCount, page.PageSize);
            });
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(_pageLoadCts, loadSource)) _loadingFindingBatch = false; }
    }
    async void PrependPreviousFindingBatch()
    {
        if (_sortedFindings.Length == 0 || _firstLoadedPage <= 0) return;
        var pageSize = _imageTileMode && IsImagesFilter ? FindingPagination.TilePageSize(FindingsTileZoom.Value) : FindingPagination.ListPageSize;
        _loadingFindingBatch = true;
        _pageLoadCts?.Cancel();
        _pageLoadCts = new CancellationTokenSource();
        var loadSource = _pageLoadCts;
        var token = loadSource.Token;
        try
        {
            var previousPage = _firstLoadedPage - 1;
            var page = FindingPagination.Slice(_sortedFindings, previousPage, pageSize);
            FindingsPageStatus.Text = LocalizationService.Get("PageLoading");
            await Task.Yield();
            token.ThrowIfCancellationRequested();
            await Dispatcher.InvokeAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                _firstLoadedPage = previousPage;
                _visibleFindings.InsertRange(0, page.Items);
                var existingCounts = _loadedFindingBatches.ToArray();
                _loadedFindingBatches.Clear();
                _loadedFindingBatches.Enqueue(page.Items.Count);
                foreach (var count in existingCounts) _loadedFindingBatches.Enqueue(count);
                while (_loadedFindingBatches.Count > 3)
                {
                    var counts = _loadedFindingBatches.ToArray();
                    var remove = counts[^1];
                    _loadedFindingBatches.Clear();
                    foreach (var count in counts[..^1]) _loadedFindingBatches.Enqueue(count);
                    _visibleFindings.RemoveRange(Math.Max(0, _visibleFindings.Count - remove), remove);
                    _lastLoadedPage--;
                }
                _findingsPage = _firstLoadedPage;
                FindingsPageStatus.Text = string.Format(LocalizationService.Get("PageStatus"), _firstLoadedPage + 1, Math.Max(1, (int)Math.Ceiling(_sortedFindings.Length / (double)pageSize)), _sortedFindings.Length, pageSize);
            });
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(_pageLoadCts, loadSource)) _loadingFindingBatch = false; }
    }
    bool IsImagesFilter => (CategoryFilter?.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "Images";
    void UpdateFindingsPresentation()
    {
        if (!IsInitialized || RiskColumn is null) return;
        var images = IsImagesFilter;
        _updatingFindingsUi = true;
        try
        {
            if (images)
            {
                RiskFilter.SelectedIndex = 0;
                ExposureOnly.IsChecked = false;
                if (_sortProperty is nameof(Finding.RiskLevel) or nameof(Finding.ExposureScore) or nameof(Finding.ModifiedAt))
                {
                    _sortProperty = nameof(Finding.SizeBytes);
                    _sortDescending = true;
                }
            }
            else _imageTileMode = false;
        }
        finally { _updatingFindingsUi = false; }
        RiskFilter.Visibility = images ? Visibility.Collapsed : Visibility.Visible;
        ExposureOnly.Visibility = images ? Visibility.Collapsed : Visibility.Visible;
        RiskColumn.Visibility = images ? Visibility.Collapsed : Visibility.Visible;
        ModifiedColumn.Visibility = images ? Visibility.Collapsed : Visibility.Visible;
        ImageModeControls.Visibility = images ? Visibility.Visible : Visibility.Collapsed;
        FindingsGrid.Visibility = _imageTileMode ? Visibility.Collapsed : Visibility.Visible;
        FindingsTileList.Visibility = _imageTileMode ? Visibility.Visible : Visibility.Collapsed;
        TileScaleControls.Visibility = images && _imageTileMode ? Visibility.Visible : Visibility.Collapsed;
        FindingsViewToggle.Content = LocalizationService.Get(_imageTileMode ? "ListView" : "TileView");
        UpdatePeoplePresentation();
    }

    async void UpdatePeoplePresentation()
    {
        _mediaFilterCts?.Cancel();
        _mediaFilterCts?.Dispose();
        var loadSource = new CancellationTokenSource();
        _mediaFilterCts = loadSource;
        var token = loadSource.Token;
        var filter = (MediaPeopleFilter?.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var sizeStep = MediaSizeSlider is not null ? (int)MediaSizeSlider.Value : 0;
        var ageStep = MediaAgeSlider is not null ? (int)MediaAgeSlider.Value : 0;
        var now = DateTime.Now;
        var mediaSnapshot = _mediaFindings.ToArray();
        MediaLoadingPanel.Visibility = Visibility.Visible;
        MediaTileList.Visibility = Visibility.Collapsed;
        MediaEmptyPanel.Visibility = Visibility.Collapsed;
        MediaLoadingProgress.IsIndeterminate = true;
        MediaLoadingText.Text = LocalizationService.Get("MediaLoading");
        MediaCountText.Text = string.Format(LocalizationService.Get("MediaCount"), 0, _mediaFindings.Count);
        try
        {
            // Give WPF one render pass so the loader is visible before parsing metadata.
            await Task.Yield();
            var prepared = await Task.Run(() =>
            {
                var filtered = new List<Finding>();
                var records = 0; var people = 0; var noPeople = 0; var errors = 0; var index = 0;
                foreach (var finding in mediaSnapshot)
                {
                    if ((index++ & 255) == 0) token.ThrowIfCancellationRequested();
                    if (!FindingFilter.MatchesSize(finding, sizeStep)) continue;
                    if (!FindingFilter.MatchesAge(finding, ageStep, now)) continue;
                    var hasResult = PeopleScanMetadata.TryParse(finding.MetadataJson, out var result);
                    var isDoc = DocumentDetectionResult.TryParse(finding.MetadataJson, out var docResult) && docResult!.IsDocument;
                    if (hasResult)
                    {
                        records++;
                        if (result!.Status == PeopleScanStatus.Error) errors++;
                        else if (result.Status == PeopleScanStatus.Completed && result.PeopleDetected) people++;
                        else if (result.Status == PeopleScanStatus.Completed) noPeople++;
                    }
                    var matches = filter switch
                    {
                        "GeoExif" => ExifMetadataResult.TryParse(finding.MetadataJson, out var exif) && exif!.DisclosedFields.Count > 0,
                        "GpsOnly" => ExifMetadataResult.TryParse(finding.MetadataJson, out var exif) && exif!.HasGeolocation,
                        "Documents" => isDoc,
                        "People" => hasResult && result!.Status == PeopleScanStatus.Completed && result.PeopleDetected,
                        "NoPeople" => hasResult && result!.Status == PeopleScanStatus.Completed && !result.PeopleDetected,
                        "Errors" => hasResult && result!.Status == PeopleScanStatus.Error,
                        "Unscanned" => !hasResult,
                        _ => true
                    };
                    if (matches) filtered.Add(finding);
                }
                return (Filtered: filtered.ToArray(), Records: records, People: people, NoPeople: noPeople, Errors: errors);
            }, token);
            await Dispatcher.InvokeAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                _sortedMediaFindings = prepared.Filtered;
                var page = FindingPagination.Slice(_sortedMediaFindings, 0, FindingPagination.TilePageSize(MediaTileZoom.Value));
                _visibleMediaFindings.ReplaceRange(page.Items);
                _loadedMediaBatches.Clear();
                _loadedMediaBatches.Enqueue(page.Items.Count);
                _firstLoadedMediaPage = _lastLoadedMediaPage = 0;
                MediaCountText.Text = string.Format(LocalizationService.Get("MediaCount"), page.Items.Count, prepared.Filtered.Length);
                PeopleScanStatsText.Text = prepared.Records == 0
                    ? LocalizationService.Get("PeopleScanNotRun")
                    : string.Format(LocalizationService.Get("PeopleScanComplete"), prepared.Records, prepared.People, prepared.NoPeople, prepared.Errors);
                _completedPeopleResults = prepared.People + prepared.NoPeople;
                UpdateModelControls();
                MediaLoadingPanel.Visibility = Visibility.Collapsed;
                MediaTileList.Visibility = prepared.Filtered.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                MediaEmptyPanel.Visibility = prepared.Filtered.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            });
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_mediaFilterCts, loadSource))
            {
                void FinishLoading() => MediaLoadingPanel.Visibility = Visibility.Collapsed;
                if (Dispatcher.CheckAccess()) FinishLoading(); else _ = Dispatcher.BeginInvoke((Action)FinishLoading);
                _mediaFilterCts = null;
            }
            loadSource.Dispose();
        }
    }

    bool HasPendingPeopleScanWork()
    {
        if (_sortedMediaFindings.Length == 0) return false;
        return _completedPeopleResults > 0 && _completedPeopleResults < _sortedMediaFindings.Length;
    }

    async Task ApplyPersistedPeopleResultsAsync(IEnumerable<Finding> images)
    {
        var candidates = images.ToArray();
        var restored = await Task.Run(() => candidates
            .Select(f =>
            {
                var file = new FileInfo(f.Path);
                return (Finding: f, Result: file.Exists ? _peopleRepository.FindReusable(f.Path, file.Length, file.LastWriteTime, _modelManager.Manifest.ModelVersion) : null);
            })
            .Where(x => x.Result is not null)
            .ToArray());
        await Dispatcher.InvokeAsync(() =>
        {
            foreach (var item in restored) item.Finding.MetadataJson = PeopleScanMetadata.Serialize(item.Result!);
            if (restored.Length > 0)
            {
                SaveCurrentSnapshot();
                UpdatePeoplePresentation();
                RefreshFindingsPage(true);
            }
        });
    }

    void UpdateModelControls()
    {
        bool installed;
        try { installed = _modelManager.IsInstalled; }
        catch { installed = false; }
        var busy = _cts is not null;
        DownloadPeopleModelButton.Visibility = installed ? Visibility.Collapsed : Visibility.Visible;
        DownloadPeopleModelButton.IsEnabled = !busy;
        PeopleScanButton.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        PeopleScanButton.Content = LocalizationService.Get(HasPendingPeopleScanWork() ? "ContinuePeopleScan" : "SearchPeople");
        PeopleScanButton.IsEnabled = installed && !busy;
        RemovePeopleModelButton.IsEnabled = _modelManager.HasModelFiles && !busy;
        PeopleScanCancelButton.IsEnabled = busy;
    }
    void ToggleFindingsView_Click(object sender, RoutedEventArgs e)
    {
        if (!IsImagesFilter) return;
        _imageTileMode = !_imageTileMode;
        UpdateFindingsPresentation();
        RefreshFindingsPage(true);
    }
    void FindingsTileZoom_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FindingsTileZoomLabel is null) return;
        FindingsTileZoomLabel.Text = $"{e.NewValue:0} px";
        if (IsInitialized && _imageTileMode) RefreshFindingsPage(true);
    }
    void PreviousFindingsPage_Click(object sender, RoutedEventArgs e) { _findingsPage--; RefreshFindingsPage(); }
    void NextFindingsPage_Click(object sender, RoutedEventArgs e) { _findingsPage++; RefreshFindingsPage(); }
    void FindingsGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        var property = string.IsNullOrWhiteSpace(e.Column.SortMemberPath) ? nameof(Finding.ModifiedAt) : e.Column.SortMemberPath;
        _sortDescending = property == _sortProperty ? !_sortDescending : true;
        _sortProperty = property;
        var direction = _sortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        foreach (var column in FindingsGrid.Columns) if (!ReferenceEquals(column, e.Column)) column.SortDirection = null;
        e.Column.SortDirection = direction;
        RefreshFindingsPage(true);
    }
    async void DownloadPeopleModel_Click(object sender, RoutedEventArgs e)
    {
        if (_cts is not null) return;
        var guard = TryAcquireHeavyTask(LocalizationService.Get("PeopleModelDownload"));
        if (guard is null) return;

        try { if (_modelManager.IsInstalled) { UpdateModelControls(); return; } }
        catch (Exception ex) { ShowPeopleScanError(ex); return; }
        _cts = new CancellationTokenSource();
        UpdateModelControls(); CancelButton.IsEnabled = true; Busy.Visibility = Visibility.Visible;
        ResetPeopleOperationMessage(); PeopleModelProgress.Visibility = Visibility.Visible; PeopleModelProgress.IsIndeterminate = true; PeopleScanStageText.Text = LocalizationService.Get("PeopleScanConnecting");
        try
        {
            StatusText.Text = LocalizationService.Get("PeopleModelDownload");
            await _modelManager.EnsureInstalledDetailedAsync(new Progress<ModelDownloadProgress>(UpdateModelDownloadProgress), _cts.Token);
            PeopleModelProgress.IsIndeterminate = false; PeopleModelProgress.Value = 1; PeopleScanStageText.Text = LocalizationService.Get("PeopleModelInstalled"); PeopleScanProgressText.Text = LocalizationService.Get("PeopleModelInstalledDescription"); StatusText.Text = LocalizationService.Get("PeopleModelInstalled");
        }
        catch (OperationCanceledException) { PeopleScanStageText.Text = LocalizationService.Get("PeopleDownloadCanceled"); ResetPeopleOperationMessage(); StatusText.Text = LocalizationService.Get("PeopleDownloadCanceled"); }
        catch (Exception ex) { ShowPeopleScanError(ex); }
        finally
        {
            guard.Dispose();
            _cts.Dispose(); _cts = null; CancelButton.IsEnabled = false; Busy.Visibility = Visibility.Collapsed; UpdateModelControls();
        }
    }

    async void PeopleScan_Click(object sender, RoutedEventArgs e)
    {
        if (_cts is not null) return;
        var guard = TryAcquireHeavyTask(LocalizationService.Get("PeopleScanRunningStage"));
        if (guard is null) return;

        var images = _sortedMediaFindings.Where(x => File.Exists(x.Path)).ToArray();
        if (images.Length == 0) { guard.Dispose(); StatusText.Text = LocalizationService.Get("NoImagesForPeopleScan"); PeopleScanErrorText.Text = LocalizationService.Get("NoImagesForPeopleScan"); PeopleScanErrorText.Visibility = Visibility.Visible; return; }
        try { if (!_modelManager.IsInstalled) { guard.Dispose(); ShowPeopleScanError(new ModelDownloadException(LocalizationService.Get("PeopleModelRequired"), "model_missing")); return; } }
        catch (Exception ex) { guard.Dispose(); ShowPeopleScanError(ex); return; }

        _cts = new CancellationTokenSource();
        UpdateModelControls(); CancelButton.IsEnabled = true; Busy.Visibility = Visibility.Visible;
        ResetPeopleOperationMessage(); PeopleModelProgress.Visibility = Visibility.Visible; PeopleModelProgress.IsIndeterminate = true; PeopleScanStageText.Text = LocalizationService.Get("PeopleScanCheckingModel");
        try
        {
            PeopleScanStageText.Text = LocalizationService.Get("PeopleScanRunningStage"); PeopleModelProgress.IsIndeterminate = true;
            var progress = new Progress<PeopleScanProgress>(p =>
            {
                StatusText.Text = string.Format(LocalizationService.Get("PeopleScanRunning"), p.Completed, p.Total);
                PeopleScanStageText.Text = LocalizationService.Get("PeopleScanRunningStage"); PeopleModelProgress.IsIndeterminate = false; PeopleModelProgress.Value = p.Total == 0 ? 0 : p.Completed / (double)p.Total; PeopleScanProgressText.Text = $"{p.Completed:N0} / {p.Total:N0}";
                CountersText.Text = $"{LocalizationService.Get("PeopleDetected")}: {p.People:N0}   {LocalizationService.Get("PeopleScanErrors")}: {p.Errors:N0}";
            });
            var results = await new PeopleScanner(_modelManager, _peopleRepository).ScanAsync(images, progress, _cts.Token);
            foreach (var result in results)
            {
                var finding = _findings.FirstOrDefault(x => x.Path.Equals(result.Path, StringComparison.OrdinalIgnoreCase));
                if (finding is not null) finding.MetadataJson = PeopleScanMetadata.Serialize(result);
            }
            SaveCurrentSnapshot(); UpdatePeoplePresentation(); RefreshFindingsPage(true);
            var people = results.Count(x => x.Status == PeopleScanStatus.Completed && x.PeopleDetected);
            var noPeople = results.Count(x => x.Status == PeopleScanStatus.Completed && !x.PeopleDetected);
            var errors = results.Count(x => x.Status == PeopleScanStatus.Error);
            PeopleModelProgress.IsIndeterminate = false; PeopleModelProgress.Value = 1; PeopleScanStageText.Text = LocalizationService.Get("PeopleScanCompleted"); ResetPeopleOperationMessage();
            if (errors > 0)
            {
                var firstError = results.FirstOrDefault(x => x.Status == PeopleScanStatus.Error)?.Error;
                PeopleScanErrorText.Text = string.Format(LocalizationService.Get("PeopleScanErrorsDetails"), errors, string.IsNullOrWhiteSpace(firstError) ? LocalizationService.Get("PeopleScanFailedTitle") : firstError);
                PeopleScanErrorText.Visibility = Visibility.Visible;
            }
            StatusText.Text = string.Format(LocalizationService.Get("PeopleScanComplete"), results.Count, people, noPeople, errors);
        }
        catch (OperationCanceledException)
        {
            await ApplyPersistedPeopleResultsAsync(images);
            PeopleScanStageText.Text = LocalizationService.Get("PeopleScanCanceled"); ResetPeopleOperationMessage(); StatusText.Text = LocalizationService.Get("PeopleScanCanceled");
        }
        catch (Exception ex) { ShowPeopleScanError(ex); }
        finally
        {
            guard.Dispose();
            _cts.Dispose(); _cts = null; CancelButton.IsEnabled = false; Busy.Visibility = Visibility.Collapsed; UpdateModelControls();
        }
    }

    void PeopleScanCancel_Click(object sender, RoutedEventArgs e)
    {
        var source = _cts ?? _documentScanCts ?? _exifScanCts ?? _similarCts;
        RequestCancellation(source);
        if (!ReferenceEquals(source, _cts)) RequestCancellation(_cts);
        if (!ReferenceEquals(source, _documentScanCts)) RequestCancellation(_documentScanCts);
        if (!ReferenceEquals(source, _exifScanCts)) RequestCancellation(_exifScanCts);
        if (!ReferenceEquals(source, _similarCts)) RequestCancellation(_similarCts);
    }

    void UpdateModelDownloadProgress(ModelDownloadProgress progress)
    {
        PeopleScanStageText.Text = progress.Stage switch
        {
            ModelDownloadStage.Checking => LocalizationService.Get("PeopleScanCheckingModel"),
            ModelDownloadStage.Connecting => LocalizationService.Get("PeopleScanConnecting"),
            ModelDownloadStage.Downloading => LocalizationService.Get("PeopleScanDownloading"),
            ModelDownloadStage.Verifying => LocalizationService.Get("PeopleScanVerifying"),
            ModelDownloadStage.DownloadingLicense => LocalizationService.Get("PeopleScanDownloadingLicense"),
            ModelDownloadStage.Installing => LocalizationService.Get("PeopleScanInstalling"),
            ModelDownloadStage.Completed => LocalizationService.Get("PeopleScanCompleted"),
            _ => LocalizationService.Get("PeopleModelDownload")
        };
        if (progress.Fraction is double fraction)
        {
            PeopleModelProgress.IsIndeterminate = false; PeopleModelProgress.Value = fraction;
            PeopleScanProgressText.Text = progress.TotalBytes is long total ? $"{Format.Bytes(progress.BytesReceived)} / {Format.Bytes(total)} ({fraction:P0})" : $"{Format.Bytes(progress.BytesReceived)} ({fraction:P0})";
        }
        else
        {
            PeopleModelProgress.IsIndeterminate = progress.Stage is ModelDownloadStage.Connecting or ModelDownloadStage.Downloading;
            PeopleScanProgressText.Text = progress.Stage == ModelDownloadStage.Downloading ? LocalizationService.Get("PeopleScanWaitingForNetwork") : "";
        }
        CountersText.Text = progress.TotalBytes is long bytes ? $"{Format.Bytes(progress.BytesReceived)} / {Format.Bytes(bytes)}" : "";
    }

    void ResetPeopleOperationMessage()
    {
        PeopleScanErrorText.Text = "";
        PeopleScanErrorText.Visibility = Visibility.Collapsed;
        PeopleScanProgressText.Text = "";
    }

    void ShowPeopleScanError(Exception exception, bool showDialog = false)
    {
        var message = exception switch
        {
            ModelDownloadException { Code: "timeout" } => LocalizationService.Get("PeopleDownloadTimeout"),
            ModelDownloadException { Code: "model_in_use" } => LocalizationService.Get("PeopleModelInUse"),
            ModelDownloadException { Code: "model_access_denied" } => LocalizationService.Get("PeopleModelAccessDenied"),
            ModelDownloadException { Code: "hash_mismatch" } => LocalizationService.Get("PeopleModelHashMismatch"),
            ModelDownloadException { Code: "model_missing" } => LocalizationService.Get("PeopleModelRequired"),
            System.Net.Http.HttpRequestException => LocalizationService.Get("PeopleNetworkError"),
            IOException => LocalizationService.Get("PeopleModelInUse"),
            _ => exception.Message
        };
        var details = $"{message}\n\n{string.Format(LocalizationService.Get("PeopleScanLogPath"), _modelManager.LogPath)}";
        PeopleScanStageText.Text = LocalizationService.Get("PeopleScanFailedTitle"); PeopleScanErrorText.Text = details; PeopleScanErrorText.Visibility = Visibility.Visible; PeopleScanProgressText.Text = ""; StatusText.Text = message;
        if (showDialog) System.Windows.MessageBox.Show(details, "NotBad Privacy Detector Agent", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    void RemovePeopleModel_Click(object sender, RoutedEventArgs e)
    {
        if (!_modelManager.HasModelFiles || _cts is not null) { UpdateModelControls(); return; }
        if (System.Windows.MessageBox.Show(LocalizationService.Get("PeopleModelRemovePrompt"), "NotBad Privacy Detector Agent", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try { _modelManager.RemoveInstalledModel(); PeopleModelProgress.Visibility = Visibility.Collapsed; PeopleScanStageText.Text = LocalizationService.Get("PeopleScanReady"); PeopleScanProgressText.Text = ""; PeopleScanErrorText.Visibility = Visibility.Collapsed; StatusText.Text = LocalizationService.Get("PeopleModelRemoved"); UpdateModelControls(); }
        catch (Exception ex) { ShowPeopleScanError(ex, false); }
    }

    void TextScanCancel_Click(object sender, RoutedEventArgs e)
    {
        RequestCancellation(_textScanCts);
    }

    void FindingsLegendToggle_Click(object sender, RoutedEventArgs e)
    {
        if (FindingsLegendPanel is null) return;
        FindingsLegendPanel.Visibility = FindingsLegendPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    void MediaLegendToggle_Click(object sender, RoutedEventArgs e)
    {
        if (MediaLegendPanel is null) return;
        MediaLegendPanel.Visibility = MediaLegendPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    void ResetFindingsFilters_Click(object sender, RoutedEventArgs e)
    {
        RiskFilter.SelectedIndex = 0;
        CategoryFilter.SelectedIndex = 0;
        SearchBox.Text = "";
        ExposureOnly.IsChecked = false;
        PersonalRecommendationOnly.IsChecked = false;
        FindingsSizeSlider.Value = 0;
        FindingsAgeSlider.Value = 0;
        _sortProperty = nameof(Finding.ModifiedAt);
        _sortDescending = true;
        foreach (var column in FindingsGrid.Columns) column.SortDirection = null;
        RefreshFindingsPage(true);
    }

    void ResetMediaFilters_Click(object sender, RoutedEventArgs e)
    {
        MediaPeopleFilter.SelectedIndex = 0;
        MediaSizeSlider.Value = 0;
        MediaAgeSlider.Value = 0;
        MediaTileZoom.Value = 180;
        UpdatePeoplePresentation();
    }

    void SetTextScanButtons(bool enabled)
    {
        PiiScanButton.IsEnabled = enabled;
        SecretsScanButton.IsEnabled = enabled;
        ConfigsScanButton.IsEnabled = enabled;
        IdentityScanButton.IsEnabled = enabled;
        ArchivesScanButton.IsEnabled = enabled;
        TextScanCancelButton.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        TextScanProgress.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
    }

    async void PiiScan_Click(object sender, RoutedEventArgs e)
    {
        if (_textScanCts is not null) return;
        var guard = TryAcquireHeavyTask(LocalizationService.Get("PiiScanning"));
        if (guard is null) return;

        var candidates = _findings.Where(x => !x.Ignored && TextExtractor.IsSupported(x.Path) && File.Exists(x.Path)).ToArray();
        if (candidates.Length == 0)
        {
            guard.Dispose();
            StatusText.Text = LocalizationService.Get("NoTextFilesForScan");
            TextScanStatusText.Text = LocalizationService.Get("NoTextFilesForScan");
            return;
        }

        _textScanCts = new CancellationTokenSource();
        var token = _textScanCts.Token;
        SetTextScanButtons(false);
        TextScanProgress.IsIndeterminate = false;
        TextScanProgress.Value = 0;
        TextScanStatusText.Text = LocalizationService.Get("PiiScanning");
        StatusText.Text = LocalizationService.Get("PiiScanning");

        int foundCount = 0;
        try
        {
            await Task.Run(async () =>
            {
                for (int i = 0; i < candidates.Length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var finding = candidates[i];
                    try
                    {
                        var text = TextExtractor.ExtractText(finding.Path);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            var scan = PiiDetector.Scan(text);
                            if (scan.TotalMatches > 0)
                            {
                                finding.MetadataJson = PiiDetectionResult.InjectIntoMetadata(finding.MetadataJson, scan);
                                Interlocked.Increment(ref foundCount);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        CrashLogger.LogException(ex, $"PiiDetector file processing: {finding.Path}");
                    }

                    if ((i + 1) % 5 == 0 || i == candidates.Length - 1)
                    {
                        var currentIdx = i + 1;
                        await Dispatcher.InvokeAsync(() =>
                        {
                            TextScanProgress.Value = (double)currentIdx / candidates.Length;
                            TextScanStatusText.Text = string.Format(LocalizationService.Get("TextScanProgressFormat"), currentIdx, candidates.Length);
                        });
                    }
                }
            }, token);

            SaveCurrentSnapshot();
            RebuildCategories();
            var piiItem = CategoryFilter.Items.OfType<ComboBoxItem>().FirstOrDefault(x => x.Tag?.ToString() == "PII");
            if (piiItem is not null) CategoryFilter.SelectedItem = piiItem;
            RefreshFindingsPage(true);
            BuildDashboard();

            var msg = string.Format(LocalizationService.Get("PiiComplete"), foundCount);
            StatusText.Text = msg;
            TextScanStatusText.Text = msg;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = LocalizationService.Get("TextScanCanceled");
            TextScanStatusText.Text = LocalizationService.Get("TextScanCanceled");
        }
        catch (Exception ex)
        {
            CrashLogger.ShowErrorDialog(ex, "Поиск ПДн", this);
            StatusText.Text = ex.Message;
            TextScanStatusText.Text = ex.Message;
        }
        finally
        {
            guard.Dispose();
            _textScanCts?.Dispose();
            _textScanCts = null;
            SetTextScanButtons(true);
        }
    }

    async void SecretsScan_Click(object sender, RoutedEventArgs e)
    {
        if (_textScanCts is not null) return;
        var guard = TryAcquireHeavyTask(LocalizationService.Get("SecretsScanning"));
        if (guard is null) return;

        var candidates = _findings.Where(x => !x.Ignored && TextExtractor.IsSupported(x.Path) && File.Exists(x.Path)).ToArray();
        if (candidates.Length == 0)
        {
            guard.Dispose();
            StatusText.Text = LocalizationService.Get("NoTextFilesForScan");
            TextScanStatusText.Text = LocalizationService.Get("NoTextFilesForScan");
            return;
        }

        _textScanCts = new CancellationTokenSource();
        var token = _textScanCts.Token;
        SetTextScanButtons(false);
        TextScanProgress.IsIndeterminate = false;
        TextScanProgress.Value = 0;
        TextScanStatusText.Text = LocalizationService.Get("SecretsScanning");
        StatusText.Text = LocalizationService.Get("SecretsScanning");

        int foundCount = 0;
        try
        {
            await Task.Run(async () =>
            {
                for (int i = 0; i < candidates.Length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var finding = candidates[i];
                    try
                    {
                        var text = TextExtractor.ExtractText(finding.Path);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            var scan = SecretDetector.Scan(text, finding.Path);
                            if (scan.TotalMatches > 0)
                            {
                                finding.MetadataJson = SecretDetectionResult.InjectIntoMetadata(finding.MetadataJson, scan);
                                Interlocked.Increment(ref foundCount);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        CrashLogger.LogException(ex, $"SecretDetector file processing: {finding.Path}");
                    }

                    if ((i + 1) % 5 == 0 || i == candidates.Length - 1)
                    {
                        var currentIdx = i + 1;
                        await Dispatcher.InvokeAsync(() =>
                        {
                            TextScanProgress.Value = (double)currentIdx / candidates.Length;
                            TextScanStatusText.Text = string.Format(LocalizationService.Get("TextScanProgressFormat"), currentIdx, candidates.Length);
                        });
                    }
                }
            }, token);

            SaveCurrentSnapshot();
            RebuildCategories();
            var secItem = CategoryFilter.Items.OfType<ComboBoxItem>().FirstOrDefault(x => x.Tag?.ToString() == "Secrets");
            if (secItem is not null) CategoryFilter.SelectedItem = secItem;
            RefreshFindingsPage(true);
            BuildDashboard();

            var msg = string.Format(LocalizationService.Get("SecretsComplete"), foundCount);
            StatusText.Text = msg;
            TextScanStatusText.Text = msg;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = LocalizationService.Get("TextScanCanceled");
            TextScanStatusText.Text = LocalizationService.Get("TextScanCanceled");
        }
        catch (Exception ex)
        {
            CrashLogger.ShowErrorDialog(ex, "Поиск секретов", this);
            StatusText.Text = ex.Message;
            TextScanStatusText.Text = ex.Message;
        }
        finally
        {
            guard.Dispose();
            _textScanCts?.Dispose();
            _textScanCts = null;
            SetTextScanButtons(true);
        }
    }

    async void ConfigsScan_Click(object sender, RoutedEventArgs e)
    {
        if (_textScanCts is not null) return;
        var guard = TryAcquireHeavyTask(LocalizationService.Get("ConfigsScanning"));
        if (guard is null) return;

        var candidates = _findings.Where(x => !x.Ignored && File.Exists(x.Path)).ToArray();
        if (candidates.Length == 0)
        {
            guard.Dispose();
            StatusText.Text = LocalizationService.Get("NoTextFilesForScan");
            TextScanStatusText.Text = LocalizationService.Get("NoTextFilesForScan");
            return;
        }

        _textScanCts = new CancellationTokenSource();
        var token = _textScanCts.Token;
        SetTextScanButtons(false);
        TextScanProgress.IsIndeterminate = false;
        TextScanProgress.Value = 0;
        TextScanStatusText.Text = LocalizationService.Get("ConfigsScanning");
        StatusText.Text = LocalizationService.Get("ConfigsScanning");

        int foundCount = 0;
        try
        {
            await Task.Run(async () =>
            {
                for (int i = 0; i < candidates.Length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var finding = candidates[i];
                    try
                    {
                        var scan = CredentialConfigDetector.Analyze(finding.Path);
                        if (scan.IsCredentialConfig)
                        {
                            finding.MetadataJson = CredentialConfigResult.InjectIntoMetadata(finding.MetadataJson, scan);
                            Interlocked.Increment(ref foundCount);
                        }
                    }
                    catch (Exception ex)
                    {
                        CrashLogger.LogException(ex, $"CredentialConfigDetector file processing: {finding.Path}");
                    }

                    if ((i + 1) % 10 == 0 || i == candidates.Length - 1)
                    {
                        var currentIdx = i + 1;
                        await Dispatcher.InvokeAsync(() =>
                        {
                            TextScanProgress.Value = (double)currentIdx / candidates.Length;
                            TextScanStatusText.Text = string.Format(LocalizationService.Get("TextScanProgressFormat"), currentIdx, candidates.Length);
                        });
                    }
                }
            }, token);

            SaveCurrentSnapshot();
            RebuildCategories();
            var cfgItem = CategoryFilter.Items.OfType<ComboBoxItem>().FirstOrDefault(x => x.Tag?.ToString() == "Configs");
            if (cfgItem is not null) CategoryFilter.SelectedItem = cfgItem;
            RefreshFindingsPage(true);
            BuildDashboard();

            var msg = string.Format(LocalizationService.Get("ConfigsComplete"), foundCount);
            StatusText.Text = msg;
            TextScanStatusText.Text = msg;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = LocalizationService.Get("TextScanCanceled");
            TextScanStatusText.Text = LocalizationService.Get("TextScanCanceled");
        }
        catch (Exception ex)
        {
            CrashLogger.ShowErrorDialog(ex, "Поиск конфигураций и учетных данных", this);
            StatusText.Text = ex.Message;
            TextScanStatusText.Text = ex.Message;
        }
        finally
        {
            guard.Dispose();
            _textScanCts?.Dispose();
            _textScanCts = null;
            SetTextScanButtons(true);
        }
    }

    async void IdentityScan_Click(object sender, RoutedEventArgs e)
    {
        if (_textScanCts is not null) return;
        var guard = TryAcquireHeavyTask(LocalizationService.Get("IdentityScanning"));
        if (guard is null) return;

        var candidates = _findings.Where(x => !x.Ignored && File.Exists(x.Path)).ToArray();
        if (candidates.Length == 0)
        {
            guard.Dispose();
            StatusText.Text = LocalizationService.Get("NoTextFilesForScan");
            TextScanStatusText.Text = LocalizationService.Get("NoTextFilesForScan");
            return;
        }

        _textScanCts = new CancellationTokenSource();
        var token = _textScanCts.Token;
        SetTextScanButtons(false);
        TextScanProgress.IsIndeterminate = false;
        TextScanProgress.Value = 0;
        TextScanStatusText.Text = LocalizationService.Get("IdentityScanning");
        StatusText.Text = LocalizationService.Get("IdentityScanning");

        int foundCount = 0;
        int totalMentions = 0;
        try
        {
            var profile = UserIdentityProfile.Collect();
            await Task.Run(async () =>
            {
                for (int i = 0; i < candidates.Length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var finding = candidates[i];
                    try
                    {
                        var scan = IdentityTraceDetector.Analyze(finding.Path, profile);
                        if (scan.HasIdentityTrace)
                        {
                            finding.MetadataJson = IdentityTraceResult.InjectIntoMetadata(finding.MetadataJson, scan);
                            Interlocked.Increment(ref foundCount);
                            Interlocked.Add(ref totalMentions, scan.TotalMentions);
                        }
                    }
                    catch (Exception ex)
                    {
                        CrashLogger.LogException(ex, $"IdentityTraceDetector file processing: {finding.Path}");
                    }

                    if ((i + 1) % 10 == 0 || i == candidates.Length - 1)
                    {
                        var currentIdx = i + 1;
                        await Dispatcher.InvokeAsync(() =>
                        {
                            TextScanProgress.Value = (double)currentIdx / candidates.Length;
                            TextScanStatusText.Text = string.Format(LocalizationService.Get("TextScanProgressFormat"), currentIdx, candidates.Length);
                        });
                    }
                }
            }, token);

            SaveCurrentSnapshot();
            RebuildCategories();
            var idItem = CategoryFilter.Items.OfType<ComboBoxItem>().FirstOrDefault(x => x.Tag?.ToString() == "Identity");
            if (idItem is not null) CategoryFilter.SelectedItem = idItem;
            RefreshFindingsPage(true);
            BuildDashboard();

            var msg = string.Format(LocalizationService.Get("IdentityComplete"), foundCount, totalMentions);
            StatusText.Text = msg;
            TextScanStatusText.Text = msg;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = LocalizationService.Get("TextScanCanceled");
            TextScanStatusText.Text = LocalizationService.Get("TextScanCanceled");
        }
        catch (Exception ex)
        {
            CrashLogger.ShowErrorDialog(ex, "Поиск цифрового следа личности", this);
            StatusText.Text = ex.Message;
            TextScanStatusText.Text = ex.Message;
        }
        finally
        {
            guard.Dispose();
            _textScanCts?.Dispose();
            _textScanCts = null;
            SetTextScanButtons(true);
        }
    }

    async void ArchivesScan_Click(object sender, RoutedEventArgs e)
    {
        if (_textScanCts is not null) return;
        var guard = TryAcquireHeavyTask(LocalizationService.Get("ArchivesScanning"));
        if (guard is null) return;

        var candidates = _findings.Where(x => !x.Ignored && ArchiveInspector.IsSupportedArchive(x.Path) && File.Exists(x.Path)).ToArray();
        if (candidates.Length == 0)
        {
            guard.Dispose();
            StatusText.Text = LocalizationService.Get("NoTextFilesForScan");
            TextScanStatusText.Text = LocalizationService.Get("NoTextFilesForScan");
            return;
        }

        _textScanCts = new CancellationTokenSource();
        var token = _textScanCts.Token;
        SetTextScanButtons(false);
        TextScanProgress.IsIndeterminate = false;
        TextScanProgress.Value = 0;
        TextScanStatusText.Text = LocalizationService.Get("ArchivesScanning");
        StatusText.Text = LocalizationService.Get("ArchivesScanning");

        int foundCount = 0;
        try
        {
            await Task.Run(async () =>
            {
                for (int i = 0; i < candidates.Length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var finding = candidates[i];
                    try
                    {
                        var scan = ArchiveInspector.Inspect(finding.Path);
                        if (scan.IsArchive)
                        {
                            finding.MetadataJson = ArchiveInspectionResult.InjectIntoMetadata(finding.MetadataJson, scan);
                            if (scan.SensitiveEntriesCount > 0)
                            {
                                Interlocked.Increment(ref foundCount);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        CrashLogger.LogException(ex, $"ArchiveInspector file processing: {finding.Path}");
                    }

                    if ((i + 1) % 2 == 0 || i == candidates.Length - 1)
                    {
                        var currentIdx = i + 1;
                        await Dispatcher.InvokeAsync(() =>
                        {
                            TextScanProgress.Value = (double)currentIdx / candidates.Length;
                            TextScanStatusText.Text = string.Format(LocalizationService.Get("TextScanProgressFormat"), currentIdx, candidates.Length);
                        });
                    }
                }
            }, token);

            SaveCurrentSnapshot();
            RebuildCategories();
            var archivesItem = CategoryFilter.Items.OfType<ComboBoxItem>().FirstOrDefault(x => x.Tag?.ToString() == "Archives");
            if (archivesItem is not null) CategoryFilter.SelectedItem = archivesItem;
            RefreshFindingsPage(true);
            BuildDashboard();

            var msg = string.Format(LocalizationService.Get("ArchivesComplete"), foundCount);
            StatusText.Text = msg;
            TextScanStatusText.Text = msg;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = LocalizationService.Get("TextScanCanceled");
            TextScanStatusText.Text = LocalizationService.Get("TextScanCanceled");
        }
        catch (Exception ex)
        {
            CrashLogger.ShowErrorDialog(ex, "Инспекция архивов", this);
            StatusText.Text = ex.Message;
            TextScanStatusText.Text = ex.Message;
        }
        finally
        {
            guard.Dispose();
            _textScanCts?.Dispose();
            _textScanCts = null;
            SetTextScanButtons(true);
        }
    }

    async void DocumentScan_Click(object sender, RoutedEventArgs e)
    {
        if (_documentScanCts is not null) return;
        var guard = TryAcquireHeavyTask(LocalizationService.Get("DocumentScanRunning"));
        if (guard is null) return;

        var images = _sortedMediaFindings.Where(x => File.Exists(x.Path)).ToArray();
        if (images.Length == 0)
        {
            guard.Dispose();
            StatusText.Text = LocalizationService.Get("NoImagesForPeopleScan");
            return;
        }

        _documentScanCts = new CancellationTokenSource();
        var token = _documentScanCts.Token;
        DocumentScanButton.IsEnabled = false;
        PeopleScanButton.IsEnabled = false;
        PeopleScanCancelButton.IsEnabled = true;
        PeopleModelProgress.Visibility = Visibility.Visible;
        PeopleModelProgress.IsIndeterminate = false;
        PeopleModelProgress.Value = 0;
        PeopleScanStageText.Text = LocalizationService.Get("DocumentScanRunning");
        PeopleScanProgressText.Text = string.Format(LocalizationService.Get("DocumentScanProgressFormat"), 0, images.Length);
        StatusText.Text = LocalizationService.Get("DocumentScanRunning");

        int foundCount = 0;
        try
        {
            await Task.Run(async () =>
            {
                for (int i = 0; i < images.Length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var finding = images[i];
                    try
                    {
                        var faceDetected = false;
                        var faceCount = 0;
                        if (PeopleScanMetadata.TryParse(finding.MetadataJson, out var people))
                        {
                            faceDetected = people!.PeopleDetected;
                            faceCount = people.FaceCount;
                        }

                        var docResult = DocumentDetector.Analyze(finding.Path, faceDetected, faceCount);
                        if (docResult.IsDocument)
                        {
                            finding.MetadataJson = DocumentDetectionResult.InjectIntoMetadata(finding.MetadataJson, docResult);
                            Interlocked.Increment(ref foundCount);
                        }
                    }
                    catch (Exception ex)
                    {
                        CrashLogger.LogException(ex, $"DocumentDetector file processing: {finding.Path}");
                    }

                    if ((i + 1) % 3 == 0 || i == images.Length - 1)
                    {
                        var currentIdx = i + 1;
                        await Dispatcher.InvokeAsync(() =>
                        {
                            PeopleModelProgress.Value = (double)currentIdx / images.Length;
                            PeopleScanProgressText.Text = string.Format(LocalizationService.Get("DocumentScanProgressFormat"), currentIdx, images.Length);
                        });
                    }
                }
            }, token);

            SaveCurrentSnapshot();
            if (foundCount > 0)
            {
                var docItem = MediaPeopleFilter.Items.OfType<ComboBoxItem>().FirstOrDefault(x => x.Tag?.ToString() == "Documents");
                if (docItem is not null) MediaPeopleFilter.SelectedItem = docItem;
            }
            UpdatePeoplePresentation();

            var msg = string.Format(LocalizationService.Get("DocumentScanComplete"), foundCount);
            StatusText.Text = msg;
            PeopleScanStageText.Text = msg;
            PeopleScanProgressText.Text = "";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = LocalizationService.Get("TextScanCanceled");
            PeopleScanStageText.Text = LocalizationService.Get("TextScanCanceled");
            PeopleScanProgressText.Text = "";
        }
        catch (Exception ex)
        {
            CrashLogger.ShowErrorDialog(ex, "Поиск фото документов", this);
            StatusText.Text = ex.Message;
            PeopleScanStageText.Text = ex.Message;
        }
        finally
        {
            guard.Dispose();
            _documentScanCts?.Dispose();
            _documentScanCts = null;
            DocumentScanButton.IsEnabled = true;
            PeopleScanButton.IsEnabled = true;
            PeopleScanCancelButton.IsEnabled = false;
            PeopleModelProgress.Visibility = Visibility.Collapsed;
        }
    }

    async void ExifScan_Click(object sender, RoutedEventArgs e)
    {
        if (_exifScanCts is not null) return;
        var guard = TryAcquireHeavyTask(LocalizationService.Get("ExifScanning"));
        if (guard is null) return;

        var candidates = _findings.Where(x => !x.Ignored && File.Exists(x.Path)).ToArray();
        if (candidates.Length == 0)
        {
            guard.Dispose();
            StatusText.Text = LocalizationService.Get("NoImagesForPeopleScan");
            return;
        }

        _exifScanCts = new CancellationTokenSource();
        var token = _exifScanCts.Token;
        ExifScanButton.IsEnabled = false;
        PeopleScanButton.IsEnabled = false;
        DocumentScanButton.IsEnabled = false;
        PeopleScanCancelButton.IsEnabled = true;
        PeopleModelProgress.Visibility = Visibility.Visible;
        PeopleModelProgress.IsIndeterminate = false;
        PeopleModelProgress.Value = 0;
        PeopleScanStageText.Text = LocalizationService.Get("ExifScanning");
        StatusText.Text = LocalizationService.Get("ExifScanning");

        int metadataFound = 0;
        int gpsFound = 0;
        try
        {
            await Task.Run(async () =>
            {
                for (int i = 0; i < candidates.Length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var finding = candidates[i];
                    try
                    {
                        var exifResult = ExifMetadataExtractor.Extract(finding.Path);
                        if (exifResult.DisclosedFields.Count > 0)
                        {
                            finding.MetadataJson = ExifMetadataResult.InjectIntoMetadata(finding.MetadataJson, exifResult);
                            Interlocked.Increment(ref metadataFound);
                            if (exifResult.HasGeolocation) Interlocked.Increment(ref gpsFound);
                        }
                    }
                    catch (Exception ex)
                    {
                        CrashLogger.LogException(ex, $"ExifMetadataExtractor file processing: {finding.Path}");
                    }

                    if ((i + 1) % 5 == 0 || i == candidates.Length - 1)
                    {
                        var currentIdx = i + 1;
                        await Dispatcher.InvokeAsync(() =>
                        {
                            PeopleModelProgress.Value = (double)currentIdx / candidates.Length;
                            PeopleScanProgressText.Text = string.Format(LocalizationService.Get("DocumentScanProgressFormat"), currentIdx, candidates.Length);
                        });
                    }
                }
            }, token);

            SaveCurrentSnapshot();
            if (metadataFound > 0)
            {
                var geoItem = gpsFound > 0
                    ? MediaPeopleFilter.Items.OfType<ComboBoxItem>().FirstOrDefault(x => x.Tag?.ToString() == "GPS")
                    : MediaPeopleFilter.Items.OfType<ComboBoxItem>().FirstOrDefault(x => x.Tag?.ToString() == "GeoExif");
                if (geoItem is not null) MediaPeopleFilter.SelectedItem = geoItem;
            }
            UpdatePeoplePresentation();

            var msg = string.Format(LocalizationService.Get("ExifComplete"), metadataFound, gpsFound);
            StatusText.Text = msg;
            PeopleScanStageText.Text = msg;
            PeopleScanProgressText.Text = "";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = LocalizationService.Get("TextScanCanceled");
            PeopleScanStageText.Text = LocalizationService.Get("TextScanCanceled");
            PeopleScanProgressText.Text = "";
        }
        catch (Exception ex)
        {
            CrashLogger.ShowErrorDialog(ex, "Поиск метаданных и геолокации", this);
            StatusText.Text = ex.Message;
            PeopleScanStageText.Text = ex.Message;
        }
        finally
        {
            guard.Dispose();
            _exifScanCts?.Dispose();
            _exifScanCts = null;
            ExifScanButton.IsEnabled = true;
            PeopleScanButton.IsEnabled = true;
            DocumentScanButton.IsEnabled = true;
            PeopleScanCancelButton.IsEnabled = false;
            PeopleModelProgress.Visibility = Visibility.Collapsed;
        }
    }

    void MediaFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized) return;
        UpdatePeoplePresentation();
    }
    void MediaTileList_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            var delta = e.Delta > 0 ? 20 : -20;
            MediaTileZoom.Value = Math.Clamp(MediaTileZoom.Value + delta, MediaTileZoom.Minimum, MediaTileZoom.Maximum);
            return;
        }
        var deltaRows = e.Delta > 0 ? -1 : 1;
        ScrollMediaTilesByRow(deltaRows);
        e.Handled = true;
    }
    void ScrollMediaTilesByRow(int rowDelta)
    {
        if (VisualTreeHelper.GetChildrenCount(MediaTileList) == 0) return;
        var border = VisualTreeHelper.GetChild(MediaTileList, 0) as Decorator;
        if (border?.Child is not ScrollViewer sv) return;
        var targetOffset = Math.Max(0, sv.VerticalOffset + (rowDelta * (MediaTileZoom.Value + 20)));
        sv.ScrollToVerticalOffset(targetOffset);
    }
    void MediaTileList_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0 && e.ExtentHeightChange == 0) return;
        if (_loadingMediaBatch) return;
        var sv = e.OriginalSource as ScrollViewer;
        if (sv is null) return;
        if (sv.VerticalOffset + sv.ViewportHeight >= sv.ExtentHeight - 40 && e.VerticalChange > 0)
        {
            AppendNextMediaBatch();
        }
        else if (sv.VerticalOffset <= 40 && e.VerticalChange < 0 && _firstLoadedMediaPage > 0)
        {
            PrependPreviousMediaBatch();
        }
    }
    void AppendNextMediaBatch()
    {
        if (_sortedMediaFindings.Length == 0 || _loadingMediaBatch) return;
        var pageSize = FindingPagination.TilePageSize(MediaTileZoom.Value);
        if ((_lastLoadedMediaPage + 1) * pageSize >= _sortedMediaFindings.Length) return;
        _loadingMediaBatch = true;
        try
        {
            var page = FindingPagination.Slice(_sortedMediaFindings, _lastLoadedMediaPage + 1, pageSize);
            _lastLoadedMediaPage = page.PageIndex;
            _visibleMediaFindings.AddRange(page.Items);
            _loadedMediaBatches.Enqueue(page.Items.Count);
            while (_loadedMediaBatches.Count > 3)
            {
                var remove = _loadedMediaBatches.Dequeue();
                _visibleMediaFindings.RemoveRange(0, remove);
                _firstLoadedMediaPage++;
            }
            MediaCountText.Text = string.Format(LocalizationService.Get("MediaCount"), Math.Min((_lastLoadedMediaPage + 1) * pageSize, page.TotalCount), page.TotalCount);
        }
        finally { _loadingMediaBatch = false; }
    }
    void PrependPreviousMediaBatch()
    {
        if (_sortedMediaFindings.Length == 0 || _loadingMediaBatch || _firstLoadedMediaPage <= 0) return;
        _loadingMediaBatch = true;
        try
        {
            var pageSize = FindingPagination.TilePageSize(MediaTileZoom.Value);
            var page = FindingPagination.Slice(_sortedMediaFindings, _firstLoadedMediaPage - 1, pageSize);
            _firstLoadedMediaPage = page.PageIndex;
            _visibleMediaFindings.InsertRange(0, page.Items);
            var existing = _loadedMediaBatches.ToArray();
            _loadedMediaBatches.Clear();
            _loadedMediaBatches.Enqueue(page.Items.Count);
            foreach (var count in existing) _loadedMediaBatches.Enqueue(count);
            while (_loadedMediaBatches.Count > 3)
            {
                var counts = _loadedMediaBatches.ToArray();
                var remove = counts[^1];
                _loadedMediaBatches.Clear();
                foreach (var count in counts[..^1]) _loadedMediaBatches.Enqueue(count);
                _visibleMediaFindings.RemoveRange(Math.Max(0, _visibleMediaFindings.Count - remove), remove);
                _lastLoadedMediaPage--;
            }
            MediaCountText.Text = string.Format(LocalizationService.Get("MediaCount"), Math.Min((_lastLoadedMediaPage + 1) * pageSize, page.TotalCount), page.TotalCount);
        }
        finally { _loadingMediaBatch = false; }
    }
    void MediaTileZoom_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MediaTileZoomLabel is not null) MediaTileZoomLabel.Text = $"{e.NewValue:0} px";
        if (IsInitialized) UpdatePeoplePresentation();
    }
    void MediaTile_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (MediaTileList.SelectedItem is Finding finding) SelectFinding(finding);
    }
    void MediaTile_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (MediaTileList.SelectedItem is not Finding finding) return;
        SelectFindingAndShowDetails(finding, DetailsReturnSource.MediaTiles);
        e.Handled = true;
    }
    void FindingsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var element = e.OriginalSource as DependencyObject;
        while (element is not null && element is not DataGridRow) element = VisualTreeHelper.GetParent(element);
        if (element is DataGridRow { DataContext: Finding finding })
        {
            SelectFindingAndShowDetails(finding, DetailsReturnSource.FindingsGrid);
            e.Handled = true;
        }
    }
    void FindingsTile_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindingsTileList.SelectedItem is Finding finding)
        {
            SelectFindingAndShowDetails(finding, DetailsReturnSource.FindingsTiles);
            e.Handled = true;
        }
    }
    void SelectFindingAndShowDetails(Finding f, DetailsReturnSource source)
    {
        var sourceControl = source switch
        {
            DetailsReturnSource.FindingsGrid => (DependencyObject)FindingsGrid,
            DetailsReturnSource.FindingsTiles => FindingsTileList,
            DetailsReturnSource.MediaTiles => MediaTileList,
            _ => ApplicationHistoryList
        };
        var scrollViewer = FindVisualChild<ScrollViewer>(sourceControl);
        _detailsReturnState = new DetailsReturnState(source, f, scrollViewer?.VerticalOffset ?? 0, scrollViewer?.HorizontalOffset ?? 0);
        SelectFinding(f);
        DetailsBackButton.Visibility = Visibility.Visible;
        TabDetails.IsSelected = true;
    }
    void DetailsBack_Click(object sender, RoutedEventArgs e) => ReturnFromDetails();
    void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if ((e.ChangedButton != MouseButton.XButton1 && e.ChangedButton != MouseButton.XButton2)
            || !TabDetails.IsSelected || _detailsReturnState is null) return;
        e.Handled = true;
        ReturnFromDetails();
    }
    void ReturnFromDetails()
    {
        var state = _detailsReturnState;
        if (state is null) return;

        _detailsReturnState = null;
        DetailsBackButton.Visibility = Visibility.Collapsed;
        var targetTab = state.Source switch { DetailsReturnSource.MediaTiles => TabMedia, DetailsReturnSource.ApplicationHistory => TabApplicationHistory, _ => TabFindings };
        targetTab.IsSelected = true;

        // Wait until the source tab has been laid out again; otherwise WPF can clamp the
        // offset against the Details tab's old visual tree.
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() =>
        {
            var target = state.Source switch
            {
                DetailsReturnSource.FindingsGrid => (DependencyObject)FindingsGrid,
                DetailsReturnSource.FindingsTiles => FindingsTileList,
                DetailsReturnSource.MediaTiles => MediaTileList,
                _ => ApplicationHistoryList
            };
            switch (state.Source)
            {
                case DetailsReturnSource.FindingsGrid:
                    FindingsGrid.SelectedItem = state.Finding;
                    break;
                case DetailsReturnSource.FindingsTiles:
                    FindingsTileList.SelectedItem = state.Finding;
                    break;
                case DetailsReturnSource.MediaTiles:
                    MediaTileList.SelectedItem = state.Finding;
                    break;
                case DetailsReturnSource.ApplicationHistory:
                    break;
            }
            var scrollViewer = FindVisualChild<ScrollViewer>(target);
            scrollViewer?.ScrollToHorizontalOffset(state.HorizontalOffset);
            scrollViewer?.ScrollToVerticalOffset(state.VerticalOffset);
        }));
    }
    static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T found) return found;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (FindVisualChild<T>(child) is { } result) return result;
        }
        return null;
    }
    void Finding_Selected(object sender, SelectionChangedEventArgs e) { if (FindingsGrid.SelectedItem is Finding f) SelectFinding(f); }
    void TileFinding_Selected(object sender, SelectionChangedEventArgs e) { if (FindingsTileList.SelectedItem is Finding f) SelectFinding(f); }
    void SelectFinding(Finding f, bool keepSimilarSection = false)
    {
        UpdateProvenancePanel(f);
        _selected = f;
        OpenDetailsFileButton.Visibility = IsDirectoryFinding(f) ? Visibility.Collapsed : Visibility.Visible;
        var piiDetails = PiiDetectionResult.TryParse(f.MetadataJson, out var pii) && pii!.TotalMatches > 0
            ? $"\n\n{string.Format(LocalizationService.Get("PiiDetails"), pii.TotalMatches, string.Join(", ", pii.Categories), string.Join("\n• ", pii.Matches.Take(10).Select(m => $"{m.Category}: {m.Sample}")))}"
            : "";
        var secDetails = SecretDetectionResult.TryParse(f.MetadataJson, out var sec) && sec!.TotalMatches > 0
            ? $"\n\n{string.Format(LocalizationService.Get("SecretsDetails"), sec.TotalMatches, string.Join(", ", sec.Categories), string.Join("\n• ", sec.Matches.Take(10).Select(m => $"{m.Category}: {m.Sample}")))}"
            : "";
        var docDetails = DocumentDetectionResult.TryParse(f.MetadataJson, out var doc) && doc!.IsDocument
            ? $"\n\n{string.Format(LocalizationService.Get("DocumentDetails"), LocalizationService.Get(doc.IsIdentityDocument ? "IdDocumentDetected" : "DocumentDetected"), doc.Confidence, doc.AspectRatio, doc.TextDensity, string.Join("\n• ", doc.Reasons))}"
            : "";
        var peopleDetails = PeopleScanMetadata.TryParse(f.MetadataJson, out var people)
            ? $"\n\n{string.Format(LocalizationService.Get("PeopleDetails"), people!.Status == PeopleScanStatus.Completed && people.PeopleDetected ? LocalizationService.Get("PeopleDetected") : people.Status == PeopleScanStatus.Error ? LocalizationService.Get("PeopleScanErrors") : LocalizationService.Get("NoPeopleDetected"), people.FaceCount, people.MaxConfidence, people.ModelVersion, people.ScannedAtUtc.ToLocalTime())}"
            : "";
        var exifDetails = ExifMetadataResult.TryParse(f.MetadataJson, out var exif) && exif!.DisclosedFields.Count > 0
            ? $"\n\n{string.Format(LocalizationService.Get("ExifDetails"), string.Join(", ", exif.DisclosedFields), exif.ExposureLevel, exif.HasGeolocation ? $"{exif.Latitude:F6}, {exif.Longitude:F6} (Alt: {exif.Altitude}m)" : "—", exif.CameraModel ?? "—", exif.CameraSerialNumber ?? "—", exif.Software ?? "—", exif.Author ?? exif.LastSavedBy ?? "—", exif.DateTaken ?? "—")}"
            : "";
        var configDetails = CredentialConfigResult.TryParse(f.MetadataJson, out var cfg) && cfg!.IsCredentialConfig
            ? $"\n\n{string.Format(LocalizationService.Get("ConfigsDetails"), cfg.ConfigType, cfg.ExposureLevel, string.Join("\n• ", cfg.ExposedParameters.DefaultIfEmpty("—")), string.Join("\n• ", cfg.Endpoints.DefaultIfEmpty("—")))}"
            : "";
        var identityDetails = IdentityTraceResult.TryParse(f.MetadataJson, out var idt) && idt!.HasIdentityTrace
            ? $"\n\n{string.Format(LocalizationService.Get("IdentityDetails"), idt.TotalMentions, string.Join("\n• ", idt.MatchedTerms.Select(kv => $"{kv.Key}: {kv.Value}")))}"
            : "";
        var archiveDetails = ArchiveInspectionResult.TryParse(f.MetadataJson, out var arch) && arch!.IsArchive
            ? $"\n\n{string.Format(LocalizationService.Get("ArchivesDetails"), arch.PrivacyScore, arch.SensitiveEntriesCount, arch.TotalEntries, arch.TreeView)}"
            : "";
        var applicationHistoryDetails = string.IsNullOrWhiteSpace(f.ApplicationHistoryReferences)
            ? ""
            : $"\n\n{string.Format(LocalizationService.Get("ApplicationHistoryFindingDetails"), f.ApplicationHistoryReferences, f.ApplicationHistoryLastSeen?.ToString("g") ?? "—", f.ApplicationHistoryInteractionCount > 0 ? f.ApplicationHistoryInteractionCount.ToString("N0") : "—")}";

        var personalDetails = f.PersonalAttentionScore is float personalScore
            ? $"\n\n{LocalizationService.Get("PersonalModel")}:\n{personalScore:0}% — {LocalizationService.Get(personalScore >= 70 ? "LikelyInteresting" : personalScore <= 30 ? "LikelyNotInteresting" : "PersonalUncertain")}" +
              (PersonalAttentionFeatureExtractor.Explain(f) is { Count: > 0 } factors ? $"\n{LocalizationService.Get("Why")}:\n• {string.Join("\n• ", factors)}" : "")
            : $"\n\n{LocalizationService.Get("PersonalModelNotTrained")}";
        DetailsText.Text = $"{f.DisplayName}\n\nFull path:\n{f.Path}\n\nSize: {f.SizeDisplay}\nCreated: {f.CreatedAt}\nModified: {f.ModifiedAt}\nLast access: {f.LastAccessAt}\n\nExposure: {f.ExposureScore} / 100 ({f.RiskLevel})\nReasons:\n• {string.Join("\n• ", f.ExposureReasons)}\n\nCategory: {f.Category}\nSubcategory: {f.Subcategory}\nAge: {f.AgeClass}\nScanner: {f.ScannerId}{personalDetails}{piiDetails}{secDetails}{docDetails}{peopleDetails}{exifDetails}{configDetails}{identityDetails}{archiveDetails}{applicationHistoryDetails}";

        // Media Preview on Details tab
        if (string.Equals(Classifier.File(f.Path), "Images", StringComparison.OrdinalIgnoreCase) && File.Exists(f.Path))
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.DecodePixelWidth = 480;
                bi.UriSource = new Uri(f.Path, UriKind.Absolute);
                bi.EndInit();
                bi.Freeze();
                DetailsImagePreview.Source = bi;
                DetailsImageDimensions.Text = $"{bi.PixelWidth} × {bi.PixelHeight} px";
                DetailsImageFormat.Text = $"{Path.GetExtension(f.Path).ToUpperInvariant().TrimStart('.')} • {f.SizeDisplay}";
                DetailsImagePreviewBorder.Visibility = Visibility.Visible;
            }
            catch
            {
                DetailsImagePreview.Source = null;
                DetailsImagePreviewBorder.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            DetailsImagePreview.Source = null;
            DetailsImagePreviewBorder.Visibility = Visibility.Collapsed;
        }

        if (string.Equals(Classifier.File(f.Path), "Images", StringComparison.OrdinalIgnoreCase))
        {
            FindSimilarButton.Content = LocalizationService.Get("FindSimilarImages");
            FindSimilarButton.Visibility = Visibility.Visible;
        }
        else if (TextExtractor.IsSupported(f.Path))
        {
            FindSimilarButton.Content = LocalizationService.Get("FindSimilarDocuments");
            FindSimilarButton.Visibility = Visibility.Visible;
        }
        else
        {
            FindSimilarButton.Visibility = Visibility.Collapsed;
        }

        if (!keepSimilarSection)
        {
            SimilarSectionBorder.Visibility = Visibility.Collapsed;
            SimilarFindingsList.ItemsSource = null;
        }
    }

    void UpdateProvenancePanel(Finding finding)
    {
        _provenanceResult = _db.GetProvenance(finding);
        ProvenanceWhyButton.Visibility = _provenanceResult is null ? Visibility.Collapsed : Visibility.Visible;
        ProvenanceAnalyzeButton.Visibility = Visibility.Visible;
        ProvenanceAnalyzeButton.Content = _provenanceResult is null ? LocalizationService.Get("InvestigateProvenance") : LocalizationService.Get("InvestigateAgain");
        if (_provenanceResult is null) { ProvenanceDetailsPanel.Visibility = Visibility.Collapsed; ProvenanceDetailsText.Text = ""; return; }
        var current = _provenanceResult.IsCurrent(finding);
        ProvenanceDetailsText.Text = BuildProvenanceText(_provenanceResult, current ? null : LocalizationService.Get("ProvenanceStale"));
        ProvenanceDetailsPanel.Visibility = Visibility.Visible;
    }

    string BuildProvenanceText(FileProvenanceResult result, string? warning)
    {
        var app = string.IsNullOrWhiteSpace(result.ApplicationName) ? LocalizationService.Get("ProvenanceUnknown") : result.ApplicationName;
        var evidence = result.Evidence.Count == 0 ? LocalizationService.Get("ProvenanceUnknown") : string.Format(LocalizationService.Get("ProvenanceEvidence"), string.Join("\n• ", result.Evidence.Take(5).Select(x => x.Description)));
        var text = string.Format(LocalizationService.Get("ProvenanceSaved"), app, result.ApplicationStatus, result.FileRole, result.ConfidenceLevel, result.ConfidenceScore, result.DetectedFormat, evidence);
        var flags = string.Join(", ", new[] { result.PossibleOrphan ? "possible orphan" : null, result.PossibleCache ? "cache" : null, result.PossibleUserData ? "user data" : null }.Where(x => x is not null));
        text += $"\n{LocalizationService.Get("ProvenancePublisher")}: {result.Publisher ?? "—"}\n{LocalizationService.Get("ProvenanceExecutable")}: {result.ExecutablePath ?? "—"}\n{LocalizationService.Get("ProvenanceFlags")}: {(string.IsNullOrWhiteSpace(flags) ? "—" : flags)}";
        if (result.SchemaHints.Count > 0) text += $"\n{LocalizationService.Get("ProvenanceSchema")}: {string.Join(", ", result.SchemaHints)}";
        if (result.Neighbors.Count > 0) text += $"\n{LocalizationService.Get("ProvenanceNeighbors")}: {string.Join(", ", result.Neighbors.Take(12))}";
        return warning is null ? text : $"⚠ {warning}\n\n{text}";
    }

    async void AnalyzeProvenance_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _provenanceCts is not null) return;
        var finding = _selected; var cached = _db.GetProvenance(finding);
        if (cached is not null && cached.IsCurrent(finding)) { _provenanceResult = cached; UpdateProvenancePanel(finding); return; }
        _provenanceCts = new CancellationTokenSource(); ProvenanceCancelButton.Visibility = Visibility.Visible; ProvenanceAnalyzeButton.IsEnabled = false; ProvenanceWhyButton.Visibility = Visibility.Collapsed;
        try
        {
            ProvenanceDetailsPanel.Visibility = Visibility.Visible;
            var progress = new Progress<string>(stage => ProvenanceDetailsText.Text = string.Format(LocalizationService.Get("ProvenanceRunning"), stage));
            var result = await new FileProvenanceAnalyzer().AnalyzeAsync(finding, _provenanceCts.Token, progress);
            _db.SaveProvenance(result); _provenanceResult = result; UpdateProvenancePanel(finding);
        }
        catch (OperationCanceledException) { UpdateProvenancePanel(finding); }
        catch (Exception ex) { ProvenanceDetailsPanel.Visibility = Visibility.Visible; ProvenanceDetailsText.Text = ex.Message; CrashLogger.LogException(ex, "AnalyzeProvenance_Click"); }
        finally { _provenanceCts.Dispose(); _provenanceCts = null; ProvenanceCancelButton.Visibility = Visibility.Collapsed; ProvenanceAnalyzeButton.IsEnabled = true; }
    }
    void CancelProvenance_Click(object sender, RoutedEventArgs e) => _provenanceCts?.Cancel();
    void ShowProvenanceWhy_Click(object sender, RoutedEventArgs e)
    {
        if (_provenanceResult is null) return;
        System.Windows.MessageBox.Show(string.Format(LocalizationService.Get("ProvenanceEvidence"), string.Join("\n• ", _provenanceResult.Evidence.Select(x => $"{x.Weight:+#;-#;0} — {x.Description} [{x.Source}]"))), LocalizationService.Get("ProvenanceWhy"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    async void FindSimilar_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var guard = TryAcquireHeavyTask(LocalizationService.Get("CalculatingSimilarity"));
        if (guard is null) return;

        _similarCts?.Cancel();
        _similarCts = new CancellationTokenSource();
        var token = _similarCts.Token;

        var target = _selected;
        SimilarSectionBorder.Visibility = Visibility.Visible;
        SimilarProgressContainer.Visibility = Visibility.Visible;
        SimilarProgress.Value = 0;
        SimilarProgressText.Text = LocalizationService.Get("CalculatingSimilarity");
        SimilarEmptyText.Visibility = Visibility.Collapsed;
        SimilarFindingsList.ItemsSource = null;
        SimilarSectionTitle.Text = LocalizationService.Get("CalculatingSimilarity");

        try
        {
            var isImage = string.Equals(Classifier.File(target.Path), "Images", StringComparison.OrdinalIgnoreCase);
            var progress = new Progress<(int current, int total)>(p =>
            {
                SimilarProgress.Value = p.total > 0 ? (double)p.current / p.total : 0;
                var formatKey = isImage ? "DocumentScanProgressFormat" : "TextScanProgressFormat";
                SimilarProgressText.Text = string.Format(LocalizationService.Get(formatKey), p.current, p.total);
            });

            var matches = await Task.Run(() =>
            {
                if (isImage)
                {
                    return ImageSimilarity.FindSimilar(target, _findings, token, progress);
                }
                return DocumentSimilarity.FindSimilar(target, _findings, token, progress);
            }, token);

            SimilarProgressContainer.Visibility = Visibility.Collapsed;
            SimilarSectionTitle.Text = string.Format(LocalizationService.Get("SimilarFindingsTitle"), matches.Count);
            if (matches.Count == 0)
            {
                SimilarEmptyText.Visibility = Visibility.Visible;
            }
            else
            {
                SimilarFindingsList.ItemsSource = matches;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            CrashLogger.LogException(ex, "FindSimilar_Click");
            SimilarProgressContainer.Visibility = Visibility.Collapsed;
            SimilarEmptyText.Text = ex.Message;
            SimilarEmptyText.Visibility = Visibility.Visible;
        }
        finally
        {
            guard.Dispose();
            _similarCts?.Dispose();
            _similarCts = null;
        }
    }

    void CloseSimilarSection_Click(object sender, RoutedEventArgs e)
    {
        RequestCancellation(_similarCts);
        SimilarSectionBorder.Visibility = Visibility.Collapsed;
    }

    void SimilarItem_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (SimilarFindingsList.SelectedItem is SimilarityMatch match)
        {
            SelectFinding(match.Finding, keepSimilarSection: true);
        }
    }

    void SimilarItem_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SimilarFindingsList.SelectedItem is SimilarityMatch match)
        {
            SelectFinding(match.Finding, keepSimilarSection: true);
            e.Handled = true;
        }
    }

    void Open_Click(object s, RoutedEventArgs e) { if (_selected is not null) OpenMediaFile(_selected.Path); }
    void OpenMediaFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string path }) OpenMediaFile(path);
        e.Handled = true;
    }
    void OpenMediaFile(string path)
    {
        if (Directory.Exists(path)) { OpenFolderInExplorer(path); return; }
        if (!File.Exists(path)) { StatusText.Text = LocalizationService.Get("FileMissing"); return; }
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { StatusText.Text = string.Format(LocalizationService.Get("OpenFileFailed"), ex.Message); }
    }
    static bool IsDirectoryFinding(Finding finding) => finding.IsDirectory || Directory.Exists(finding.Path);
    void OpenFolderInExplorer(string path)
    {
        if (!Directory.Exists(path)) { StatusText.Text = LocalizationService.Get("FileMissing"); return; }
        Process.Start("explorer.exe", $"\"{path}\"");
    }
    void Folder_Click(object s, RoutedEventArgs e) { if (_selected is null) return; if (IsDirectoryFinding(_selected)) OpenFolderInExplorer(_selected.Path); else ShowFindingInFolder(_selected); }
    static ItemsControl? FindFindingItemsControl(DependencyObject element)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is DataGrid or System.Windows.Controls.ListBox) return (ItemsControl)current;
        }
        return null;
    }

    static Finding[] GetSelectedFindings(ItemsControl? owner) => owner switch
    {
        DataGrid grid => grid.SelectedItems.OfType<Finding>().ToArray(),
        System.Windows.Controls.ListBox list => list.SelectedItems.OfType<Finding>().ToArray(),
        _ => []
    };

    static void ClearFindingSelection(ItemsControl? owner)
    {
        switch (owner)
        {
            case DataGrid grid: grid.UnselectAll(); break;
            case System.Windows.Controls.ListBox list: list.UnselectAll(); break;
        }
    }

    static bool ContainsFinding(IEnumerable<Finding> findings, Finding target) => findings.Any(x => x.Id == target.Id);

    void Finding_RightClick(object sender, MouseButtonEventArgs e)
    {
        var element = e.OriginalSource as DependencyObject;
        while (element is not null && element is not DataGridRow && element is not ListBoxItem) element = VisualTreeHelper.GetParent(element);
        if (element is not FrameworkElement { DataContext: Finding finding } target) return;
        var owner = FindFindingItemsControl(target);
        var selected = GetSelectedFindings(owner).ToList();
        if (!ContainsFinding(selected, finding))
        {
            ClearFindingSelection(owner);
            if (target is DataGridRow row) row.IsSelected = true;
            if (target is ListBoxItem tile) tile.IsSelected = true;
            selected = [finding];
        }
        SelectFinding(finding);
        var menu = new ContextMenu { PlacementTarget = target, Focusable = false, HasDropShadow = true, Padding = new Thickness(5) };
        var folder = CreateContextMenuItem("ShowExplorer", "folder"); folder.Click += (_, _) => ShowFindingInFolder(finding);
        if (IsDirectoryFinding(finding))
        {
            menu.Items.Add(folder);
            menu.IsOpen = true;
            e.Handled = true;
            return;
        }
        var open = CreateContextMenuItem("OpenFile", "open"); open.Click += (_, _) => OpenFinding(finding);
        var delete = new MenuItem
        {
            Header = selected.Count > 1 ? string.Format(LocalizationService.Get("DeleteSelectedFiles"), selected.Count) : LocalizationService.Get("DeleteFile"),
            Tag = "delete", FocusVisualStyle = null, IsTabStop = false, BorderThickness = new Thickness(0), Padding = new Thickness(14, 9, 14, 9),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(23, 29, 40)),
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 105, 97))
        };
        delete.Click += (_, _) => DeleteFindings(selected);
        menu.Items.Add(open); menu.Items.Add(folder); menu.Items.Add(new Separator()); menu.Items.Add(delete);
        menu.IsOpen = true;
        e.Handled = true;
    }
    static MenuItem CreateContextMenuItem(string textKey, string tag) => new()
    {
        Header = LocalizationService.Get(textKey), Tag = tag, Icon = null, InputGestureText = "",
        FocusVisualStyle = null, IsTabStop = false, BorderThickness = new Thickness(0), Padding = new Thickness(14, 9, 14, 9),
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(23, 29, 40))
    };
    void OpenFinding(Finding finding)
    {
        if (IsDirectoryFinding(finding)) { OpenFolderInExplorer(finding.Path); return; }
        if (!File.Exists(finding.Path)) { StatusText.Text = LocalizationService.Get("FileMissing"); return; }
        Process.Start(new ProcessStartInfo(finding.Path) { UseShellExecute = true });
    }
    void ShowFindingInFolder(Finding finding)
    {
        if (IsDirectoryFinding(finding)) { OpenFolderInExplorer(finding.Path); return; }
        if (!File.Exists(finding.Path)) { StatusText.Text = LocalizationService.Get("FileMissing"); return; }
        Process.Start("explorer.exe", $"/select,\"{finding.Path}\"");
    }
    void DeleteFinding(Finding finding) => DeleteFindings([finding]);

    void DeleteFindings(IReadOnlyCollection<Finding> selected)
    {
        if (selected.Count == 0) return;
        if (selected.Any(IsDirectoryFinding)) return;
        var paths = selected
            .GroupBy(x => PersonalAttentionFeatureExtractor.PathKey(x.Path), StringComparer.OrdinalIgnoreCase)
            .Select(x => (Key: x.Key, Path: x.First().Path))
            .ToArray();
        var prompt = paths.Length == 1
            ? string.Format(LocalizationService.Get("DeleteFilePrompt"), paths[0].Path)
            : string.Format(LocalizationService.Get("DeleteSelectedFilesPrompt"), paths.Length);
        if (System.Windows.MessageBox.Show(prompt, "NotBad Privacy Detector Agent", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;

        var removedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failures = new List<string>();
        try
        {
            foreach (var item in paths)
            {
                try
                {
                    if (File.Exists(item.Path))
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(item.Path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    }
                    removedKeys.Add(item.Key);
                }
                catch (Exception ex) { failures.Add($"{item.Path}: {ex.Message}"); }
            }

            if (removedKeys.Count == 0)
            {
                StatusText.Text = string.Format(LocalizationService.Get("DeleteFileFailed"), failures.FirstOrDefault() ?? LocalizationService.Get("FileMissing"));
                return;
            }

            _findings.RemoveAll(x => removedKeys.Contains(PersonalAttentionFeatureExtractor.PathKey(x.Path)));
            _mediaFindings.RemoveAll(x => removedKeys.Contains(PersonalAttentionFeatureExtractor.PathKey(x.Path)));
            if (_selected is not null && removedKeys.Contains(PersonalAttentionFeatureExtractor.PathKey(_selected.Path)))
            {
                _selected = null;
                DetailsText.Clear();
            }
            FindingsGrid.UnselectAll();
            FindingsTileList.UnselectAll();
            MediaTileList.UnselectAll();
            SaveCurrentSnapshot();
            RefreshFindingsPage(true);
            BuildDashboard();
            UpdatePeoplePresentation();
            StatusText.Text = failures.Count == 0
                ? (paths.Length == 1 ? LocalizationService.Get("FileDeleted") : LocalizationService.Get("FilesDeleted"))
                : string.Format(LocalizationService.Get("DeleteSelectedFilesFailed"), failures.Count);
        }
        catch (Exception ex) { StatusText.Text = string.Format(LocalizationService.Get("DeleteFileFailed"), ex.Message); }
    }
    void SaveCurrentSnapshot()
    {
        try { SnapshotStore.Save(_snapshotPath, DateTime.UtcNow, _findings); } catch { }
    }
    void ApplyPersonalState()
    {
        var feedback = _db.GetPersonalFeedback(PersonalAttentionSchema.Version)
            .GroupBy(x => x.PathKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(v => v.UpdatedAt).First(), StringComparer.OrdinalIgnoreCase);
        var scores = _personalModel.PredictMany(_findings);
        for (var i = 0; i < _findings.Count; i++)
        {
            var finding = _findings[i];
            feedback.TryGetValue(PersonalAttentionFeatureExtractor.PathKey(finding.Path), out var rating);
            finding.PersonalAttentionLabel = rating?.Label;
            finding.PersonalAttentionScore = scores[i];
        }
        UpdatePersonalModelStats();
    }

    void UpdatePersonalModelStats()
    {
        if (PersonalModelStatsText is null) return;
        var stats = _db.GetPersonalModelStats(_personalModel.Metadata?.TrainedSamples ?? 0);
        PersonalModelStatsText.Text = string.Format(LocalizationService.Get("PersonalModelStats"), stats.Total, stats.Positive, stats.Negative) +
            (stats.CanTrain ? (_personalModel.IsReady ? "" : $"\n{LocalizationService.Get("PersonalModelReadyToTrain")}") : $"\n{LocalizationService.Get("PersonalModelNotTrained")}");
        PersonalRetrainButton.IsEnabled = stats.CanTrain && _personalTrainingCts is null;
    }

    async void PersonalFeedback_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { DataContext: Finding finding } button) return;
        bool? label = button.Tag?.ToString() switch { "True" => true, "False" => false, _ => null };
        _db.SetPersonalFeedback(finding, label); finding.PersonalAttentionLabel = label; UpdatePersonalModelStats();
        var stats = _db.GetPersonalModelStats(_personalModel.Metadata?.TrainedSamples ?? 0);
        if (stats.CanTrain && (!_personalModel.IsReady || stats.Total - stats.TrainedSamples >= PersonalAttentionSchema.RetrainInterval))
            await TrainPersonalModelAsync();
        e.Handled = true;
    }

    async void PersonalRetrain_Click(object sender, RoutedEventArgs e) => await TrainPersonalModelAsync();
    void ShowPersonalModelInfo_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PersonalModelInfoDialog();
        dialog.Completed += (_, _) => HideModal();
        ShowModal(dialog, LocalizationService.Get("PersonalModelInfoTitle"));
    }

    async Task TrainPersonalModelAsync()
    {
        if (_personalTrainingCts is not null) return;
        var records = _db.GetPersonalFeedback(PersonalAttentionSchema.Version);
        var current = PersonalAttentionFeatureExtractor.IndexFindingsByPath(_findings);
        var samples = records.Select(record =>
        {
            var stored = PersonalAttentionFeatureExtractor.Deserialize(record.FeatureJson);
            if (stored is not null) { stored.Label = record.Label; return stored; }
            return current.TryGetValue(record.PathKey, out var finding) ? PersonalAttentionFeatureExtractor.Extract(finding, record.Label) : null;
        }).Where(x => x is not null).Cast<PersonalAttentionFeatures>().ToArray();
        var positive = samples.Count(x => x.Label); var stats = new PersonalModelStats(samples.Length, positive, samples.Length - positive);
        if (!stats.CanTrain) { UpdatePersonalModelStats(); return; }
        _personalTrainingCts = new(); PersonalRetrainButton.IsEnabled = false; PersonalCancelButton.Visibility = Visibility.Visible;
        PersonalModelStatsText.Text = LocalizationService.Get("PersonalModelTraining");
        try
        {
            await _personalModel.TrainAsync(samples, _personalTrainingCts.Token);
            var scoreSnapshot = _findings.ToArray();
            var scores = await _personalModel.PredictManyAsync(scoreSnapshot, _personalTrainingCts.Token);
            for (var i = 0; i < scoreSnapshot.Length; i++) scoreSnapshot[i].PersonalAttentionScore = scores[i];
            await ApplyApplicationHistoryPersonalStateAsync(_personalTrainingCts.Token);
            RefreshFindingsPage(); if (_selected is not null) SelectFinding(_selected); StatusText.Text = LocalizationService.Get("PersonalModelTrained");
        }
        catch (OperationCanceledException) { StatusText.Text = LocalizationService.Get("PersonalTrainingCancelled"); }
        catch (Exception ex) { CrashLogger.LogException(ex, "PersonalAttentionTraining"); StatusText.Text = string.Format(LocalizationService.Get("PersonalTrainingFailed"), ex.Message); }
        finally { _personalTrainingCts.Dispose(); _personalTrainingCts = null; PersonalCancelButton.Visibility = Visibility.Collapsed; ClearCancellationProgress(); UpdatePersonalModelStats(); }
    }

    void PersonalCancel_Click(object sender, RoutedEventArgs e) => RequestCancellation(_personalTrainingCts);
    void DeletePersonalAll_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.MessageBox.Show(LocalizationService.Get("DeletePersonalAllPrompt"), LocalizationService.Get("AppTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _personalModel.DeleteModel(); _db.DeletePersonalFeedback(); foreach (var finding in _findings) { finding.PersonalAttentionLabel = null; finding.PersonalAttentionScore = null; } foreach (var entry in _applicationHistoryApplications.SelectMany(x => x.Entries)) { entry.PersonalAttentionLabel = null; entry.PersonalAttentionScore = null; } UpdatePersonalModelStats(); RefreshFindingsPage(); RefreshApplicationHistoryFilter();
    }
    void Copy_Click(object s, RoutedEventArgs e) { if (_selected is not null) System.Windows.Clipboard.SetText(_selected.Path); }
    void Ignore_Click(object s, RoutedEventArgs e) { if (_selected is null) return; _selected.Ignored = true; SaveCurrentSnapshot(); RefreshFindingsPage(); BuildDashboard(); }
    void Exclude_Click(object s, RoutedEventArgs e) { if (_selected is null) return; _db.AddExclusion(_selected.Path); _selected.Ignored = true; SaveCurrentSnapshot(); RefreshFindingsPage(); StatusText.Text = LocalizationService.Get("Excluded"); }
    void DeleteDb_Click(object s, RoutedEventArgs e) { if (System.Windows.MessageBox.Show(LocalizationService.Get("DeletePrompt"), "NotBad Privacy Detector Agent", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes) { _db.DeleteDatabase(); _db.DeleteProvenance(); _peopleRepository.DeleteAll(); StatusText.Text = LocalizationService.Get("DatabaseDeleted"); } }

    public bool CanRunCleanup => _activeHeavyTaskName is null && _cts is null && _personalTrainingCts is null &&
        _textScanCts is null && _documentScanCts is null && _similarCts is null && _exifScanCts is null;

    public void ClearCachesAndAuditResults()
    {
        if (!CanRunCleanup) throw new InvalidOperationException(LocalizationService.Get("CleanupBusy"));
        _pageLoadCts?.Cancel(); _mediaFilterCts?.Cancel();
        _cleanupService.ClearCachesAndAuditResults();
        _findings.Clear(); _visibleFindings.Clear(); _mediaFindings.Clear(); _visibleMediaFindings.Clear();
        _selected = null; DetailsText.Clear(); DashboardPanel.Children.Clear(); EmptyDashboard.Visibility = Visibility.Visible;
        RebuildCategories(); RefreshFindingsPage(true); UpdatePeoplePresentation(); UpdateModelControls();
        StatusText.Text = LocalizationService.Get("CleanupSecondaryDone");
    }

    public void DeleteAllApplicationData()
    {
        if (!CanRunCleanup) throw new InvalidOperationException(LocalizationService.Get("CleanupBusy"));
        _pageLoadCts?.Cancel(); _mediaFilterCts?.Cancel();
        _cleanupService.DeleteAllApplicationData();
    }

    void CancelAllBackgroundWork()
    {
        _cts?.Cancel();
        _textScanCts?.Cancel();
        _documentScanCts?.Cancel();
        _similarCts?.Cancel();
        _exifScanCts?.Cancel();
        _applicationHistoryCts?.Cancel();
        _personalTrainingCts?.Cancel();
        _provenanceCts?.Cancel();
        _pageLoadCts?.Cancel();
        _mediaFilterCts?.Cancel();
    }
}
