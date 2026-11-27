using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace RealCaptureX;

/// <summary>
/// A spring described the way designers think about it (Apple's model): <see cref="Damping"/> is the
/// damping ratio (1 = critically damped, no overshoot; below 1 overshoots) and <see cref="Response"/> is
/// the period of the undamped spring in seconds (lower = snappier). A spring has no duration: its settle
/// time emerges from these two numbers.
/// </summary>
internal readonly record struct SpringSpec(float Damping, float Response)
{
    /// <summary>Panels and windows arriving or re-settling: no overshoot.</summary>
    internal static readonly SpringSpec Default = new(1f, .30f);
    /// <summary>Small popups (menus, pills, badges): quick and still critically damped.</summary>
    internal static readonly SpringSpec Snappy = new(1f, .22f);
    /// <summary>Selection pill that follows the pointer between controls; a whisper of give, no visible bounce.</summary>
    internal static readonly SpringSpec Indicator = new(.92f, .26f);
    /// <summary>Press feedback: reaches the pressed state almost immediately, releases just as fast.</summary>
    internal static readonly SpringSpec Press = new(1f, .12f);
    /// <summary>Leaving: fast, critically damped; opacity reaches zero long before the tail matters.</summary>
    internal static readonly SpringSpec Exit = new(1f, .20f);
    /// <summary>Only after a gesture that carried momentum (a flick, a throw): a little overshoot feels physical.</summary>
    internal static readonly SpringSpec Momentum = new(.82f, .36f);
    /// <summary>Continuous values the user steers directly (zoom, the selection highlight).</summary>
    internal static readonly SpringSpec Tracking = new(1f, .24f);

    internal float Omega => 2 * MathF.PI / MathF.Max(.001f, Response);

    /// <summary>
    /// Value for a composition spring's <c>Period</c>. Measured on the real compositor (Desktop Duplication,
    /// 2026-11-27): Windows treats <c>Period</c> as the time constant 1/ω, not the oscillation period, so Apple's
    /// response maps to response / 2π. Using the response directly made every spring 2π× too slow.
    /// </summary>
    internal TimeSpan CompositionPeriod => TimeSpan.FromSeconds(Response / (2 * MathF.PI));
}

/// <summary>Closed-form damped harmonic oscillator: exact position and velocity at any time, frame-rate independent.</summary>
internal static class SpringMath
{
    /// <summary>Advances one axis by <paramref name="t"/> seconds towards <paramref name="target"/>.</summary>
    internal static (double Position, double Velocity) Step(double position, double velocity, double target, SpringSpec spec, double t)
    {
        var x0 = position - target;
        var v0 = velocity;
        var w = (double)spec.Omega;
        var z = (double)spec.Damping;
        double x, v;
        if (z < .999)
        {
            var wd = w * Math.Sqrt(1 - z * z);
            var a = z * w;
            var b = (v0 + a * x0) / wd;
            var decay = Math.Exp(-a * t);
            var (sin, cos) = Math.SinCos(wd * t);
            x = decay * (x0 * cos + b * sin);
            v = decay * ((-a * x0 + b * wd) * cos + (-a * b - x0 * wd) * sin);
        }
        else if (z <= 1.001)
        {
            var b = v0 + w * x0;
            var decay = Math.Exp(-w * t);
            x = (x0 + b * t) * decay;
            v = (v0 - w * b * t) * decay;
        }
        else
        {
            var root = Math.Sqrt(z * z - 1);
            var r1 = -w * (z - root);
            var r2 = -w * (z + root);
            var c2 = (v0 - r1 * x0) / (r2 - r1);
            var c1 = x0 - c2;
            x = c1 * Math.Exp(r1 * t) + c2 * Math.Exp(r2 * t);
            v = c1 * r1 * Math.Exp(r1 * t) + c2 * r2 * Math.Exp(r2 * t);
        }
        return (target + x, v);
    }

    internal static (Vector3 Position, Vector3 Velocity) Step(Vector3 position, Vector3 velocity, Vector3 target, SpringSpec spec, double t)
    {
        var (x, vx) = Step(position.X, velocity.X, target.X, spec, t);
        var (y, vy) = Step(position.Y, velocity.Y, target.Y, spec, t);
        var (z, vz) = Step(position.Z, velocity.Z, target.Z, spec, t);
        return (new Vector3((float)x, (float)y, (float)z), new Vector3((float)vx, (float)vy, (float)vz));
    }

    /// <summary>
    /// Where a flick would come to rest under scroll-like exponential deceleration (Apple's projection from
    /// <i>Designing Fluid Interfaces</i>). <paramref name="velocity"/> in units per second.
    /// </summary>
    internal static double Project(double velocity, double deceleration = .998) =>
        velocity / 1000 * deceleration / (1 - deceleration);

    /// <summary>Progressive resistance past a boundary: the further out, the less the element follows.</summary>
    internal static double Rubberband(double overshoot, double dimension, double constant = .55) =>
        overshoot * dimension * constant / (dimension + constant * Math.Abs(overshoot));
}

/// <summary>Short pointer history → release velocity (units per second) from the last ~80 ms of movement.</summary>
internal sealed class VelocityTracker
{
    private readonly (long Time, double X, double Y)[] samples = new (long, double, double)[12];
    private int count;
    private int head;

    internal void Reset() => count = head = 0;

    internal void Add(double x, double y)
    {
        samples[head] = (Stopwatch.GetTimestamp(), x, y);
        head = (head + 1) % samples.Length;
        count = Math.Min(count + 1, samples.Length);
    }

    internal (double X, double Y) Velocity()
    {
        if (count < 2) return (0, 0);
        var newest = samples[(head - 1 + samples.Length) % samples.Length];
        // A pointer that stopped before release has no momentum, however fast it moved earlier.
        if (Stopwatch.GetElapsedTime(newest.Time) > TimeSpan.FromMilliseconds(60)) return (0, 0);
        var oldest = newest;
        for (var i = 2; i <= count; i++)
        {
            var sample = samples[(head - i + samples.Length * 2) % samples.Length];
            if (Stopwatch.GetElapsedTime(sample.Time, newest.Time) > TimeSpan.FromMilliseconds(80)) break;
            oldest = sample;
        }
        var seconds = Stopwatch.GetElapsedTime(oldest.Time, newest.Time).TotalSeconds;
        return seconds < .004 ? (0, 0) : ((newest.X - oldest.X) / seconds, (newest.Y - oldest.Y) / seconds);
    }
}

/// <summary>
/// Spring for values that live on the UI thread (layout rectangles, HWND positions, canvas zoom). It keeps
/// position and velocity, so a new target mid-flight bends the motion instead of restarting it, and it
/// unsubscribes from the render loop as soon as it is at rest.
/// </summary>
internal sealed class SpringDriver
{
    private readonly double[] value;
    private readonly double[] velocity;
    private readonly double[] target;
    private readonly Action<double[]> apply;
    private readonly double precision;
    private SpringSpec spec = SpringSpec.Default;
    private long last;
    private bool running;

    internal SpringDriver(int dimensions, Action<double[]> apply, double precision = .25)
    {
        value = new double[dimensions];
        velocity = new double[dimensions];
        target = new double[dimensions];
        this.apply = apply;
        this.precision = precision;
    }

    internal event Action? Settled;
    internal bool IsRunning => running;
    internal IReadOnlyList<double> Value => value;
    internal IReadOnlyList<double> Velocity => velocity;
    internal IReadOnlyList<double> Target => target;

    /// <summary>Jumps to a value with no motion (also used when motion is off).</summary>
    internal void Snap(params double[] to)
    {
        Stop();
        Array.Copy(to, value, value.Length);
        Array.Copy(to, target, target.Length);
        Array.Clear(velocity);
        apply(value);
    }

    /// <summary>Replaces position and velocity without drawing (the next <see cref="To"/> continues from here).</summary>
    internal void Reset(double[] to, double[]? withVelocity = null)
    {
        Array.Copy(to, value, value.Length);
        if (withVelocity is null) Array.Clear(velocity);
        else Array.Copy(withVelocity, velocity, velocity.Length);
    }

    /// <summary>Moves towards a target from the current value, keeping the current velocity unless one is handed off.</summary>
    internal void To(double[] to, SpringSpec? with = null, double[]? handoff = null)
    {
        Array.Copy(to, target, target.Length);
        if (handoff is not null) Array.Copy(handoff, velocity, velocity.Length);
        spec = ProductMotion.Scaled(with ?? SpringSpec.Default);
        if (!ProductMotion.Enabled)
        {
            Snap(to);
            return;
        }
        last = Stopwatch.GetTimestamp();
        if (running) return;
        running = true;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += Tick;
    }

    internal void Stop()
    {
        if (!running) return;
        running = false;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= Tick;
    }

    private void Tick(object? sender, object args)
    {
        var now = Stopwatch.GetTimestamp();
        var t = Math.Min(.064, Stopwatch.GetElapsedTime(last, now).TotalSeconds);
        last = now;
        var rest = true;
        for (var i = 0; i < value.Length; i++)
        {
            (value[i], velocity[i]) = SpringMath.Step(value[i], velocity[i], target[i], spec, t);
            if (Math.Abs(value[i] - target[i]) > precision || Math.Abs(velocity[i]) > precision * 8) rest = false;
        }
        if (rest)
        {
            Array.Copy(target, value, value.Length);
            Array.Clear(velocity);
            Stop();
        }
        apply(value);
        if (rest) Settled?.Invoke();
    }
}

/// <summary>
/// Shared motion system. Movement runs on the composition thread with springs that start from the value on
/// screen and inherit the velocity of whatever motion they interrupt, so every transition can be grabbed and
/// reversed without a jump or a velocity "brick wall". With reduced motion (Windows "Animation effects" off,
/// or the app setting) movement and scaling are replaced by a short cross-fade.
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
    internal static SpringSpec Scaled(SpringSpec spec) => spec with { Response = (float)(spec.Response * slowmo) };

    internal static readonly TimeSpan Hover = Slow(120);
    internal static readonly TimeSpan Press = Slow(80);
    internal static readonly TimeSpan Popup = Slow(180);
    internal static readonly TimeSpan Page = Slow(220);
    internal static readonly TimeSpan Dismiss = Slow(140);
    /// <summary>Reduced-motion replacement for any movement: a brief opacity cross-fade.</summary>
    internal static readonly TimeSpan CrossFade = Slow(110);

    private sealed class Pivot
    {
        internal Vector2 Origin = new(.5f, .5f);
        internal float Inset;

        internal Vector3 Point(double width, double height) => new(
            Inset + (float)Math.Max(0, width - Inset * 2) * Origin.X, Inset + (float)Math.Max(0, height - Inset * 2) * Origin.Y, 0);
    }

    private sealed class SpringTrack
    {
        internal Vector3 From, Velocity, Target;
        internal SpringSpec Spec;
        internal long Start;
    }

    private static readonly ConditionalWeakTable<FrameworkElement, Pivot> pivots = new();
    private static readonly ConditionalWeakTable<object, Dictionary<string, SpringTrack>> tracks = new();

    /// <summary>Runs an action at the start of the next XAML frame (once).</summary>
    internal static void OnNextFrame(Action action)
    {
        EventHandler<object>? handler = null;
        handler = (_, _) =>
        {
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= handler;
            action();
        };
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += handler;
    }

    /// <summary>Full motion allowed (Windows animation effects on and the app's Reduce motion off).</summary>
    internal static bool Enabled => Appearance.AnimationsEnabled;

    /// <summary>
    /// Records a spring about to start on <paramref name="owner"/>.<paramref name="property"/> and returns the
    /// velocity it must start with: the live velocity of the spring it replaces (computed analytically), or the
    /// gesture velocity handed off by the caller. Works for both compositors (the glass backdrop lives on the system one).
    /// </summary>
    internal static Vector3 Retarget(object owner, string property, Vector3 target, SpringSpec spec, Vector3? from, Vector3? handoff)
    {
        var now = Stopwatch.GetTimestamp();
        var map = tracks.GetOrCreateValue(owner);
        var position = from ?? target;
        var velocity = Vector3.Zero;
        if (map.TryGetValue(property, out var previous))
        {
            var (p, v) = SpringMath.Step(previous.From, previous.Velocity, previous.Target, previous.Spec,
                Stopwatch.GetElapsedTime(previous.Start, now).TotalSeconds);
            position = from ?? p;
            velocity = v;
        }
        if (handoff is { } given) velocity = given;
        map[property] = new SpringTrack { From = position, Velocity = velocity, Target = target, Spec = spec, Start = now };
        return velocity;
    }

    /// <summary>Forgets spring state after a property was set directly or by a key-frame animation.</summary>
    internal static void Forget(object owner, string property)
    {
        if (tracks.TryGetValue(owner, out var map)) map.Remove(property);
    }

    /// <summary>Pop in: fades in while scale and position settle on a critically damped spring from <paramref name="origin"/>.</summary>
    internal static void Enter(FrameworkElement element, bool enabled = true, float rise = 0, float fromScale = .965f, float slide = 0,
        SpringSpec? spec = null, Vector2? velocity = null)
    {
        var visual = Prepare(element);
        if (!enabled)
        {
            Rest(visual);
            return;
        }
        if (!Enabled)
        {
            Rest(visual);
            visual.Opacity = 0;
            Fade(visual, 1, CrossFade);
            return;
        }
        var spring = spec ?? SpringSpec.Default;
        visual.Opacity = 0;
        Fade(visual, 1, Popup);
        Spring(visual, "Scale", Vector3.One, spring, from: new Vector3(fromScale, fromScale, 1));
        Spring(visual, "Translation", Vector3.Zero, spring, from: new Vector3(slide, rise, 0),
            handoff: velocity is { } v ? new Vector3(v, 0) : null);
    }

    /// <summary>
    /// Launch velocity for something arriving from off screen: already moving on its first frame (so it is seen
    /// immediately) yet still critically damped. A critically damped spring overshoots once v₀ exceeds ω·distance,
    /// so this stays just under that limit.
    /// </summary>
    internal static Vector2 Launch(Vector2 travel, SpringSpec spec, float fraction = .85f) => -travel * spec.Omega * fraction;

    /// <summary>Returns an element to rest from wherever an interrupted animation left it, keeping its velocity.</summary>
    internal static void Settle(FrameworkElement element, SpringSpec? spec = null)
    {
        var visual = Prepare(element);
        if (!Enabled)
        {
            visual.StopAnimation("Scale");
            visual.StopAnimation("Translation");
            visual.Scale = Vector3.One;
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
            Forget(visual, "Scale");
            Forget(visual, "Translation");
            Fade(visual, 1, CrossFade);
            return;
        }
        Fade(visual, 1, Hover);
        Spring(visual, "Scale", Vector3.One, spec ?? SpringSpec.Default);
        Spring(visual, "Translation", Vector3.Zero, spec ?? SpringSpec.Default);
    }

    /// <summary>
    /// Leaves along the path it came in: ease-in fade, motion on a fast spring that inherits any velocity it
    /// interrupts. Safe to interrupt with <see cref="Settle"/>.
    /// </summary>
    internal static Task ExitAsync(FrameworkElement element, float toScale = .975f, float drop = 0, float slide = 0, Vector3? velocity = null)
    {
        var visual = Prepare(element);
        if (!Enabled)
        {
            Fade(visual, 0, CrossFade, accelerate: true);
            return Task.Delay(CrossFade);
        }
        Fade(visual, 0, Dismiss, accelerate: true);
        Spring(visual, "Scale", new Vector3(toScale, toScale, 1), SpringSpec.Exit);
        Spring(visual, "Translation", new Vector3(slide, drop, 0), velocity is null ? SpringSpec.Exit : SpringSpec.Default,
            handoff: velocity);
        return Task.Delay(Dismiss);
    }

    /// <summary>Content swap inside a stable container: fades in, optionally sliding in from the direction of travel.</summary>
    internal static void Reveal(FrameworkElement element, float slide = 0)
    {
        var visual = Prepare(element);
        visual.Opacity = 0;
        if (!Enabled)
        {
            Fade(visual, 1, CrossFade);
            return;
        }
        Fade(visual, 1, Page);
        if (slide == 0) return;
        Spring(visual, "Translation", Vector3.Zero, SpringSpec.Default, from: new Vector3(slide, 0, 0));
    }

    /// <summary>Slides a selection indicator to a new offset; retargets mid-flight keep the pill's velocity.</summary>
    internal static void MoveTo(FrameworkElement element, Vector2 offset, bool animate = true, SpringSpec? spec = null)
    {
        var visual = Prepare(element);
        var target = new Vector3(offset, 0);
        if (!animate || !Enabled)
        {
            Set(visual, "Translation", target);
            return;
        }
        Spring(visual, "Translation", target, spec ?? SpringSpec.Indicator);
    }

    /// <summary>Animates a scale (e.g. a pill growing to a new width) on the indicator spring.</summary>
    internal static void ScaleTo(FrameworkElement element, Vector2 scale, bool animate = true, SpringSpec? spec = null)
    {
        var visual = Prepare(element);
        var target = new Vector3(scale, 1);
        if (!animate || !Enabled)
        {
            visual.StopAnimation("Scale");
            visual.Scale = target;
            Forget(visual, "Scale");
            return;
        }
        Spring(visual, "Scale", target, spec ?? SpringSpec.Indicator);
    }

    /// <summary>Drives an element's translation 1:1 during a gesture (no animation, no lag).</summary>
    internal static void Track(FrameworkElement element, Vector2 offset) => Set(Prepare(element), "Translation", new Vector3(offset, 0));

    /// <summary>
    /// Starts an element invisible at the composition level. Never use XAML Opacity = 0 for elements that
    /// later fade in through composition: the two opacities multiply, so the fade would stay invisible.
    /// </summary>
    internal static void Hide(FrameworkElement element) => Prepare(element).Opacity = 0;

    internal static void Opacity(FrameworkElement element, float to, TimeSpan duration)
    {
        var visual = Prepare(element);
        if (duration <= TimeSpan.Zero) { visual.StopAnimation("Opacity"); visual.Opacity = to; return; }
        Fade(visual, to, Enabled ? duration : TimeSpan.FromTicks(Math.Min(duration.Ticks, CrossFade.Ticks)));
    }

    /// <summary>
    /// Sets where an element scales from, normalized to its size ((0,0) top-left, (.5,.5) centre). Popups use
    /// the corner nearest the control that opened them, so they grow out of it and shrink back into it.
    /// </summary>
    /// <param name="inset">Margin (DIPs) around the part that should pivot, e.g. the shadow layer's transparent border.</param>
    internal static void SetOrigin(FrameworkElement element, Vector2 origin, float inset = 0)
    {
        var visual = Prepare(element);
        var pivot = pivots.GetOrCreateValue(element);
        pivot.Origin = origin;
        pivot.Inset = inset;
        visual.CenterPoint = pivot.Point(element.ActualWidth, element.ActualHeight);
    }

    internal static Visual Prepare(FrameworkElement element)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        if (!pivots.TryGetValue(element, out var pivot))
        {
            pivot = new Pivot();
            pivots.Add(element, pivot);
            element.SizeChanged += (_, e) => visual.CenterPoint = pivot.Point(e.NewSize.Width, e.NewSize.Height);
        }
        if (element.ActualWidth <= 0 && element.IsLoaded) element.UpdateLayout();
        visual.CenterPoint = pivot.Point(element.ActualWidth, element.ActualHeight);
        return visual;
    }

    private static void Rest(Visual visual)
    {
        visual.StopAnimation("Opacity");
        visual.StopAnimation("Scale");
        visual.StopAnimation("Translation");
        visual.Opacity = 1;
        visual.Scale = Vector3.One;
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
        Forget(visual, "Scale");
        Forget(visual, "Translation");
    }

    private static void Set(Visual visual, string property, Vector3 value)
    {
        visual.StopAnimation(property);
        if (property == "Scale") visual.Scale = value;
        else visual.Properties.InsertVector3(property, value);
        Forget(visual, property);
        // A direct set is a known position at rest: the next spring starts from here with zero velocity.
        Retarget(visual, property, value, SpringSpec.Default, value, Vector3.Zero);
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

    /// <summary>Starts a composition spring that continues the velocity of the motion it replaces.</summary>
    internal static void Spring(Visual visual, string property, Vector3 to, SpringSpec spec, Vector3? from = null, Vector3? handoff = null)
    {
        var scaled = Scaled(spec);
        var velocity = Retarget(visual, property, to, scaled, from, handoff);
        var spring = visual.Compositor.CreateSpringVector3Animation();
        spring.FinalValue = to;
        if (from is { } start) spring.InitialValue = start;
        spring.InitialVelocity = velocity;
        spring.DampingRatio = spec.Damping;
        spring.Period = scaled.CompositionPeriod;
        spring.StopBehavior = AnimationStopBehavior.SetToFinalValue;
        visual.StartAnimation(property, spring);
    }

    /// <summary>Ease-out (decelerate) for entering, ease-in for leaving.</summary>
    internal static CompositionEasingFunction Curve(Compositor compositor, bool accelerate = false) => accelerate
        ? compositor.CreateCubicBezierEasingFunction(new Vector2(.4f, 0), new Vector2(1, 1))
        : compositor.CreateCubicBezierEasingFunction(new Vector2(.16f, 1), new Vector2(.3f, 1));
}
