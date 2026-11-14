using DiskMonitor.App.Startup;

namespace DiskMonitor.App;

public sealed class SettingsForm : Form
{
    private readonly NumericUpDown _thresholdInput = new()
    {
        Minimum = 0.1M,
        Maximum = 100,
        DecimalPlaces = 1,
        Increment = 1,
        Dock = DockStyle.Fill,
        BackColor = Color.FromArgb(20, 24, 30),
        ForeColor = Color.White
    };

    private readonly NumericUpDown _durationInput = new()
    {
        Minimum = 1,
        Maximum = 3600,
        DecimalPlaces = 0,
        Increment = 5,
        Dock = DockStyle.Fill,
        BackColor = Color.FromArgb(20, 24, 30),
        ForeColor = Color.White
    };

    public SettingsForm(ReadinessSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Text = "Readiness Settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(430, 225);
        BackColor = Color.FromArgb(7, 9, 12);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9F);

        _thresholdInput.Value = (decimal)settings.ActiveTimeThresholdPercent;
        _durationInput.Value = settings.StableDurationSeconds;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 2,
            RowCount = 4,
            BackColor = BackColor
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var heading = new Label
        {
            Text = "System readiness",
            Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold),
            ForeColor = Color.White,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        layout.Controls.Add(heading, 0, 0);
        layout.SetColumnSpan(heading, 2);
        AddSetting(layout, "Disk active time must be below", _thresholdInput, 1, "%");
        AddSetting(layout, "Stable duration", _durationInput, 2, "seconds");

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };
        var save = CreateButton("Save");
        var cancel = CreateButton("Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        save.Click += (_, _) => SaveSettings();
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 3);
        layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout);

        AcceptButton = save;
        CancelButton = cancel;
    }

    public ReadinessSettings? UpdatedSettings { get; private set; }

    private static void AddSetting(
        TableLayoutPanel layout,
        string title,
        NumericUpDown input,
        int row,
        string suffix)
    {
        layout.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(190, 198, 208),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, row);

        var editor = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = layout.BackColor
        };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        editor.Controls.Add(input, 0, 0);
        editor.Controls.Add(new Label
        {
            Text = suffix,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(165, 175, 188),
            TextAlign = ContentAlignment.MiddleLeft
        }, 1, 0);
        layout.Controls.Add(editor, 1, row);
    }

    private static Button CreateButton(string text) => new()
    {
        Text = text,
        Width = 82,
        BackColor = Color.FromArgb(20, 24, 30),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat
    };

    private void SaveSettings()
    {
        var settings = new ReadinessSettings
        {
            ActiveTimeThresholdPercent = (double)_thresholdInput.Value,
            StableDurationSeconds = (int)_durationInput.Value
        };
        settings.Validate();
        UpdatedSettings = settings;
        DialogResult = DialogResult.OK;
        Close();
    }
}
