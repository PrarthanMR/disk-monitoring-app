using System.Drawing.Drawing2D;
using DiskMonitor.Core.Models;

namespace DiskMonitor.App.Controls;

public sealed class ActivityChart : Control
{
    private const int MaximumPoints = 60;
    private readonly Queue<(DateTimeOffset Timestamp, double Value)> _points = new();

    public ActivityChart()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
        ForeColor = Color.FromArgb(35, 115, 190);
        MinimumSize = new Size(180, 120);
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    public void AddMeasurement(DiskMetrics metrics)
    {
        if (metrics.ActiveTimePercent is not double value || !double.IsFinite(value))
        {
            return;
        }

        _points.Enqueue((metrics.TimestampUtc, Math.Clamp(value, 0, 100)));
        while (_points.Count > MaximumPoints)
        {
            _points.Dequeue();
        }

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        if (ClientSize.Width < 40 || ClientSize.Height < 40)
        {
            return;
        }

        Graphics graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        const int left = 32;
        const int right = 8;
        const int top = 8;
        const int bottom = 22;
        float plotWidth = ClientSize.Width - left - right;
        float plotHeight = ClientSize.Height - top - bottom;
        if (plotWidth <= 0 || plotHeight <= 0)
        {
            return;
        }

        using var gridPen = new Pen(Color.FromArgb(229, 233, 239), 1);
        using var labelBrush = new SolidBrush(Color.FromArgb(110, 120, 134));
        using var labelFont = new Font("Segoe UI", 7.5F);
        for (int percent = 0; percent <= 100; percent += 25)
        {
            float y = top + plotHeight * (100 - percent) / 100;
            graphics.DrawLine(gridPen, left, y, left + plotWidth, y);
            graphics.DrawString($"{percent}%", labelFont, labelBrush, 0, y - 7);
        }

        (DateTimeOffset Timestamp, double Value)[] points = _points.ToArray();
        if (points.Length < 2)
        {
            graphics.DrawString("Collecting activity samples…", labelFont, labelBrush, left + 6, top + plotHeight / 2);
            return;
        }

        PointF[] line = new PointF[points.Length];
        for (int index = 0; index < points.Length; index++)
        {
            float x = left + plotWidth * index / (points.Length - 1);
            float y = top + plotHeight * (float)(100 - points[index].Value) / 100;
            line[index] = new PointF(x, y);
        }

        using var linePen = new Pen(ForeColor, 2.2F);
        graphics.DrawLines(linePen, line);
        graphics.DrawString(
            $"{points[0].Timestamp.ToLocalTime():HH:mm:ss}  —  {points[^1].Timestamp.ToLocalTime():HH:mm:ss}",
            labelFont,
            labelBrush,
            left,
            top + plotHeight + 4);
    }
}
