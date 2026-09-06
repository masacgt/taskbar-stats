using System.Drawing;

namespace TaskbarStats.UI;

/// <summary>
/// タスクバー上の右端に使用率を表示する小さな常時ラベル。
/// 非アクティブ + ツールウィンドウなのでタスクバー/Alt-Tabに出ない。
/// </summary>
public sealed class StatsLabel : Form
{
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;

    private const int MarginRight = 12;
    private const int MarginBottom = 6;
    private const int LabelHeight = 22;
    private const int PaddingLeft = 8;
    private const int PaddingRight = 8;

    private string _text = "CPU  --%  RAM  --%  GPU  --%  VRAM  --%  NET   --Mbps";

    public StatsLabel()
    {
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = Color.FromArgb(12, 12, 12);
        ForeColor = Color.FromArgb(240, 240, 240);
        Font = new Font("Consolas", 8.25f, FontStyle.Bold);
        Text = _text;
        Size = Measure(_text);
        Location = ComputeLocation();
        DoubleBuffered = true;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExNoActivate | WsExToolWindow;
            return cp;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        TextRenderer.DrawText(
            e.Graphics,
            _text,
            Font,
            ClientRectangle,
            ForeColor,
            TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Utils.CrashLog.Write($"label shown: location={Location}, size={Size}", null);
    }

    public void UpdateText(string text)
    {
        try
        {
            _text = text;
            Text = text;
            Size = Measure(_text);
            Location = ComputeLocation();
            Invalidate();
        }
        catch (Exception ex)
        {
            Utils.CrashLog.Write("StatsLabel.UpdateText", ex);
        }
    }

    private Point ComputeLocation()
    {
        var wa = SystemInformation.WorkingArea;
        return new Point(wa.Right - Width - MarginRight, wa.Bottom - LabelHeight - MarginBottom);
    }

    private Size Measure(string text)
    {
        var sz = TextRenderer.MeasureText(text, Font, new Size(0, 0), TextFormatFlags.NoPadding);
        return new Size(sz.Width + PaddingLeft + PaddingRight, LabelHeight);
    }
}
