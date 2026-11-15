using System.Diagnostics;
using DiskMonitor.App.Controls;
using DiskMonitor.App.Startup;
using DiskMonitor.Core.Configuration;
using DiskMonitor.Core.Interfaces;
using DiskMonitor.Core.Models;
using DiskMonitor.Core.Services;
using DiskMonitor.Windows.Performance;
using DiskMonitor.Windows.Startup;

namespace DiskMonitor.App;

public sealed class MainForm : Form
{
    private readonly CancellationTokenSource _monitorCancellation = new();
    private readonly IDiskActiveTimeProvider _metricsProvider = new WindowsDiskMetricsProvider();
    private readonly StartupSettingsStore _settingsStore = new();
    private readonly WindowsStartupRegistration _startupRegistration = new();
    private readonly ActivityChart _activityChart = new();
    private readonly Label _activeTimeValue = new();
    private readonly Label _statusValue = new();
    private readonly Label _workflowValue = new();
    private readonly Label _progressValue = new();
    private readonly Label _thresholdValue = new();
    private readonly Label _durationValue = new();
    private readonly Label _startupValue = new();
    private readonly Button _settingsButton = new();
    private ReadinessSettings _settings;
    private Task? _monitoringTask;
    private int _settingsVersion;
    private bool _shutdownStarted;
    private bool _shutdownComplete;
    private string? _startupRegistrationError;

    public MainForm()
    {
        _settings = _settingsStore.Load();
        _settings.Validate();

        Text = "Disk Monitor — Workflow Readiness";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(640, 620);
        ClientSize = new Size(780, 650);
        BackColor = Color.FromArgb(7, 9, 12);
        Font = new Font("Segoe UI", 10F);

        try
        {
            _startupRegistration.SetEnabled(true);
        }
        catch (Exception exception) when (IsStartupRegistrationException(exception))
        {
            _startupRegistrationError = exception.Message;
            Debug.WriteLine($"Disk Monitor could not register for current-user startup: {exception}");
        }

        BuildDashboard();
        UpdateConfigurationSummary();
        Shown += (_, _) => StartMonitoring();
        FormClosing += HandleFormClosing;
    }

    private void BuildDashboard()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 4,
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 315));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        Controls.Add(root);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        header.Controls.Add(new Label
        {
            Text = "Disk Monitor",
            Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold),
            ForeColor = Color.White,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        _settingsButton.Text = "Settings";
        _settingsButton.Anchor = AnchorStyles.Right;
        _settingsButton.AutoSize = true;
        _settingsButton.BackColor = Color.FromArgb(20, 24, 30);
        _settingsButton.ForeColor = Color.White;
        _settingsButton.FlatStyle = FlatStyle.Flat;
        _settingsButton.FlatAppearance.BorderColor = Color.FromArgb(54, 62, 72);
        _settingsButton.Click += (_, _) => ShowSettings();
        header.Controls.Add(_settingsButton, 1, 0);
        root.Controls.Add(header, 0, 0);

        var statusCard = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(13, 16, 20),
            Padding = new Padding(18, 8, 18, 10),
            ColumnCount = 1,
            RowCount = 6,
            Margin = new Padding(0, 8, 0, 12)
        };
        statusCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        statusCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        statusCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        statusCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        statusCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        statusCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _statusValue.Text = "INITIALIZING";
        _statusValue.Font = new Font("Segoe UI Semibold", 27F, FontStyle.Bold);
        _statusValue.ForeColor = Color.FromArgb(85, 165, 255);
        _statusValue.Dock = DockStyle.Fill;
        _statusValue.TextAlign = ContentAlignment.BottomCenter;
        statusCard.Controls.Add(_statusValue, 0, 0);

        _activeTimeValue.Text = "Unavailable";
        _activeTimeValue.Font = new Font("Segoe UI Semibold", 25F, FontStyle.Bold);
        _activeTimeValue.ForeColor = Color.White;
        _activeTimeValue.Dock = DockStyle.Fill;
        _activeTimeValue.TextAlign = ContentAlignment.MiddleCenter;
        statusCard.Controls.Add(_activeTimeValue, 0, 1);
        statusCard.Controls.Add(CreateCaption("CURRENT DISK ACTIVE TIME"), 0, 2);

        _workflowValue.Text = "Checking disk activity before you start.";
        _workflowValue.Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold);
        _workflowValue.ForeColor = Color.FromArgb(180, 190, 202);
        _workflowValue.Dock = DockStyle.Fill;
        _workflowValue.TextAlign = ContentAlignment.MiddleCenter;
        statusCard.Controls.Add(_workflowValue, 0, 3);

        _progressValue.Text = "Waiting for the first valid disk counter sample.";
        _progressValue.ForeColor = Color.FromArgb(190, 198, 208);
        _progressValue.Dock = DockStyle.Fill;
        _progressValue.TextAlign = ContentAlignment.MiddleCenter;
        _progressValue.AutoEllipsis = true;
        statusCard.Controls.Add(_progressValue, 0, 4);

        var configuration = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(2, 4, 2, 0)
        };
        configuration.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        configuration.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        configuration.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        configuration.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        _thresholdValue.ForeColor = Color.FromArgb(165, 175, 188);
        _thresholdValue.Dock = DockStyle.Fill;
        _thresholdValue.TextAlign = ContentAlignment.MiddleLeft;
        _durationValue.ForeColor = Color.FromArgb(165, 175, 188);
        _durationValue.Dock = DockStyle.Fill;
        _durationValue.TextAlign = ContentAlignment.MiddleRight;
        _startupValue.ForeColor = Color.FromArgb(135, 145, 158);
        _startupValue.Dock = DockStyle.Fill;
        _startupValue.TextAlign = ContentAlignment.MiddleLeft;
        _startupValue.AutoEllipsis = true;
        var monitoringValue = new Label
        {
            Text = "Monitoring: Active",
            ForeColor = Color.FromArgb(0, 210, 105),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight
        };
        configuration.Controls.Add(_thresholdValue, 0, 0);
        configuration.Controls.Add(_durationValue, 1, 0);
        configuration.Controls.Add(_startupValue, 0, 1);
        configuration.Controls.Add(monitoringValue, 1, 1);
        statusCard.Controls.Add(configuration, 0, 5);
        root.Controls.Add(statusCard, 0, 1);

        var graphCard = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(13, 16, 20),
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 8)
        };
        graphCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        graphCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        graphCard.Controls.Add(CreateCaption("RECENT DISK ACTIVITY"), 0, 0);
        _activityChart.Dock = DockStyle.Fill;
        graphCard.Controls.Add(_activityChart, 0, 1);
        root.Controls.Add(graphCard, 0, 2);

        var footer = new Label
        {
            Text = "Ready means disk activity has stayed below your threshold for the full duration.",
            ForeColor = Color.FromArgb(135, 145, 158),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = true
        };
        root.Controls.Add(footer, 0, 3);
    }

    private static Label CreateCaption(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Color.FromArgb(135, 145, 158),
        Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold)
    };

    private void UpdateConfigurationSummary()
    {
        _thresholdValue.Text = $"Disk activity threshold: {_settings.ActiveTimeThresholdPercent:0.#}%";
        _durationValue.Text = $"Required stable duration: {_settings.StableDurationSeconds} seconds";
        _startupValue.Text = _startupRegistrationError is null
            ? "Startup: enabled"
            : $"Startup registration unavailable: {_startupRegistrationError}";
        _startupValue.ForeColor = _startupRegistrationError is null
            ? Color.FromArgb(135, 145, 158)
            : Color.FromArgb(255, 180, 90);
    }

    private void StartMonitoring()
    {
        _monitoringTask ??= Task.Run(() => MonitorAsync(_monitorCancellation.Token));
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        try
        {
            int appliedSettingsVersion = -1;
            ReadinessEvaluator evaluator = new(CreateOptions(_settings));
            await foreach (DiskActiveTimeSample sample in _metricsProvider
                .ReadActiveTimeAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                int currentSettingsVersion = Volatile.Read(ref _settingsVersion);
                if (currentSettingsVersion != appliedSettingsVersion)
                {
                    evaluator = new ReadinessEvaluator(CreateOptions(_settings));
                    appliedSettingsVersion = currentSettingsVersion;
                }

                ReadinessUpdate update = evaluator.Evaluate(sample);
                PostToUi(() => UpdateDashboard(sample, update));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Disk Monitor stopped collecting disk active time: {exception}");
            PostToUi(() =>
            {
                _activeTimeValue.Text = "Unavailable";
                _statusValue.Text = "PLEASE WAIT";
                _statusValue.ForeColor = Color.FromArgb(255, 190, 75);
                _workflowValue.Text = "Please wait before opening apps.";
                _workflowValue.ForeColor = Color.FromArgb(255, 190, 75);
                _progressValue.Text = $"Monitoring stopped unexpectedly: {exception.Message}";
            });
        }
    }

    private void UpdateDashboard(DiskActiveTimeSample sample, ReadinessUpdate update)
    {
        _activeTimeValue.Text = update.ActiveTimePercent is double activeTime
            ? $"{activeTime:F1}%"
            : "Unavailable";
        _activityChart.AddMeasurement(sample);
        (_statusValue.Text, _statusValue.ForeColor, _workflowValue.Text, _workflowValue.ForeColor) = update.State switch
        {
            ReadinessState.Initializing => (
                "INITIALIZING",
                Color.FromArgb(85, 165, 255),
                "Checking disk activity before you start.",
                Color.FromArgb(180, 190, 202)),
            ReadinessState.Ready => (
                "READY TO GO",
                Color.FromArgb(0, 210, 105),
                "You can start your workflow!",
                Color.FromArgb(0, 210, 105)),
            _ => (
                "PLEASE WAIT",
                Color.FromArgb(255, 190, 75),
                "Please wait before opening apps.",
                Color.FromArgb(255, 190, 75))
        };
        _progressValue.Text = update.Explanation;
    }

    private void ShowSettings()
    {
        using var settingsForm = new SettingsForm(_settings);
        if (settingsForm.ShowDialog(this) != DialogResult.OK
            || settingsForm.UpdatedSettings is not ReadinessSettings updatedSettings
            || updatedSettings == _settings)
        {
            return;
        }

        try
        {
            updatedSettings.Validate();
            _settingsStore.Save(updatedSettings);
            _settings = updatedSettings;
            Interlocked.Increment(ref _settingsVersion);
            UpdateConfigurationSummary();
            _progressValue.Text = "Settings saved. Stability timer restarted with the new threshold.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _workflowValue.Text = "Settings could not be saved.";
            _workflowValue.ForeColor = Color.FromArgb(255, 190, 75);
            _progressValue.Text = exception.Message;
        }
    }

    private static ReadinessOptions CreateOptions(ReadinessSettings settings) =>
        new(settings.ActiveTimeThresholdPercent, settings.StableDurationSeconds);

    private void PostToUi(Action update)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(update);
        }
        catch (InvalidOperationException) when (IsDisposed || !IsHandleCreated)
        {
        }
    }

    private async void HandleFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_shutdownComplete)
        {
            return;
        }

        eventArgs.Cancel = true;
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;
        _settingsButton.Enabled = false;
        _monitorCancellation.Cancel();
        if (_monitoringTask is not null)
        {
            await _monitoringTask;
        }

        _shutdownComplete = true;
        Close();
    }

    private static bool IsStartupRegistrationException(Exception exception) =>
        exception is ArgumentException
            or IOException
            or InvalidOperationException
            or System.Security.SecurityException
            or UnauthorizedAccessException;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _monitorCancellation.Cancel();
            _monitorCancellation.Dispose();
            Font.Dispose();
        }

        base.Dispose(disposing);
    }
}
