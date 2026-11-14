using System.Drawing.Drawing2D;
using DiskMonitor.Core.Models;

namespace DiskMonitor.App.Controls;

public sealed class ActivityChart : Control
{
    private const int MaximumPoints = 60;
    private readonly Queue<(DateTimeOffset Timestamp, double? Value)> _points = new();

    public ActivityChart()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(13, 16, 20);
        ForeColor = Color.FromArgb(0, 210, 105);
        MinimumSize = new Size(180, 120);
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    public void AddMeasurement(DiskActiveTimeSample sample)
    {
        if (sample.ActiveTimePercent is not double value
            || !double.IsFinite(value)
            || value is < 0 or > 100)
        {
            _points.Enqueue((sample.TimestampUtc, null));
        }
        else
        {
            _points.Enqueue((sample.TimestampUtc, value));
        }

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

        using var gridPen = new Pen(Color.FromArgb(38, 44, 52), 1);
        using var labelBrush = new SolidBrush(Color.FromArgb(145, 155, 168));
        using var labelFont = new Font("Segoe UI", 7.5F);
        for (int percent = 0; percent <= 100; percent += 25)
        {
            float y = top + plotHeight * (100 - percent) / 100;
            graphics.DrawLine(gridPen, left, y, left + plotWidth, y);
            graphics.DrawString($"{percent}%", labelFont, labelBrush, 0, y - 7);
        }

        (DateTimeOffset Timestamp, double? Value)[] points = _points.ToArray();
        if (points.Count(point => point.Value.HasValue) < 2)
        {
            graphics.DrawString("Collecting activity samples…", labelFont, labelBrush, left + 6, top + plotHeight / 2);
            return;
        }

        var line = new List<PointF>();
        for (int index = 0; index < points.Length; index++)
        {
            if (points[index].Value is double value)
            {
                float x = left + plotWidth * index / (points.Length - 1);
                float y = top + plotHeight * (float)(100 - value) / 100;
                line.Add(new PointF(x, y));
            }
            else
            {
                DrawLineSegment(graphics, line, ForeColor);
            }
        }

        DrawLineSegment(graphics, line, ForeColor);
        (DateTimeOffset Timestamp, double? Value) first = points.First(point => point.Value.HasValue);
        (DateTimeOffset Timestamp, double? Value) last = points.Last(point => point.Value.HasValue);
        graphics.DrawString(
            $"{first.Timestamp.ToLocalTime():HH:mm:ss}  —  {last.Timestamp.ToLocalTime():HH:mm:ss}",
            labelFont,
            labelBrush,
            left,
            top + plotHeight + 4);
    }

    private static void DrawLineSegment(Graphics graphics, List<PointF> line, Color color)
    {
        if (line.Count > 1)
        {
            using var linePen = new Pen(color, 2.2F);
            graphics.DrawLines(linePen, line.ToArray());
        }

        line.Clear();
    }
}
