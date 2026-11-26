using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using System.Numerics;

namespace RealCaptureX;

/// <summary>
/// Shared motion system. Every animation runs on the composition thread, starts from the current
/// presented value (so it can be interrupted and reversed without snapping) and collapses to an
/// instant change when Windows "Animation effects" is off.
/// </summary>
internal static class ProductMotion
{
#if DEBUG
    /// <summary>QA slow motion (REAL_CAPTURE_X_SLOWMO=n) so transitions can be inspected frame by frame.</summary>
    private static readonly double slowmo = double.TryParse(Environment.GetEnvironmentVariable("REAL_CAPTURE_X_SLOWMO"),
        System.Globalization.CultureInfo.InvariantCulture, out var factor) && factor > 0 ? factor : 1;
#else
    private const double slowmo = 1;
#endif

    private static TimeSpan Slow(double milliseconds) => TimeSpan.FromMilliseconds(milliseconds * slowmo);

    internal static readonly TimeSpan Hover = Slow(130);
    internal static readonly TimeSpan Press = Slow(90);
    internal static readonly TimeSpan Select = Slow(220);
    internal static readonly TimeSpan Popup = Slow(230);
    internal static readonly TimeSpan Panel = Slow(260);
    internal static readonly TimeSpan Page = Slow(260);
    internal static readonly TimeSpan Dismiss = Slow(140);


    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement, Visual> pivots = new();

    internal static bool Enabled => Appearance.AnimationsEnabled;

    /// <summary>Pop in: opacity 0 → 1, scale .965 → 1 on a critically-damped spring, optional rise.</summary>
    internal static void Enter(FrameworkElement element, bool enabled = true, float rise = 0, float fromScale = .965f, float slide = 0)
    {
        var visual = Prepare(element);
        if (!enabled || !Enabled)
        {
            Stop(visual);
            visual.Opacity = 1;
            visual.Scale = Vector3.One;
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
            return;
        }
        visual.Opacity = 0;
        visual.Scale = new Vector3(fromScale, fromScale, 1);
        visual.Properties.InsertVector3("Translation", new Vector3(slide, rise, 0));
        Fade(visual, 1, Popup);
        Spring(visual, "Scale", Vector3.One, .78f, 46);
        if (rise != 0 || slide != 0) Spring(visual, "Translation", Vector3.Zero, .82f, 46);
    }

    /// <summary>Returns an element to its resting state from wherever an interrupted animation left it.</summary>
    internal static void Settle(FrameworkElement element)
    {
        var visual = Prepare(element);
        if (!Enabled)
        {
            Stop(visual);
            visual.Opacity = 1;
            visual.Scale = Vector3.One;
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
            return;
        }
        Fade(visual, 1, Hover);
        Spring(visual, "Scale", Vector3.One, .8f, 40);
        Spring(visual, "Translation", Vector3.Zero, .85f, 40);
    }

    /// <summary>Quick fade/scale out. Safe to interrupt with <see cref="Enter"/>.</summary>
    internal static Task ExitAsync(FrameworkElement element, float toScale = .975f, float drop = 0, float slide = 0)
    {
        if (!Enabled) return Task.CompletedTask;
        var visual = Prepare(element);
        Fade(visual, 0, Dismiss, accelerate: true);
        Ease(visual, "Scale", new Vector3(toScale, toScale, 1), Dismiss, accelerate: true);
        if (drop != 0 || slide != 0) Ease(visual, "Translation", new Vector3(slide, drop, 0), Dismiss, accelerate: true);
        return Task.Delay(Dismiss);
    }

    /// <summary>Content swap inside a stable container: fade in with a short slide.</summary>
    internal static void Reveal(FrameworkElement element, float slide = 0)
    {
        var visual = Prepare(element);
        if (!Enabled)
        {
            visual.Opacity = 1;
            return;
        }
        visual.Opacity = 0;
        Fade(visual, 1, Page);
        if (slide == 0) return;
        visual.Properties.InsertVector3("Translation", new Vector3(slide, 0, 0));
        Ease(visual, "Translation", Vector3.Zero, Page);
    }

    /// <summary>Slides a selection indicator to a new offset with a spring (interruptible).</summary>
    internal static void MoveTo(FrameworkElement element, Vector2 offset, bool animate = true)
    {
        var visual = Prepare(element);
        var target = new Vector3(offset, 0);
        if (!animate || !Enabled)
        {
            visual.StopAnimation("Translation");
            visual.Properties.InsertVector3("Translation", target);
            return;
        }
        Spring(visual, "Translation", target, .86f, 40);
    }

    /// <summary>Cascading entrance for list rows: each fades and rises a few pixels after the previous one.</summary>
    internal static void Stagger(IReadOnlyList<FrameworkElement> elements, TimeSpan step, float rise)
    {
        for (var index = 0; index < elements.Count; index++)
        {
            var visual = Prepare(elements[index]);
            if (!Enabled)
            {
                Stop(visual);
                visual.Opacity = 1;
                visual.Properties.InsertVector3("Translation", Vector3.Zero);
                continue;
            }
            var compositor = visual.Compositor;
            var delay = step * Math.Min(index, 9);
            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0, 0);
            fade.InsertKeyFrame(1, 1, Curve(compositor));
            fade.Duration = Slow(180);
            fade.DelayTime = delay;
            fade.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            visual.StartAnimation("Opacity", fade);
            var slide = compositor.CreateVector3KeyFrameAnimation();
            slide.InsertKeyFrame(0, new Vector3(0, rise, 0));
            slide.InsertKeyFrame(1, Vector3.Zero, Curve(compositor));
            slide.Duration = Slow(200);
            slide.DelayTime = delay;
            slide.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            visual.StartAnimation("Translation", slide);
        }
    }

    /// <summary>
    /// Starts an element invisible at the composition level. Never use XAML Opacity = 0 for elements that
    /// later fade in through composition: the two opacities multiply, so the fade would stay invisible.
    /// </summary>
    internal static void Hide(FrameworkElement element) => Prepare(element).Opacity = 0;

    internal static void Opacity(FrameworkElement element, float to, TimeSpan duration)
    {
        var visual = Prepare(element);
        if (!Enabled) { visual.StopAnimation("Opacity"); visual.Opacity = to; return; }
        Fade(visual, to, duration);
    }

    internal static Visual Prepare(FrameworkElement element)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        if (element.ActualWidth <= 0 && element.IsLoaded) element.UpdateLayout();
        visual.CenterPoint = new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);
        if (!pivots.TryGetValue(element, out _))
        {
            pivots.Add(element, visual);
            element.SizeChanged += (_, e) =>
                visual.CenterPoint = new Vector3((float)e.NewSize.Width / 2, (float)e.NewSize.Height / 2, 0);
        }
        return visual;
    }

    private static void Stop(Visual visual)
    {
        visual.StopAnimation("Opacity");
        visual.StopAnimation("Scale");
        visual.StopAnimation("Translation");
    }

    private static void Fade(Visual visual, float to, TimeSpan duration, bool accelerate = false)
    {
        var compositor = visual.Compositor;
        var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.InsertExpressionKeyFrame(0, "this.StartingValue");
        animation.InsertKeyFrame(1, to, Curve(compositor, accelerate));
        animation.Duration = duration;
        visual.StartAnimation("Opacity", animation);
    }

    private static void Ease(Visual visual, string property, Vector3 to, TimeSpan duration, bool accelerate = false)
    {
        var compositor = visual.Compositor;
        var animation = compositor.CreateVector3KeyFrameAnimation();
        animation.InsertExpressionKeyFrame(0, "this.StartingValue");
        animation.InsertKeyFrame(1, to, Curve(compositor, accelerate));
        animation.Duration = duration;
        visual.StartAnimation(property, animation);
    }

    private static void Spring(Visual visual, string property, Vector3 to, float damping, int periodMs)
    {
        var spring = visual.Compositor.CreateSpringVector3Animation();
        spring.FinalValue = to;
        spring.DampingRatio = damping;
        spring.Period = Slow(periodMs);
        spring.StopBehavior = AnimationStopBehavior.SetToFinalValue;
        visual.StartAnimation(property, spring);
    }

    /// <summary>Ease-out (decelerate) for entering, ease-in for leaving.</summary>
    internal static CompositionEasingFunction Curve(Compositor compositor, bool accelerate = false) => accelerate
        ? compositor.CreateCubicBezierEasingFunction(new Vector2(.4f, 0), new Vector2(1, 1))
        : compositor.CreateCubicBezierEasingFunction(new Vector2(.16f, 1), new Vector2(.3f, 1));
}
