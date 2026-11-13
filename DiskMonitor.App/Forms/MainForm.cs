using System.Diagnostics;
using DiskMonitor.App.Controls;
using DiskMonitor.App.Lifecycle;
using DiskMonitor.App.Notifications;
using DiskMonitor.App.Startup;
using DiskMonitor.Core.Models;
using DiskMonitor.Core.Services;
using DiskMonitor.Windows.Performance;
using DiskMonitor.Windows.Startup;

namespace DiskMonitor.App;

public sealed class MainForm : Form
{
    private readonly CancellationTokenSource _monitorCancellation = new();
    private readonly WindowsDiskMetricsProvider _metricsProvider = new();
    private readonly HealthEvaluator _healthEvaluator = new();
    private readonly ReadinessEvaluator _readinessEvaluator = new();
    private readonly StartupSettingsStore _settingsStore = new();
    private readonly WindowsStartupRegistration _startupRegistration = new();
    private readonly ActivityChart _activityChart = new();
    private readonly Label _readinessValue = CreateValueLabel("Initializing");
    private readonly Label _readinessExplanation = CreateSecondaryLabel(
        "Collecting the initial 30-second disk baseline.");
    private readonly Label _healthValue = CreateValueLabel("Initializing");
    private readonly Label _activeTimeValue = CreateValueLabel("Waiting for metrics");
    private readonly Label _readValue = CreateValueLabel("Waiting for metrics");
    private readonly Label _writeValue = CreateValueLabel("Waiting for metrics");
    private readonly Label _responseValue = CreateValueLabel("Waiting for metrics");
    private readonly Label _queueValue = CreateValueLabel("Waiting for metrics");
    private readonly Label _spaceValue = CreateValueLabel("Waiting for metrics");
    private readonly Label _lastUpdatedValue = CreateSecondaryLabel("No samples collected");
    private readonly Label _healthExplanation = CreateSecondaryLabel(
        "Waiting for enough measurements to assess disk health.");
    private readonly ListBox _alertsList = new();
    private readonly ProgressBar _spaceBar = new();
    private readonly ToolTip _toolTip = new();
    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _showMenuItem;
    private readonly ToolStripMenuItem _currentStatusMenuItem;
    private readonly ToolStripMenuItem _pauseMenuItem;
    private readonly ToolStripMenuItem _resumeMenuItem;
    private readonly ToolStripMenuItem _startupMenuItem;
    private readonly ToolStripMenuItem _exitMenuItem;
    private readonly Queue<DiskAlert> _recentAlerts = new();
    private readonly object _monitorStateLock = new();
    private readonly INotificationService _notificationService;
    private StartupSettings _startupSettings;
    private TaskCompletionSource _resumeSignal = CreateResumedSignal();
    private CancellationTokenSource? _activeReadCancellation;
    private DiskMetrics? _latestMetrics;
    private SystemReadiness? _latestReadiness;
    private Task? _monitoringTask;
    private bool _monitoringPaused;
    private bool _readinessNotificationShown;
    private bool _warningNotifiedForIncident;
    private bool _criticalNotifiedForIncident;
    private bool _allowClose;
    private bool _shutdownStarted;

    public MainForm()
    {
        Text = "Disk Monitor";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(780, 600);
        ClientSize = new Size(920, 700);
        BackColor = Color.FromArgb(245, 247, 250);
        Font = new Font("Segoe UI", 9F);

        _startupSettings = _settingsStore.Load();
        try
        {
            _startupRegistration.SetEnabled(_startupSettings.StartWithWindows);
        }
        catch (Exception exception) when (IsStartupRegistrationException(exception))
        {
            MessageBox.Show(
                this,
                $"Could not synchronize the Windows startup setting.\n\n{exception.Message}",
                "Disk Monitor startup setting",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        var trayMenu = new ContextMenuStrip();
        _showMenuItem = new ToolStripMenuItem("Open Dashboard", null, (_, _) => ShowDashboard());
        _currentStatusMenuItem = new ToolStripMenuItem("Current Status", null, (_, _) => ShowCurrentStatus());
        _pauseMenuItem = new ToolStripMenuItem("Pause Monitoring", null, (_, _) => PauseMonitoring());
        _resumeMenuItem = new ToolStripMenuItem("Resume Monitoring", null, (_, _) => ResumeMonitoring())
        {
            Enabled = false
        };
        _startupMenuItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = _startupSettings.StartWithWindows
        };
        _startupMenuItem.CheckedChanged += HandleStartupSettingChanged;
        _exitMenuItem = new ToolStripMenuItem("Exit", null, async (_, _) => await ExitApplicationAsync());
        trayMenu.Items.AddRange([
            _showMenuItem,
            _currentStatusMenuItem,
            new ToolStripSeparator(),
            _pauseMenuItem,
            _resumeMenuItem,
            _startupMenuItem,
            new ToolStripSeparator(),
            _exitMenuItem
        ]);
        _trayIcon = new NotifyIcon
        {
            Text = "Disk Monitor is running",
            Icon = SystemIcons.Information,
            ContextMenuStrip = trayMenu,
            Visible = true
        };
        _notificationService = new BalloonNotificationService(_trayIcon);
        _trayIcon.DoubleClick += (_, _) => ShowDashboard();

        BuildDashboard();
        FormClosing += HandleFormClosing;
        Shown += (_, _) => StartMonitoring();
        Application.ApplicationExit += HandleApplicationExit;
    }

    private void BuildDashboard()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            ColumnCount = 1,
            RowCount = 4,
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        Controls.Add(root);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 10)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        var title = new Label
        {
            Text = "Disk Monitor",
            Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold),
            ForeColor = Color.FromArgb(32, 40, 52),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        header.Controls.Add(title, 0, 0);
        header.SetRowSpan(title, 2);
        header.Controls.Add(new Label
        {
            Text = "Physical disk · All drives",
            Font = new Font("Segoe UI", 9F),
            ForeColor = Color.FromArgb(100, 110, 125),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomRight
        }, 1, 0);
        _lastUpdatedValue.TextAlign = ContentAlignment.TopRight;
        header.Controls.Add(_lastUpdatedValue, 1, 1);
        root.Controls.Add(header, 0, 0);

        root.Controls.Add(BuildReadinessCard(), 0, 1);

        var middle = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 10)
        };
        middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        middle.Controls.Add(BuildPerformanceCard(), 0, 0);
        middle.Controls.Add(BuildActivityCard(), 1, 0);
        root.Controls.Add(middle, 0, 2);

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        bottom.Controls.Add(BuildHealthCard(), 0, 0);
        bottom.Controls.Add(BuildAlertsCard(), 1, 0);
        root.Controls.Add(bottom, 0, 3);
    }

    private Control BuildReadinessCard()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16, 11, 16, 9)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(CreateSectionTitle("SYSTEM READINESS"), 0, 0);
        _readinessValue.Font = new Font("Segoe UI Semibold", 17F, FontStyle.Bold);
        layout.Controls.Add(_readinessValue, 0, 1);
        layout.Controls.Add(_readinessExplanation, 0, 2);
        return CreateCard(layout);
    }

    private Control BuildPerformanceCard()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(16, 12, 16, 12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 53));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        for (int row = 1; row < 6; row++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
        }

        layout.Controls.Add(CreateSectionTitle("LIVE PERFORMANCE"), 0, 0);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 0)!, 2);
        AddMetricRow(layout, 1, "Disk active time", _activeTimeValue);
        AddMetricRow(layout, 2, "Read throughput", _readValue);
        AddMetricRow(layout, 3, "Write throughput", _writeValue);
        AddMetricRow(layout, 4, "Average response", _responseValue);
        AddMetricRow(layout, 5, "Current queue length", _queueValue);
        return CreateCard(layout);
    }

    private Control BuildActivityCard()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(16, 12, 16, 12)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(CreateSectionTitle("RECENT DISK ACTIVITY"), 0, 0);
        _activityChart.Dock = DockStyle.Fill;
        layout.Controls.Add(_activityChart, 0, 1);
        return CreateCard(layout);
    }

    private Control BuildHealthCard()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16, 12, 16, 12)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(CreateSectionTitle("DISK HEALTH"), 0, 0);
        layout.Controls.Add(_healthValue, 0, 1);
        layout.Controls.Add(_healthExplanation, 0, 2);

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 10, 0, 0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 68));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
        panel.Controls.Add(CreateCard(layout), 0, 0);
        panel.Controls.Add(BuildSpaceCard(), 0, 1);
        return panel;
    }

    private Control BuildSpaceCard()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16, 9, 16, 8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 21));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(CreateSectionTitle("SYSTEM VOLUME SPACE"), 0, 0);
        layout.Controls.Add(_spaceValue, 0, 1);
        _spaceBar.Dock = DockStyle.Fill;
        _spaceBar.Minimum = 0;
        _spaceBar.Maximum = 100;
        _spaceBar.Style = ProgressBarStyle.Continuous;
        layout.Controls.Add(_spaceBar, 0, 2);
        return CreateCard(layout);
    }

    private Control BuildAlertsCard()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(16, 12, 16, 12)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(CreateSectionTitle("RECENT DISK ALERTS"), 0, 0);
        _alertsList.Dock = DockStyle.Fill;
        _alertsList.BorderStyle = BorderStyle.None;
        _alertsList.BackColor = Color.White;
        _alertsList.Font = new Font("Segoe UI", 8.5F);
        _alertsList.HorizontalScrollbar = true;
        _alertsList.Items.Add("No alerts yet. Monitoring is active.");
        layout.Controls.Add(_alertsList, 0, 1);
        return CreateCard(layout);
    }

    private static Panel CreateCard(Control content)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(1),
            Margin = new Padding(0, 0, 10, 0)
        };
        card.Controls.Add(content);
        return card;
    }

    private static Label CreateSectionTitle(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.FromArgb(105, 115, 130),
        Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold)
    };

    private static Label CreateValueLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.FromArgb(36, 45, 59),
        Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold),
        AutoEllipsis = true
    };

    private static Label CreateSecondaryLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.FromArgb(103, 113, 127),
        Font = new Font("Segoe UI", 9F),
        AutoEllipsis = true
    };

    private static void AddMetricRow(TableLayoutPanel layout, int row, string name, Label value)
    {
        layout.Controls.Add(new Label
        {
            Text = name,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(100, 110, 125),
            Font = new Font("Segoe UI", 9F)
        }, 0, row);
        layout.Controls.Add(value, 1, row);
    }

    private void StartMonitoring()
    {
        if (_monitoringTask is not null)
        {
            return;
        }

        _monitoringTask = Task.Run(() => MonitorAsync(_monitorCancellation.Token));
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Task resumeTask;
                lock (_monitorStateLock)
                {
                    resumeTask = _monitoringPaused ? _resumeSignal.Task : Task.CompletedTask;
                }

                await resumeTask.WaitAsync(cancellationToken).ConfigureAwait(false);

                using var sampleCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                lock (_monitorStateLock)
                {
                    if (_monitoringPaused)
                    {
                        sampleCancellation.Cancel();
                    }
                    else
                    {
                        _activeReadCancellation = sampleCancellation;
                    }
                }

                if (sampleCancellation.IsCancellationRequested)
                {
                    continue;
                }

                try
                {
                    await foreach (DiskMetrics metrics in _metricsProvider
                        .ReadMetricsAsync(sampleCancellation.Token)
                        .ConfigureAwait(false))
                    {
                        DiskAlert? alert = _healthEvaluator.Evaluate(metrics);
                        SystemReadiness readiness = _readinessEvaluator.Evaluate(metrics);
                        DiskHealthState healthState = _healthEvaluator.CurrentState;
                        PostToUi(() => UpdateDashboard(metrics, readiness, healthState, alert));
                    }
                }
                catch (OperationCanceledException) when (sampleCancellation.IsCancellationRequested)
                {
                }
                finally
                {
                    lock (_monitorStateLock)
                    {
                        if (ReferenceEquals(_activeReadCancellation, sampleCancellation))
                        {
                            _activeReadCancellation = null;
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Disk monitoring stopped unexpectedly: {exception}");
            PostToUi(() =>
            {
                _readinessValue.Text = "Monitoring unavailable";
                _readinessValue.ForeColor = Color.Firebrick;
                _readinessExplanation.Text = exception.Message;
                _healthValue.Text = "Unavailable";
                _healthValue.ForeColor = Color.Firebrick;
                _healthExplanation.Text = "The monitoring stream stopped unexpectedly.";
            });
        }
    }

    private static TaskCompletionSource CreateResumedSignal()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.SetResult();
        return signal;
    }

    private void UpdateDashboard(
        DiskMetrics metrics,
        SystemReadiness readiness,
        DiskHealthState healthState,
        DiskAlert? alert)
    {
        _latestMetrics = metrics;
        _latestReadiness = readiness;

        if (!_readinessNotificationShown && readiness.State != SystemReadinessState.Initializing)
        {
            _readinessNotificationShown = true;
            _notificationService.Show(
                $"Startup assessment: {readiness.State}",
                readiness.Explanation,
                readiness.State == SystemReadinessState.Degraded ? ToolTipIcon.Warning : ToolTipIcon.Info);
        }

        _readinessValue.Text = readiness.State.ToString();
        _readinessValue.ForeColor = readiness.State switch
        {
            SystemReadinessState.Ready => Color.FromArgb(20, 125, 75),
            SystemReadinessState.Degraded => Color.Firebrick,
            SystemReadinessState.Wait => Color.DarkGoldenrod,
            _ => Color.FromArgb(55, 105, 165)
        };
        _readinessExplanation.Text = readiness.Explanation;

        _healthValue.Text = healthState.ToString();
        _healthValue.ForeColor = healthState switch
        {
            DiskHealthState.Critical or DiskHealthState.Unavailable => Color.Firebrick,
            DiskHealthState.Warning => Color.DarkGoldenrod,
            DiskHealthState.Recovery => Color.FromArgb(25, 115, 150),
            DiskHealthState.Normal => Color.FromArgb(20, 125, 75),
            _ => Color.FromArgb(55, 105, 165)
        };
        _healthExplanation.Text = alert?.Explanation ?? GetHealthExplanation(healthState);

        _activeTimeValue.Text = FormatMetric(metrics.ActiveTimePercent, "%");
        _readValue.Text = FormatMetric(metrics.ReadThroughputMBps, "MB/s");
        _writeValue.Text = FormatMetric(metrics.WriteThroughputMBps, "MB/s");
        _responseValue.Text = FormatMetric(metrics.AverageResponseTimeMilliseconds, "ms");
        _queueValue.Text = FormatMetric(metrics.CurrentDiskQueueLength, string.Empty);
        _lastUpdatedValue.Text = $"Updated {metrics.TimestampUtc.ToLocalTime():HH:mm:ss}";

        foreach (Label valueLabel in new[]
        {
            _activeTimeValue,
            _readValue,
            _writeValue,
            _responseValue,
            _queueValue,
            _healthExplanation
        })
        {
            _toolTip.SetToolTip(valueLabel, null);
        }

        foreach ((string metric, string reason) in metrics.UnavailableMetrics)
        {
            _toolTip.SetToolTip(FindMetricLabel(metric), reason);
        }

        if (metrics.SystemVolumeSpace is DiskSpaceMetrics space)
        {
            _spaceValue.Text = $"{FormatBytes(space.AvailableFreeBytes)} free of {FormatBytes(space.CapacityBytes)}";
            _spaceBar.Value = space.CapacityBytes > 0
                ? (int)Math.Clamp(
                    (space.CapacityBytes - space.AvailableFreeBytes) * 100d / space.CapacityBytes,
                    0,
                    100)
                : 0;
            _toolTip.SetToolTip(_spaceBar, $"{space.VolumeName} · {FormatBytes(space.AvailableFreeBytes)} available");
        }
        else
        {
            _spaceValue.Text = "Unavailable";
            _spaceBar.Value = 0;
        }

        _activityChart.AddMeasurement(metrics);

        if (alert is not null)
        {
            if (alert.State is DiskHealthState.Warning or DiskHealthState.Critical or DiskHealthState.Recovery)
            {
                if (_alertsList.Items.Count == 1 && Equals(_alertsList.Items[0], "No alerts yet. Monitoring is active."))
                {
                    _alertsList.Items.Clear();
                }

                _recentAlerts.Enqueue(alert);
                while (_recentAlerts.Count > 20)
                {
                    _recentAlerts.Dequeue();
                }

                _alertsList.Items.Clear();
                foreach (DiskAlert recent in _recentAlerts.Reverse())
                {
                    _alertsList.Items.Add($"{recent.TimestampUtc.ToLocalTime():HH:mm:ss}  {recent.State}: {recent.Explanation}");
                }
            }

            NotifyHealthTransition(alert);
        }
    }

    private void NotifyHealthTransition(DiskAlert alert)
    {
        switch (alert.State)
        {
            case DiskHealthState.Warning when !_warningNotifiedForIncident:
                _warningNotifiedForIncident = true;
                _notificationService.Show("Disk activity warning", alert.Explanation, ToolTipIcon.Warning);
                break;
            case DiskHealthState.Critical when !_criticalNotifiedForIncident:
                _criticalNotifiedForIncident = true;
                _notificationService.Show("Critical disk performance", alert.Explanation, ToolTipIcon.Error);
                break;
            case DiskHealthState.Recovery:
                _warningNotifiedForIncident = false;
                _criticalNotifiedForIncident = false;
                _notificationService.Show("Disk performance recovered", alert.Explanation, ToolTipIcon.Info);
                break;
        }
    }

    private Label FindMetricLabel(string metric) => metric switch
    {
        "Disk active time" => _activeTimeValue,
        "Disk read throughput" => _readValue,
        "Disk write throughput" => _writeValue,
        "Average disk response time" => _responseValue,
        "Current disk queue length" => _queueValue,
        _ => _healthExplanation
    };

    private static string GetHealthExplanation(DiskHealthState state) => state switch
    {
        DiskHealthState.Normal => "Recent activity and response time are within configured thresholds.",
        DiskHealthState.InsufficientData => "Collecting measurements to assess disk health.",
        DiskHealthState.Unavailable => "One or more required health metrics are unavailable.",
        DiskHealthState.Warning => "Sustained disk activity is elevated. High activity alone does not mean disk failure.",
        DiskHealthState.Critical => "Sustained critical latency or combined activity and latency was detected.",
        DiskHealthState.Recovery => "Disk measurements met the configured recovery conditions.",
        _ => "Waiting for disk health evaluation."
    };

    private static string FormatMetric(double? value, string unit) =>
        value is double number && double.IsFinite(number)
            ? $"{number:F1}{(unit.Length > 0 ? $" {unit}" : string.Empty)}"
            : "Unavailable";

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:F1} {units[unitIndex]}";
    }

    private void PostToUi(Action update)
    {
        if (!IsDisposed && IsHandleCreated)
        {
            try
            {
                BeginInvoke(update);
            }
            catch (InvalidOperationException) when (IsDisposed || !IsHandleCreated)
            {
            }
        }
    }

    private void ShowDashboard()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ShowCurrentStatus()
    {
        if (_latestMetrics is not DiskMetrics metrics || _latestReadiness is not SystemReadiness readiness)
        {
            _notificationService.Show(
                "Disk Monitor status",
                "Monitoring is initializing and has not produced a sample yet.",
                ToolTipIcon.Info);
            return;
        }

        string monitoringState = _monitoringPaused ? "Paused" : "Running";
        string message = $"{monitoringState} · Readiness: {readiness.State} · Health: {_healthEvaluator.CurrentState} · "
            + $"Active: {FormatMetric(metrics.ActiveTimePercent, "%")} · "
            + $"Response: {FormatMetric(metrics.AverageResponseTimeMilliseconds, "ms")}";
        _notificationService.Show("Disk Monitor status", message, ToolTipIcon.Info);
    }

    private void PauseMonitoring()
    {
        lock (_monitorStateLock)
        {
            if (_monitoringPaused)
            {
                return;
            }

            _monitoringPaused = true;
            _resumeSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _activeReadCancellation?.Cancel();
        }

        _pauseMenuItem.Enabled = false;
        _resumeMenuItem.Enabled = true;
        _trayIcon.Text = "Disk Monitor is paused";
        _notificationService.Show("Disk Monitor paused", "Disk monitoring is paused.", ToolTipIcon.Info);
    }

    private void ResumeMonitoring()
    {
        lock (_monitorStateLock)
        {
            if (!_monitoringPaused)
            {
                return;
            }

            _monitoringPaused = false;
            _resumeSignal.TrySetResult();
        }

        _pauseMenuItem.Enabled = true;
        _resumeMenuItem.Enabled = false;
        _trayIcon.Text = "Disk Monitor is running";
        _notificationService.Show("Disk Monitor resumed", "Disk monitoring has resumed.", ToolTipIcon.Info);
    }

    private void HandleStartupSettingChanged(object? sender, EventArgs eventArgs)
    {
        var updatedSettings = new StartupSettings(_startupMenuItem.Checked);
        if (updatedSettings == _startupSettings)
        {
            return;
        }

        try
        {
            _settingsStore.Save(updatedSettings);
            try
            {
                _startupRegistration.SetEnabled(updatedSettings.StartWithWindows);
            }
            catch (Exception exception) when (IsStartupRegistrationException(exception))
            {
                _settingsStore.Save(_startupSettings);
                throw;
            }

            _startupSettings = updatedSettings;
            _notificationService.Show(
                "Disk Monitor startup",
                updatedSettings.StartWithWindows
                    ? "Disk Monitor will start when you sign in."
                    : "Disk Monitor will no longer start when you sign in.",
                ToolTipIcon.Info);
        }
        catch (Exception exception) when (IsStartupRegistrationException(exception))
        {
            _startupMenuItem.Checked = _startupSettings.StartWithWindows;
            MessageBox.Show(
                this,
                $"Could not update the current-user startup setting.\n\n{exception.Message}",
                "Disk Monitor startup setting",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private static bool IsStartupRegistrationException(Exception exception) =>
        exception is ArgumentException
            or IOException
            or InvalidOperationException
            or System.Security.SecurityException
            or UnauthorizedAccessException;

    private async void HandleFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_allowClose)
        {
            _trayIcon.Visible = false;
            return;
        }

        if (eventArgs.CloseReason is CloseReason.WindowsShutDown or CloseReason.ApplicationExitCall)
        {
            eventArgs.Cancel = true;
            await ExitApplicationAsync();
            return;
        }

        if (DashboardClosePolicy.ShouldHide(eventArgs.CloseReason, explicitExit: false))
        {
            eventArgs.Cancel = true;
            Hide();
            _notificationService.Show(
                "Disk Monitor is still running",
                "Monitoring continues in the notification area.",
                ToolTipIcon.Info);
            return;
        }
    }

    private async Task ExitApplicationAsync()
    {
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;
        _showMenuItem.Enabled = false;
        _currentStatusMenuItem.Enabled = false;
        _pauseMenuItem.Enabled = false;
        _resumeMenuItem.Enabled = false;
        _startupMenuItem.Enabled = false;
        _exitMenuItem.Enabled = false;

        _monitorCancellation.Cancel();
        lock (_monitorStateLock)
        {
            _activeReadCancellation?.Cancel();
            _resumeSignal.TrySetResult();
        }

        if (_monitoringTask is not null)
        {
            await _monitoringTask;
        }

        _allowClose = true;
        _trayIcon.Visible = false;
        Close();
    }

    private void HandleApplicationExit(object? sender, EventArgs eventArgs)
    {
        _monitorCancellation.Cancel();
        lock (_monitorStateLock)
        {
            _activeReadCancellation?.Cancel();
            _resumeSignal.TrySetResult();
        }

        _trayIcon.Visible = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Application.ApplicationExit -= HandleApplicationExit;
            _monitorCancellation.Cancel();
            _trayIcon.Dispose();
            _toolTip.Dispose();
            _monitorCancellation.Dispose();
            Font.Dispose();
        }

        base.Dispose(disposing);
    }
}
