using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace ClassSchedule.Views;

/// <summary>Small, short animations. Everything here is skipped when animations are off in the app or in Windows.</summary>
public static class Motion
{
    public static bool Enabled { get; set; } = true;

    public static bool Active => Enabled && SystemParameters.ClientAreaAnimation;

    public static readonly IEasingFunction Out = new CubicEase { EasingMode = EasingMode.EaseOut };
    public static readonly IEasingFunction Spring = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 };
    public static readonly IEasingFunction Settle = new QuinticEase { EasingMode = EasingMode.EaseOut };

    private sealed record Parts(ScaleTransform Scale, TranslateTransform Move);

    private static readonly ConditionalWeakTable<UIElement, Parts> Table = new();

    private static Parts Of(UIElement element, Point origin)
    {
        if (Table.TryGetValue(element, out var existing)) return existing;
        var parts = new Parts(new ScaleTransform(1, 1), new TranslateTransform());
        var group = new TransformGroup();
        group.Children.Add(parts.Scale);
        group.Children.Add(parts.Move);
        element.RenderTransform = group;
        element.RenderTransformOrigin = origin;
        Table.Add(element, parts);
        return parts;
    }

    private static void Run(IAnimatable target, DependencyProperty property, double from, double to, int ms, int delay, IEasingFunction ease)
    {
        var owner = (DependencyObject)target;
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
        {
            BeginTime = TimeSpan.FromMilliseconds(delay),
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd
        };
        animation.Completed += (_, _) =>
        {
            owner.SetValue(property, to);
            target.BeginAnimation(property, null);
        };
        if (delay > 0) owner.SetValue(property, from);
        target.BeginAnimation(property, animation);
    }

    private static void RunTo(IAnimatable target, DependencyProperty property, double to, int ms, IEasingFunction ease)
    {
        var owner = (DependencyObject)target;
        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };
        animation.Completed += (_, _) =>
        {
            owner.SetValue(property, to);
            target.BeginAnimation(property, null);
        };
        target.BeginAnimation(property, animation);
    }

    /// <summary>Fades in while easing up from a few pixels below, optionally growing from a slightly smaller size.</summary>
    public static void Rise(UIElement element, double dy, int ms, int delay = 0, IEasingFunction? ease = null, double fromScale = 1)
    {
        if (!Active) return;
        var t = Of(element, new Point(0.5, 0.5));
        Run(element, UIElement.OpacityProperty, 0, 1, ms, delay, Out);
        if (dy != 0) Run(t.Move, TranslateTransform.YProperty, dy, 0, ms, delay, ease ?? Out);
        if (fromScale != 1)
        {
            Run(t.Scale, ScaleTransform.ScaleXProperty, fromScale, 1, ms, delay, ease ?? Out);
            Run(t.Scale, ScaleTransform.ScaleYProperty, fromScale, 1, ms, delay, ease ?? Out);
        }
    }

    public static void SlideX(UIElement element, double dx, int ms)
    {
        if (!Active) return;
        var t = Of(element, new Point(0.5, 0.5));
        Run(element, UIElement.OpacityProperty, 0, 1, ms, 0, Out);
        if (dx != 0) Run(t.Move, TranslateTransform.XProperty, dx, 0, ms, 0, Settle);
    }

    /// <summary>Clears any running or held animation and puts the element back to rest.</summary>
    public static void Reset(UIElement element)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1;
        element.IsHitTestVisible = true;
        if (!Table.TryGetValue(element, out var t)) return;
        foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty }) t.Scale.BeginAnimation(property, null);
        t.Move.BeginAnimation(TranslateTransform.XProperty, null);
        t.Move.BeginAnimation(TranslateTransform.YProperty, null);
        t.Scale.ScaleX = t.Scale.ScaleY = 1;
        t.Move.X = t.Move.Y = 0;
    }

    public static void Fade(UIElement element, int ms) => Run(element, UIElement.OpacityProperty, 0, 1, ms, 0, Out);

    public static void PopIn(FrameworkElement element)
    {
        if (!Active) return;
        var t = Of(element, new Point(0, 0));
        Run(element, UIElement.OpacityProperty, 0, 1, 140, 0, Out);
        Run(t.Scale, ScaleTransform.ScaleXProperty, 0.94, 1, 170, 0, Out);
        Run(t.Scale, ScaleTransform.ScaleYProperty, 0.94, 1, 170, 0, Out);
        Run(t.Move, TranslateTransform.YProperty, -4, 0, 170, 0, Out);
    }

    public static void Pop(UIElement element, double from = 0.7, int ms = 320)
    {
        if (!Active) return;
        var t = Of(element, new Point(0.5, 0.5));
        Run(t.Scale, ScaleTransform.ScaleXProperty, from, 1, ms, 0, Spring);
        Run(t.Scale, ScaleTransform.ScaleYProperty, from, 1, ms, 0, Spring);
    }

    /// <summary>A single soft ripple, used on the now-line dot.</summary>
    public static void Ping(UIElement element, int delay = 300, int ms = 1600)
    {
        if (!Active) return;
        var t = Of(element, new Point(0.5, 0.5));
        Run(element, UIElement.OpacityProperty, 0.55, 0, ms, delay, Out);
        Run(t.Scale, ScaleTransform.ScaleXProperty, 1, 2.8, ms, delay, Out);
        Run(t.Scale, ScaleTransform.ScaleYProperty, 1, 2.8, ms, delay, Out);
    }

    public static void FadeOut(UIElement element, int ms, Action then)
    {
        if (!Active)
        {
            then();
            return;
        }
        var t = Of(element, new Point(0.5, 0.5));
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Out, FillBehavior = FillBehavior.HoldEnd };
        fade.Completed += (_, _) => then();
        element.IsHitTestVisible = false;
        element.BeginAnimation(UIElement.OpacityProperty, fade);
        RunTo(t.Scale, ScaleTransform.ScaleXProperty, 0.9, ms, Out);
        RunTo(t.Scale, ScaleTransform.ScaleYProperty, 0.9, ms, Out);
    }

    /// <summary>Hover lift and press-down for a card.</summary>
    public static void AttachLift(FrameworkElement card)
    {
        var t = Of(card, new Point(0.5, 0.5));
        var hovering = false;

        card.MouseEnter += (_, _) =>
        {
            hovering = true;
            if (!Active) return;
            Panel.SetZIndex(card, 5);
            card.Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 4, Direction = 270, Opacity = 0.22, Color = Colors.Black };
            RunTo(t.Scale, ScaleTransform.ScaleXProperty, 1.012, 140, Out);
            RunTo(t.Scale, ScaleTransform.ScaleYProperty, 1.012, 140, Out);
            RunTo(t.Move, TranslateTransform.YProperty, -1, 140, Out);
        };
        card.MouseLeave += (_, _) =>
        {
            hovering = false;
            Panel.SetZIndex(card, 0);
            card.Effect = null;
            if (!Active) return;
            RunTo(t.Scale, ScaleTransform.ScaleXProperty, 1, 160, Out);
            RunTo(t.Scale, ScaleTransform.ScaleYProperty, 1, 160, Out);
            RunTo(t.Move, TranslateTransform.YProperty, 0, 160, Out);
        };
        card.PreviewMouseLeftButtonDown += (_, _) =>
        {
            if (!Active) return;
            RunTo(t.Scale, ScaleTransform.ScaleXProperty, 0.985, 70, Out);
            RunTo(t.Scale, ScaleTransform.ScaleYProperty, 0.985, 70, Out);
        };
        card.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (!Active) return;
            var rest = hovering ? 1.012 : 1;
            RunTo(t.Scale, ScaleTransform.ScaleXProperty, rest, 160, Out);
            RunTo(t.Scale, ScaleTransform.ScaleYProperty, rest, 160, Out);
        };
    }
}

/// <summary>Attach with <c>PressMotion.Enabled</c> in a style to make a button sink slightly while pressed.</summary>
public static class PressMotion
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(PressMotion), new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject d, bool value) => d.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ButtonBase button || e.NewValue is not true) return;

        var scale = new ScaleTransform(1, 1);
        button.RenderTransformOrigin = new Point(0.5, 0.5);
        button.RenderTransform = scale;

        void To(double value, int ms, IEasingFunction ease)
        {
            if (!Motion.Active) return;
            foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
            {
                var animation = new DoubleAnimation(value, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };
                animation.Completed += (_, _) =>
                {
                    scale.SetValue(property, value);
                    scale.BeginAnimation(property, null);
                };
                scale.BeginAnimation(property, animation);
            }
        }

        button.PreviewMouseLeftButtonDown += (_, _) => To(0.96, 70, Motion.Out);
        button.PreviewMouseLeftButtonUp += (_, _) => To(1, 220, Motion.Spring);
        button.MouseLeave += (_, _) => { if (scale.ScaleX != 1) To(1, 160, Motion.Out); };
    }
}
