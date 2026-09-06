using System.Drawing.Drawing2D;
using System.Drawing.Text;
using TaskbarStats.Models;

namespace TaskbarStats.UI;

public sealed class HistoryForm : Form
{
    private sealed record Lane(string Label, Color Color, Func<SystemStatsSample, double?> Value);

    private static readonly Lane[] Lanes =
    {
        new("CPU", Color.FromArgb(66, 165, 245), s => s.CpuPercent),
        new("RAM", Color.FromArgb(171, 71, 188), s => s.RamPercent),
        new("GPU", Color.FromArgb(102, 187, 106), s => s.GpuPercent),
        new("VRAM", Color.FromArgb(255, 167, 38), s => s.VramPercent),
    };

    private readonly StatsHistory _history;
    private readonly System.Windows.Forms.Timer _timer;

    public HistoryForm(StatsHistory history)
    {
        _history = history;

        Text = "TaskbarStats - 履歴 (直近5分)";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(640, 440);
        MinimumSize = new Size(420, 300);
        BackColor = Color.FromArgb(30, 30, 34);
        ForeColor = Color.FromArgb(225, 225, 230);
        DoubleBuffered = true;

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => Invalidate();
        _timer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var samples = _history.Snapshot();
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        int laneHeight = ClientSize.Height / Lanes.Length;
        for (int i = 0; i < Lanes.Length; i++)
        {
            DrawLane(g, samples, i * laneHeight, laneHeight, Lanes[i]);
        }
    }

    private void DrawLane(Graphics g, IReadOnlyList<SystemStatsSample> samples, int top, int height, Lane lane)
    {
        var values = samples.Select(lane.Value).Where(v => v is not null).Select(v => v!.Value).ToList();
        double min = values.Count > 0 ? values.Min() : 0.0;
        double max = values.Count > 0 ? values.Max() : 0.0;
        double avg = values.Count > 0 ? values.Average() : 0.0;

        using (var labelBrush = new SolidBrush(lane.Color))
        {
            g.DrawString(lane.Label, Font, labelBrush, 12, top + 6);
        }

        string statsText = values.Count > 0
            ? $"min {min:F0}%   avg {avg:F0}%   max {max:F0}%"
            : "データ待ち...";
        using (var statsBrush = new SolidBrush(Color.FromArgb(175, 175, 185)))
        using (var rightAlign = new StringFormat { Alignment = StringAlignment.Far })
        {
            g.DrawString(statsText, Font, statsBrush, new RectangleF(0, top + 6, ClientSize.Width - 16, 20), rightAlign);
        }

        float plotLeft = 12;
        float plotTop = top + 32;
        float plotRight = ClientSize.Width - 12;
        float plotBottom = top + height - 8;
        float plotWidth = plotRight - plotLeft;
        float plotHeight = plotBottom - plotTop;

        using (var gridPen = new Pen(Color.FromArgb(58, 58, 64)))
        {
            g.DrawLine(gridPen, plotLeft, plotTop, plotRight, plotTop);
            g.DrawLine(gridPen, plotLeft, plotTop + plotHeight / 2, plotRight, plotTop + plotHeight / 2);
            g.DrawLine(gridPen, plotLeft, plotBottom, plotRight, plotBottom);
        }

        if (values.Count >= 2)
        {
            var points = new PointF[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                float x = plotLeft + (float)i / (values.Count - 1) * plotWidth;
                float y = plotBottom - (float)Math.Clamp(values[i] / 100.0, 0.0, 1.0) * plotHeight;
                points[i] = new PointF(x, y);
            }

            var area = new PointF[values.Count + 2];
            Array.Copy(points, area, values.Count);
            area[values.Count] = new PointF(plotRight, plotBottom);
            area[values.Count + 1] = new PointF(plotLeft, plotBottom);
            using (var fillBrush = new SolidBrush(Color.FromArgb(36, lane.Color)))
            {
                g.FillPolygon(fillBrush, area);
            }

            using (var linePen = new Pen(lane.Color, 2f))
            {
                g.DrawLines(linePen, points);
            }
        }
        else if (values.Count == 1)
        {
            float y = plotBottom - (float)Math.Clamp(values[0] / 100.0, 0.0, 1.0) * plotHeight;
            using var dotBrush = new SolidBrush(lane.Color);
            g.FillEllipse(dotBrush, plotLeft - 3, y - 3, 6, 6);
        }

        if (top + height < ClientSize.Height)
        {
            using var divider = new Pen(Color.FromArgb(52, 52, 58));
            g.DrawLine(divider, 0, top + height - 1, ClientSize.Width, top + height - 1);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();
        base.OnFormClosed(e);
    }
}
