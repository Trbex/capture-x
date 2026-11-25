using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using System.Numerics;

namespace RealCaptureX;

internal static class ProductMotion
{
    internal static void Enter(FrameworkElement element, bool enabled = true)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation("Opacity");
        visual.StopAnimation("Scale");
        visual.Opacity = 1;
        visual.Scale = Vector3.One;
        if (!enabled || !Appearance.AnimationsEnabled) return;
        var duration = TimeSpan.FromMilliseconds(168);
        Opacity(visual, .08f, 1, duration);
        Scale(visual, .976f, 1, duration, element);
    }

    internal static void Reveal(FrameworkElement element)
    {
        if (!Appearance.AnimationsEnabled) return;
        Opacity(ElementCompositionPreview.GetElementVisual(element), .46f, 1, TimeSpan.FromMilliseconds(142));
    }

    internal static Task ExitAsync(FrameworkElement element)
    {
        if (!Appearance.AnimationsEnabled) return Task.CompletedTask;
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var duration = TimeSpan.FromMilliseconds(106);
        Opacity(visual, 1, 0, duration);
        Scale(visual, 1, .984f, duration, element);
        return Task.Delay(duration);
    }

    private static void Opacity(Visual visual, float from, float to, TimeSpan duration)
    {
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(0, from);
        animation.InsertKeyFrame(1, to, Ease(visual.Compositor));
        animation.Duration = duration;
        visual.StartAnimation("Opacity", animation);
    }

    private static void Scale(Visual visual, float from, float to, TimeSpan duration, FrameworkElement element)
    {
        visual.CenterPoint = new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);
        var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.InsertKeyFrame(0, new Vector3(from, from, 1));
        animation.InsertKeyFrame(1, new Vector3(to, to, 1), Ease(visual.Compositor));
        animation.Duration = duration;
        visual.StartAnimation("Scale", animation);
    }

    private static CompositionEasingFunction Ease(Compositor compositor) =>
        compositor.CreateCubicBezierEasingFunction(new Vector2(.19f, .8f), new Vector2(.22f, 1));
}
