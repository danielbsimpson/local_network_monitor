using System.Globalization;
using System.Windows;
using System.Windows.Media;
using LocalNetworkMonitor.Core;

namespace LocalNetworkMonitor.App;

public sealed class TrafficChart : FrameworkElement
{
    public IReadOnlyList<RateInterval> Intervals { get; set; } = [];
    public DateTimeOffset Now { get; set; }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var width = Math.Max(0, ActualWidth - 80);
        var height = Math.Max(0, ActualHeight - 36);
        if (width == 0 || height == 0) return;
        var cutoff = Now - MeasurementHistory.Retention;
        var visible = Intervals.Where(i => i.End > cutoff && i.Start <= Now).ToArray();
        var maximum = Math.Max(1, visible.Select(i => Math.Max(i.DownloadBytesPerSecond, i.UploadBytesPerSecond)).DefaultIfEmpty().Max());
        drawing.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(60, 8, width, height));
        foreach (var interval in visible)
        {
            var left = 60 + Math.Clamp((interval.Start - cutoff).TotalSeconds / 600, 0, 1) * width;
            var right = 60 + Math.Clamp((interval.End - cutoff).TotalSeconds / 600, 0, 1) * width;
            DrawRate(interval.DownloadBytesPerSecond, Brushes.RoyalBlue);
            DrawRate(interval.UploadBytesPerSecond, Brushes.DarkOrange);
            void DrawRate(double rate, Brush color)
            {
                var y = 8 + height * (1 - rate / maximum);
                drawing.DrawLine(new Pen(color, 2), new(left, y), new(right, y));
            }
        }
        Label($"{maximum * 8 / 1_000_000:0.###}", 0, 0);
        Label("0 Mbps", 0, height - 8);
        Label("-10 min", 60, height + 10);
        Label("now", 60 + width - 28, height + 10);
        if (visible.Length == 0) Label("No valid intervals yet", 80, 35);
        void Label(string text, double x, double y) => drawing.DrawText(
            new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 11, Brushes.DimGray, VisualTreeHelper.GetDpi(this).PixelsPerDip),
            new(x, y));
    }
}
