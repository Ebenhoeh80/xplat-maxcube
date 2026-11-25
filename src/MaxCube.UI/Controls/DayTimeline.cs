using System.Collections.Specialized;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MaxCube.UI.ViewModels;

namespace MaxCube.UI.Controls;

/// <summary>
/// Grafische Tagesleiste des Wochenprogramms (weeklyProgramController.js): farbige Abschnitte von
/// 00:00 bis 24:00 mit Temperatur und Stundenmarken. Reine Darstellung; die Bearbeitung läuft
/// über die Schaltpunktliste, damit die gesperrten Pfade des ViewModels unverändert gelten.
/// </summary>
public sealed class DayTimeline : Control
{
    public static readonly StyledProperty<IEnumerable<TimelineSegment>?> SegmentsProperty =
        AvaloniaProperty.Register<DayTimeline, IEnumerable<TimelineSegment>?>(nameof(Segments));

    private INotifyCollectionChanged? _observed;

    static DayTimeline()
    {
        AffectsRender<DayTimeline>(SegmentsProperty);
    }

    public DayTimeline() => Height = 54;

    public IEnumerable<TimelineSegment>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != SegmentsProperty)
        {
            return;
        }

        if (_observed is not null)
        {
            _observed.CollectionChanged -= OnSegmentsChanged;
        }

        _observed = change.NewValue as INotifyCollectionChanged;
        if (_observed is not null)
        {
            _observed.CollectionChanged += OnSegmentsChanged;
        }
    }

    private void OnSegmentsChanged(object? sender, NotifyCollectionChangedEventArgs args) => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Bounds.Width;
        var barHeight = Math.Max(10, Bounds.Height - 18);
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#22888888")), null, new Rect(0, 0, width, barHeight));

        foreach (var segment in Segments ?? [])
        {
            var rect = new Rect(segment.Start * width, 0, Math.Max(1, segment.Width * width), barHeight);
            context.DrawRectangle(new SolidColorBrush(Color.Parse(segment.Color)), null, rect);
            if (rect.Width >= 38)
            {
                var label = Text($"{segment.Temperature:0.0}°", 12, Brushes.Black);
                context.DrawText(label, new Point(rect.X + ((rect.Width - label.Width) / 2), (barHeight - label.Height) / 2));
            }
        }

        var tick = new Pen(Brushes.Gray, 1);
        for (var hour = 0; hour <= 24; hour += 6)
        {
            var x = Math.Min(width - 1, hour / 24.0 * width);
            context.DrawLine(tick, new Point(x, barHeight), new Point(x, barHeight + 4));
            var label = Text($"{hour:00}", 11, Brushes.Gray);
            var labelX = hour == 24 ? width - label.Width : hour == 0 ? 0 : x - (label.Width / 2);
            context.DrawText(label, new Point(labelX, barHeight + 3));
        }
    }

    private static FormattedText Text(string text, double size, IBrush brush)
        => new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, size, brush);
}
