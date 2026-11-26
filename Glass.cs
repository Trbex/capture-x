using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.UI;
using SystemComposition = Windows.UI.Composition;

namespace RealCaptureX;

/// <summary>
/// Floating glass material: a transparent, borderless HWND whose system backdrop is a DWM host-backdrop
/// (real blur of whatever is behind the window) masked to an anti-aliased rounded rectangle. The XAML
/// tree draws the tint, rim and a pre-rendered nine-grid shadow, so radius and shadow are fully ours.
/// </summary>
internal sealed class GlassBackdrop : SystemBackdrop
{
    private static SystemComposition.Compositor? compositor;
    private readonly double inset;
    private double radius;
    private SystemComposition.CompositionRoundedRectangleGeometry? geometry;
    private SystemComposition.ShapeVisual? shape;
    private SystemComposition.CompositionVisualSurface? surface;
    private SystemComposition.CompositionColorBrush? fill;
    private XamlRoot? root;

    internal GlassBackdrop(double inset, double radius)
    {
        this.inset = inset;
        this.radius = radius;
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target, xamlRoot);
        try { Connect(target, xamlRoot); }
        catch (Exception ex)
        {
            // No host backdrop on this system: fall back to opaque glass tints instead of an unblurred see-through panel.
            Diagnostics.Log($"GlassBackdrop.Connect failed: {ex}");
            Appearance.BackdropUnavailable();
        }
    }

    private void Connect(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        SystemDispatcher.Ensure();
        compositor ??= new SystemComposition.Compositor();
        geometry = compositor.CreateRoundedRectangleGeometry();
        fill = compositor.CreateColorBrush(Color.FromArgb(255, 255, 255, 255));
        var sprite = compositor.CreateSpriteShape(geometry);
        sprite.FillBrush = fill;
        shape = compositor.CreateShapeVisual();
        shape.Shapes.Add(sprite);
        surface = compositor.CreateVisualSurface();
        surface.SourceVisual = shape;
        // Mask brushes reject backdrop sources, so the rounded mask is applied through an effect graph.
        var effect = new AlphaMaskEffect
        {
            Source = new SystemComposition.CompositionEffectSourceParameter("backdrop"),
            AlphaMask = new SystemComposition.CompositionEffectSourceParameter("mask")
        };
        var mask = compositor.CreateEffectFactory(effect).CreateBrush();
        mask.SetSourceParameter("backdrop", compositor.CreateHostBackdropBrush());
        mask.SetSourceParameter("mask", compositor.CreateSurfaceBrush(surface));
        target.SystemBackdrop = mask;
        root = xamlRoot;
        root.Changed += OnRootChanged;
        Layout();
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        base.OnTargetDisconnected(target);
        if (root is not null) root.Changed -= OnRootChanged;
        target.SystemBackdrop = null;
        surface?.Dispose();
        shape?.Dispose();
        geometry?.Dispose();
        fill?.Dispose();
        surface = null;
        shape = null;
        geometry = null;
        fill = null;
        root = null;
    }

    internal void SetRadius(double value)
    {
        radius = value;
        Layout();
    }

    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Layout();

    private void Layout()
    {
        if (root is null || geometry is null || shape is null || surface is null) return;
        var scale = (float)root.RasterizationScale;
        var size = new Vector2((float)root.Size.Width, (float)root.Size.Height) * scale;
        if (size.X <= 0 || size.Y <= 0) return;
        var margin = (float)inset * scale;
        shape.Size = size;
        surface.SourceSize = size;
        geometry.Offset = new Vector2(margin, margin);
        geometry.Size = Vector2.Max(Vector2.Zero, size - new Vector2(margin * 2));
        geometry.CornerRadius = new Vector2((float)radius * scale);
        shape.CenterPoint = new Vector3(size / 2, 0);
    }

    /// <summary>Brings the backdrop mask back to fully shown from its current (possibly mid-exit) values.</summary>
    internal void Settle(TimeSpan duration)
    {
        if (compositor is null || shape is null || fill is null) return;
        if (duration <= TimeSpan.Zero)
        {
            Animate(1, 1, 1, 1, TimeSpan.Zero);
            return;
        }
        var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(.16f, 1), new Vector2(.3f, 1));
        var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.InsertExpressionKeyFrame(0, "this.StartingValue");
        scale.InsertKeyFrame(1, Vector3.One, ease);
        scale.Duration = duration;
        shape.StartAnimation("Scale", scale);
        var move = compositor.CreateVector3KeyFrameAnimation();
        move.InsertExpressionKeyFrame(0, "this.StartingValue");
        move.InsertKeyFrame(1, Vector3.Zero, ease);
        move.Duration = duration;
        shape.StartAnimation("Offset", move);
        var color = compositor.CreateColorKeyFrameAnimation();
        color.InsertExpressionKeyFrame(0, "this.StartingValue");
        color.InsertKeyFrame(1, Color.FromArgb(255, 255, 255, 255), ease);
        color.Duration = duration;
        fill.StartAnimation("Color", color);
    }

    /// <summary>Animates the blurred backdrop together with the XAML panel so the glass never pops in.</summary>
    internal void Animate(float fromOpacity, float toOpacity, float fromScale, float toScale, TimeSpan duration,
        Vector2 fromOffset = default, Vector2 toOffset = default, bool accelerate = false)
    {
        if (compositor is null || shape is null || fill is null) return;
        var pixels = (float)(root?.RasterizationScale ?? 1);
        if (duration <= TimeSpan.Zero)
        {
            shape.StopAnimation("Scale");
            shape.StopAnimation("Offset");
            fill.StopAnimation("Color");
            shape.Scale = new Vector3(toScale, toScale, 1);
            shape.Offset = new Vector3(toOffset * pixels, 0);
            fill.Color = Color.FromArgb((byte)(255 * toOpacity), 255, 255, 255);
            return;
        }
        var ease = accelerate
            ? compositor.CreateCubicBezierEasingFunction(new Vector2(.4f, 0), new Vector2(1, 1))
            : compositor.CreateCubicBezierEasingFunction(new Vector2(.16f, 1), new Vector2(.3f, 1));
        var move = compositor.CreateVector3KeyFrameAnimation();
        move.InsertKeyFrame(0, new Vector3(fromOffset * pixels, 0));
        move.InsertKeyFrame(1, new Vector3(toOffset * pixels, 0), ease);
        move.Duration = duration;
        shape.StartAnimation("Offset", move);
        var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0, new Vector3(fromScale, fromScale, 1));
        scale.InsertKeyFrame(1, new Vector3(toScale, toScale, 1), ease);
        scale.Duration = duration;
        shape.StartAnimation("Scale", scale);
        var color = compositor.CreateColorKeyFrameAnimation();
        color.InsertKeyFrame(0, Color.FromArgb((byte)(255 * fromOpacity), 255, 255, 255));
        color.InsertKeyFrame(1, Color.FromArgb((byte)(255 * toOpacity), 255, 255, 255), ease);
        color.Duration = duration;
        fill.StartAnimation("Color", color);
    }
}

/// <summary>Creates the Windows.System dispatcher queue that the system compositor requires on the UI thread.</summary>
internal static class SystemDispatcher
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Options
    {
        public int Size, ThreadType, ApartmentType;
    }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(Options options, out nint controller);

    private static nint controller;

    internal static void Ensure()
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() is not null || controller != 0) return;
        Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(
            new Options { Size = Marshal.SizeOf<Options>(), ThreadType = 2, ApartmentType = 2 }, out controller));
    }
}

/// <summary>Soft two-layer shadow rendered once per radius into a nine-grid surface with a transparent centre.</summary>
internal static class GlassShadow
{
    private static CompositionGraphicsDevice? device;
    private static readonly Dictionary<(int Radius, int Margin, bool Light, int Scale), CompositionDrawingSurface> cache = [];

    internal static void Attach(Panel host, double radius, double margin)
    {
        var visual = ElementCompositionPreview.GetElementVisual(host);
        var compositor = visual.Compositor;
        var sprite = compositor.CreateSpriteVisual();
        sprite.RelativeSizeAdjustment = Vector2.One;
        ElementCompositionPreview.SetElementChildVisual(host, sprite);
        void Refresh()
        {
            try { Apply(); }
            catch (Exception ex) { Diagnostics.Log($"GlassShadow failed: {ex}"); }
        }
        void Apply()
        {
            var light = host.ActualTheme == ElementTheme.Light;
            var scale = host.XamlRoot?.RasterizationScale ?? 1;
            var surface = Surface(compositor, (int)Math.Round(radius), (int)Math.Round(margin), light, scale);
            var brush = compositor.CreateNineGridBrush();
            var surfaceBrush = compositor.CreateSurfaceBrush(surface);
            surfaceBrush.Stretch = CompositionStretch.Fill;
            brush.Source = surfaceBrush;
            var inner = (float)((margin + radius + 2) * scale);
            brush.SetInsets(inner);
            brush.SetInsetScales((float)(1 / scale));
            brush.IsCenterHollow = true;
            sprite.Brush = brush;
        }
        host.Loaded += (_, _) =>
        {
            Refresh();
            if (host.XamlRoot is not { } root) return;
            var dpi = root.RasterizationScale;
            root.Changed += (_, _) =>
            {
                if (Math.Abs(root.RasterizationScale - dpi) < .001) return;
                dpi = root.RasterizationScale;
                Refresh();
            };
        };
        host.ActualThemeChanged += (_, _) => Refresh();
    }

    private static CompositionDrawingSurface Surface(Compositor compositor, int radius, int margin, bool light, double scale)
    {
        var key = (radius, margin, light, (int)Math.Round(scale * 100));
        if (cache.TryGetValue(key, out var existing)) return existing;
        var canvas = CanvasDevice.GetSharedDevice();
        device ??= CanvasComposition.CreateCompositionGraphicsDevice(compositor, canvas);
        var dpi = (float)(96 * scale);
        var side = (margin + radius + 2) * 2 + 8;
        var pixels = (int)Math.Ceiling(side * scale);
        var surface = device.CreateDrawingSurface(new Windows.Foundation.Size(pixels, pixels),
            Microsoft.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized,
            Microsoft.Graphics.DirectX.DirectXAlphaMode.Premultiplied);
        using (var session = CanvasComposition.CreateDrawingSession(surface, new Windows.Foundation.Rect(0, 0, pixels, pixels), dpi))
        {
            session.Clear(Microsoft.UI.Colors.Transparent);
            var panel = new Windows.Foundation.Rect(margin, margin, side - margin * 2, side - margin * 2);
            Layer(session, panel, radius, blur: margin * 0.42f, offsetY: margin * 0.36f, light ? 0.20f : 0.46f);
            Layer(session, panel, radius, blur: 2.2f, offsetY: 1, light ? 0.10f : 0.32f);
            using var hole = CanvasGeometry.CreateRoundedRectangle(session, panel, radius, radius);
            session.Blend = CanvasBlend.Copy;
            session.FillGeometry(hole, Microsoft.UI.Colors.Transparent);
        }
        cache[key] = surface;
        return surface;

        static void Layer(CanvasDrawingSession session, Windows.Foundation.Rect panel, float radius, float blur, float offsetY, float alpha)
        {
            using var list = new CanvasCommandList(session);
            using (var drawing = list.CreateDrawingSession())
                drawing.FillRoundedRectangle(panel, radius, radius, Color.FromArgb((byte)(255 * alpha), 0, 0, 0));
            using var effect = new GaussianBlurEffect { Source = list, BlurAmount = blur, BorderMode = EffectBorderMode.Soft };
            session.DrawImage(effect, 0, offsetY);
        }
    }
}

/// <summary>
/// A floating window body: transparent HWND margin for the shadow, blurred backdrop, tinted rounded
/// panel with a hairline rim. Enter/exit animate the XAML panel and the system backdrop together.
/// </summary>
internal sealed class GlassSurface
{
    internal const double Margin = 22;
    internal Grid Root { get; }
    internal Border Panel { get; }
    internal GlassBackdrop Backdrop { get; }
    internal double Radius { get; }

    internal GlassSurface(Window window, FrameworkElement content, double radius = 14)
    {
        Radius = radius;
        NativeWindow.MakeTransparent(window);
        Backdrop = new GlassBackdrop(Margin, radius);
        window.SystemBackdrop = Backdrop;
        Root = new Grid();
        var shadow = new Grid { IsHitTestVisible = false };
        GlassShadow.Attach(shadow, radius, Margin);
        Root.Children.Add(shadow);
        Panel = new Border
        {
            Margin = new Thickness(Margin), CornerRadius = new CornerRadius(radius),
            Background = Appearance.GlassTint, BorderBrush = Appearance.GlassRim, BorderThickness = new Thickness(1),
            Child = content
        };
        Root.Children.Add(Panel);
        ProductMotion.Prepare(Panel);
        ProductMotion.Prepare(shadow);
        Shadow = shadow;
    }

    private Grid Shadow { get; }

    /// <summary>Outer window size (DIPs) for a panel of the given size.</summary>
    internal static int Outer(double panel) => (int)Math.Ceiling(panel + Margin * 2);

    /// <summary>Panel, shadow and blurred backdrop pop in together (fade, scale and an optional slide).</summary>
    internal void Enter(bool animate = true, float rise = 0, float slide = 0)
    {
        ProductMotion.Enter(Panel, animate, rise, slide: slide);
        ProductMotion.Enter(Shadow, animate, rise, slide: slide);
        var on = animate && ProductMotion.Enabled;
        Backdrop.Animate(0, 1, .965f, 1, on ? ProductMotion.Popup : TimeSpan.Zero, new Vector2(slide, rise), Vector2.Zero);
    }

    /// <summary>Show again while already visible (e.g. during an exit animation): settle everything from current values.</summary>
    internal void Settle()
    {
        ProductMotion.Settle(Panel);
        ProductMotion.Settle(Shadow);
        Backdrop.Settle(ProductMotion.Enabled ? ProductMotion.Hover : TimeSpan.Zero);
    }

    internal Task ExitAsync(float drop = 0, float slide = 0)
    {
        if (!ProductMotion.Enabled) return Task.CompletedTask;
        Backdrop.Animate(1, 0, 1, .975f, ProductMotion.Dismiss, Vector2.Zero, new Vector2(slide, drop), accelerate: true);
        _ = ProductMotion.ExitAsync(Shadow, drop: drop, slide: slide);
        return ProductMotion.ExitAsync(Panel, drop: drop, slide: slide);
    }
}
