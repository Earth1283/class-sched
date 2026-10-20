using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ClassSchedule.Core;

namespace ClassSchedule.Views;

public sealed class EventColumnPanel : Panel
{
    public static readonly DependencyProperty StartProperty = DependencyProperty.RegisterAttached(
        "Start", typeof(double), typeof(EventColumnPanel), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static readonly DependencyProperty EndProperty = DependencyProperty.RegisterAttached(
        "End", typeof(double), typeof(EventColumnPanel), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static double GetStart(DependencyObject d) => (double)d.GetValue(StartProperty);
    public static void SetStart(DependencyObject d, double v) => d.SetValue(StartProperty, v);
    public static double GetEnd(DependencyObject d) => (double)d.GetValue(EndProperty);
    public static void SetEnd(DependencyObject d, double v) => d.SetValue(EndProperty, v);

    public double HourHeight { get; set; } = 56;
    public double OriginMinutes { get; set; }
    public double TotalMinutes { get; set; } = 540;

    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? 0 : available.Width;
        Place(width, arrange: false);
        return new Size(width, TotalMinutes * HourHeight / 60);
    }

    protected override Size ArrangeOverride(Size final)
    {
        Place(final.Width, arrange: true);
        return final;
    }

    private void Place(double width, bool arrange)
    {
        var items = InternalChildren.Cast<UIElement>().ToList();
        var slots = OverlapLayout.Assign(items.Select(c => (GetStart(c), GetEnd(c))).ToList());

        for (var i = 0; i < items.Count; i++)
        {
            var laneWidth = width / slots[i].Lanes;
            var top = (GetStart(items[i]) - OriginMinutes) * HourHeight / 60;
            var height = Math.Max(20, (GetEnd(items[i]) - GetStart(items[i])) * HourHeight / 60);
            var rect = new Rect(slots[i].Lane * laneWidth, top, laneWidth, height);
            if (arrange) items[i].Arrange(rect);
            else items[i].Measure(rect.Size);
        }
    }
}
