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
    private SystemComposition.ContainerVisual? container;
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
        IsReady = true;
        var waiting = ready;
        ready = null;
        waiting?.Invoke();
    }

    private Action? ready;

    /// <summary>
    /// True once WinUI has connected the backdrop (or found it unavailable). That happens asynchronously after a
    /// window is first shown, so the very first entrance has to wait for it or the blur would sit still at its
    /// final place while the panel animates in.
    /// </summary>
    internal bool IsReady { get; private set; }

    internal void WhenReady(Action action)
    {
        if (IsReady) action();
        else ready += action;
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
        // A visual surface renders its source visual as the root and ignores that root's own Offset and Scale
        // (measured: the blur stayed put while the panel slid). The animated shape therefore lives one level down.
        container = compositor.CreateContainerVisual();
        container.Children.InsertAtTop(shape);
        surface = compositor.CreateVisualSurface();
        surface.SourceVisual = container;
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
        container?.Dispose();
        shape?.Dispose();
        geometry?.Dispose();
        fill?.Dispose();
        surface = null;
        container = null;
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

    /// <summary>
    /// WinUI calls this when the theme or window activation changes. The base implementation is meant for
    /// MicaController/DesktopAcrylicController configurations and throws E_INVALIDARG for a custom brush,
    /// which crashed the app on a theme switch. The glass tint follows the theme in XAML, so nothing to do here.
    /// </summary>
    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
    }

    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Layout();

    private void Layout()
    {
        if (root is null || geometry is null || shape is null || surface is null || container is null) return;
        var scale = (float)root.RasterizationScale;
        var size = new Vector2((float)root.Size.Width, (float)root.Size.Height) * scale;
        if (size.X <= 0 || size.Y <= 0) return;
        var margin = (float)inset * scale;
        shape.Size = size;
        container.Size = size;
        surface.SourceSize = size;
        geometry.Offset = new Vector2(margin, margin);
        geometry.Size = Vector2.Max(Vector2.Zero, size - new Vector2(margin * 2));
        geometry.CornerRadius = new Vector2((float)radius * scale);
        var panel = Vector2.Max(Vector2.Zero, size - new Vector2(margin * 2));
        shape.CenterPoint = new Vector3(new Vector2(margin) + panel * origin, 0);
    }

    private Vector2 origin = new(.5f, .5f);

    /// <summary>Normalized point of the panel the blurred backdrop scales from (matches the XAML panel's origin).</summary>
    internal void SetOrigin(Vector2 value)
    {
        origin = value;
        Layout();
    }

    private float Pixels => (float)(root?.RasterizationScale ?? 1);

    /// <summary>
    /// Moves and scales the blurred backdrop on the same spring as the XAML panel (offset in DIPs), starting from
    /// its current on-screen value and velocity, so the blur never drifts from the panel or snaps when interrupted.
    /// </summary>
    internal void Spring(Vector2 offset, float scale, SpringSpec spec, Vector2? fromOffset = null, float? fromScale = null,
        Vector2? velocity = null)
    {
        if (compositor is null || shape is null) return;
        var pixels = Pixels;
        var scaled = ProductMotion.Scaled(spec);
        Start("Offset", new Vector3(offset * pixels, 0), fromOffset is { } a ? new Vector3(a * pixels, 0) : null,
            velocity is { } v ? new Vector3(v * pixels, 0) : null);
        Start("Scale", new Vector3(scale, scale, 1), fromScale is { } b ? new Vector3(b, b, 1) : null, null);

        void Start(string property, Vector3 to, Vector3? from, Vector3? handoff)
        {
            var initial = ProductMotion.Retarget(shape!, property, to, scaled, from, handoff);
            var spring = compositor!.CreateSpringVector3Animation();
            spring.FinalValue = to;
            if (from is { } start) spring.InitialValue = start;
            spring.InitialVelocity = initial;
            spring.DampingRatio = spec.Damping;
            spring.Period = scaled.CompositionPeriod;
            spring.StopBehavior = SystemComposition.AnimationStopBehavior.SetToFinalValue;
            shape!.StartAnimation(property, spring);
        }
    }

    /// <summary>Places the backdrop without motion (gesture tracking, reduced motion).</summary>
    internal void Snap(Vector2 offset, float scale)
    {
        if (shape is null) return;
        var pixels = Pixels;
        shape.StopAnimation("Offset");
        shape.StopAnimation("Scale");
        shape.Offset = new Vector3(offset * pixels, 0);
        shape.Scale = new Vector3(scale, scale, 1);
        ProductMotion.Retarget(shape, "Offset", shape.Offset, SpringSpec.Default, shape.Offset, Vector3.Zero);
        ProductMotion.Retarget(shape, "Scale", shape.Scale, SpringSpec.Default, shape.Scale, Vector3.Zero);
    }

    /// <summary>Fades the blur in or out with the panel's opacity curve, from its current value.</summary>
    internal void Fade(float opacity, TimeSpan duration, bool accelerate = false, float? from = null)
    {
        if (compositor is null || fill is null) return;
        var color = Color.FromArgb((byte)Math.Round(255 * Math.Clamp(opacity, 0, 1)), 255, 255, 255);
        if (duration <= TimeSpan.Zero)
        {
            fill.StopAnimation("Color");
            fill.Color = color;
            return;
        }
        var ease = accelerate
            ? compositor.CreateCubicBezierEasingFunction(new Vector2(.4f, 0), new Vector2(1, 1))
            : compositor.CreateCubicBezierEasingFunction(new Vector2(.16f, 1), new Vector2(.3f, 1));
        var animation = compositor.CreateColorKeyFrameAnimation();
        if (from is { } start) animation.InsertKeyFrame(0, Color.FromArgb((byte)Math.Round(255 * start), 255, 255, 255));
        else animation.InsertExpressionKeyFrame(0, "this.StartingValue");
        animation.InsertKeyFrame(1, color, ease);
        animation.Duration = duration;
        fill.StartAnimation("Color", animation);
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
/// panel with a hairline rim. Panel, shadow and backdrop always move as one object: same spring, same
/// origin, same starting point, so the material reads as a single physical surface.
/// </summary>
internal sealed class GlassSurface
{
    internal const double Margin = 22;
    internal Grid Root { get; }
    internal Border Panel { get; }
    internal GlassBackdrop Backdrop { get; }
    internal double Radius { get; }
    private Grid Shadow { get; }

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

    /// <summary>Outer window size (DIPs) for a panel of the given size.</summary>
    internal static int Outer(double panel) => (int)Math.Ceiling(panel + Margin * 2);

    /// <summary>
    /// Where the surface grows from and shrinks back into, normalized to the panel ((.5,1) = bottom centre).
    /// Popups pass the side facing the control that opened them, so they stay physically connected to it.
    /// </summary>
    internal void SetOrigin(Vector2 value)
    {
        ProductMotion.SetOrigin(Panel, value);
        ProductMotion.SetOrigin(Shadow, value, (float)Margin);
        Backdrop.SetOrigin(value);
    }

    /// <summary>Panel, shadow and blurred backdrop arrive together: fade, scale from the origin and an optional slide.</summary>
    internal void Enter(bool animate = true, float rise = 0, float slide = 0, float fromScale = .965f, SpringSpec? spec = null,
        Vector2? origin = null, Vector2? velocity = null)
    {
        if (!Backdrop.IsReady)
        {
            // First show of this window: keep the panel invisible until the blur exists, then enter as one object.
            var current = ++entering;
            ProductMotion.Hide(Panel);
            ProductMotion.Hide(Shadow);
            Backdrop.WhenReady(() =>
            {
                if (current == entering) Enter(animate, rise, slide, fromScale, spec, origin, velocity);
            });
            return;
        }
        SetOrigin(origin ?? new Vector2(.5f, .5f));
        var spring = spec ?? SpringSpec.Default;
        ProductMotion.Enter(Panel, animate, rise, fromScale, slide, spring, velocity);
        ProductMotion.Enter(Shadow, animate, rise, fromScale, slide, spring, velocity);
        if (!animate)
        {
            Backdrop.Snap(Vector2.Zero, 1);
            Backdrop.Fade(1, TimeSpan.Zero);
        }
        else if (!ProductMotion.Enabled)
        {
            Backdrop.Fade(0, TimeSpan.Zero);
            Backdrop.Snap(Vector2.Zero, 1);
            var current = ++entering;
            ProductMotion.AfterNextRender(() =>
            {
                if (current == entering) Backdrop.Fade(1, ProductMotion.CrossFade, from: 0);
            });
        }
        else
        {
            // The blur lives on the system compositor, which presents immediately, while the XAML panel's first
            // frame of a newly shown window arrives only after layout and rendering (measured up to ~100 ms). Hold
            // the blur invisible at its start pose and begin its motion once XAML has rendered, so material and
            // panel arrive as one object.
            Backdrop.Fade(0, TimeSpan.Zero);
            Backdrop.Snap(new Vector2(slide, rise), fromScale);
            var current = ++entering;
            ProductMotion.AfterNextRender(() =>
            {
                if (current != entering) return;
                Backdrop.Fade(1, ProductMotion.Popup, from: 0);
                Backdrop.Spring(Vector2.Zero, 1, spring, new Vector2(slide, rise), fromScale, velocity);
            });
        }
    }

    private int entering;

    /// <summary>Show again while already visible (e.g. during an exit animation): settle everything from current values and velocity.</summary>
    internal void Settle(SpringSpec? spec = null)
    {
        entering++;
        var spring = spec ?? SpringSpec.Default;
        ProductMotion.Settle(Panel, spring);
        ProductMotion.Settle(Shadow, spring);
        if (!ProductMotion.Enabled)
        {
            Backdrop.Snap(Vector2.Zero, 1);
            Backdrop.Fade(1, ProductMotion.CrossFade);
            return;
        }
        Backdrop.Fade(1, ProductMotion.Hover);
        Backdrop.Spring(Vector2.Zero, 1, spring);
    }

    /// <summary>Leaves towards the origin it came from (symmetric path), continuing any motion it interrupts.</summary>
    internal Task ExitAsync(float drop = 0, float slide = 0, float toScale = .975f)
    {
        entering++;
        _ = ProductMotion.ExitAsync(Shadow, toScale, drop, slide);
        var done = ProductMotion.ExitAsync(Panel, toScale, drop, slide);
        if (!ProductMotion.Enabled)
        {
            Backdrop.Fade(0, ProductMotion.CrossFade, accelerate: true);
            return done;
        }
        // The blur dematerializes a touch ahead of the tint, so no milky ghost lingers after the panel has gone.
        Backdrop.Fade(0, ProductMotion.Dismiss * .8, accelerate: true);
        Backdrop.Spring(new Vector2(slide, drop), toScale, SpringSpec.Exit);
        return done;
    }

    /// <summary>
    /// Call right after hiding the window without an exit animation. A shown window first presents its last
    /// composed state; concealing the surface on hide guarantees nothing stale (a visible blur without its panel)
    /// can appear before the next entrance runs. Every entrance starts from this concealed state.
    /// </summary>
    internal void Conceal()
    {
        entering++;
        ProductMotion.Hide(Panel);
        ProductMotion.Hide(Shadow);
        Backdrop.Fade(0, TimeSpan.Zero);
    }
}
