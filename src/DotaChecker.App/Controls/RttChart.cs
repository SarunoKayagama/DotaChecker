using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DotaChecker.App.Controls;

/// <summary>Лёгкий график RTT/потерь без внешних библиотек.</summary>
public sealed class RttChart : Control
{
    public static readonly DependencyProperty RttProperty = DependencyProperty.Register(
        nameof(Rtt),
        typeof(IReadOnlyList<double>),
        typeof(RttChart),
        new PropertyMetadata(null, OnDataChanged));

    public static readonly DependencyProperty SuccessProperty = DependencyProperty.Register(
        nameof(Success),
        typeof(IReadOnlyList<bool>),
        typeof(RttChart),
        new PropertyMetadata(null, OnDataChanged));

    public IReadOnlyList<double>? Rtt
    {
        get => (IReadOnlyList<double>?)GetValue(RttProperty);
        set => SetValue(RttProperty, value);
    }

    public IReadOnlyList<bool>? Success
    {
        get => (IReadOnlyList<bool>?)GetValue(SuccessProperty);
        set => SetValue(SuccessProperty, value);
    }

    private static void OnDataChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((RttChart)sender).InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var background = new SolidColorBrush(Color.FromArgb(20, 128, 128, 128));
        var grid = new Pen(new SolidColorBrush(Color.FromArgb(45, 128, 128, 128)), 1);
        drawingContext.DrawRectangle(background, null, new Rect(0, 0, width, height));

        for (var i = 1; i < 4; i++)
        {
            var y = height * i / 4;
            drawingContext.DrawLine(grid, new Point(0, y), new Point(width, y));
        }

        var values = Rtt;
        if (values is null || values.Count == 0)
        {
            return;
        }

        var success = Success;
        var max = Math.Max(10, values.Where(value => !double.IsNaN(value)).DefaultIfEmpty(0).Max() * 1.15);
        var stepX = width / Math.Max(1, values.Count - 1);
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(64, 170, 255)), 1.5);
        var lossBrush = new SolidColorBrush(Color.FromRgb(230, 90, 90));

        Point? previous = null;
        for (var i = 0; i < values.Count; i++)
        {
            var x = i * stepX;
            var isSuccess = success is null || i >= success.Count || success[i];

            if (isSuccess && !double.IsNaN(values[i]))
            {
                var y = height - values[i] / max * height;
                var current = new Point(x, y);
                if (previous.HasValue)
                {
                    drawingContext.DrawLine(pen, previous.Value, current);
                }

                previous = current;
            }
            else
            {
                previous = null;
                drawingContext.DrawRectangle(lossBrush, null, new Rect(x - 1, height - 3, 2, 3));
            }
        }
    }
}
