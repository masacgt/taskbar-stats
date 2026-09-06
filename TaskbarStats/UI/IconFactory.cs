using System.Drawing.Drawing2D;

namespace TaskbarStats.UI;

public static class IconFactory
{
    public static Color GetColor(LoadLevel level) => level switch
    {
        LoadLevel.Critical => Color.FromArgb(229, 57, 53),
        LoadLevel.Warning => Color.FromArgb(251, 192, 45),
        _ => Color.FromArgb(82, 196, 119),
    };

    public static Icon Create(LoadLevel level, int maxPercent)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            using (var background = new SolidBrush(Color.FromArgb(38, 38, 42)))
            {
                graphics.FillEllipse(background, 1, 1, 30, 30);
            }

            int barHeight = Math.Max(2, (int)Math.Round(22 * Math.Clamp(maxPercent, 0, 100) / 100.0));
            using (var bar = new SolidBrush(GetColor(level)))
            {
                graphics.FillRectangle(bar, 11, 27 - barHeight, 10, barHeight);
            }

            using var outline = new Pen(Color.FromArgb(110, 110, 118), 1.5f);
            graphics.DrawEllipse(outline, 1, 1, 30, 30);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }
}
