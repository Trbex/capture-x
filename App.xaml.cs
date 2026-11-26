using Microsoft.UI.Xaml;

namespace RealCaptureX;

public partial class App : Application
{
    private Mutex? instance;
    private CaptureController? controller;

    internal static string InstanceName { get; } =
#if DEBUG
        QaMode.Profile ? "RealCaptureX.Inspect" :
#endif
        "RealCaptureX";

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => Diagnostics.Log($"Unhandled: {e.Exception}");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
#if DEBUG
        if (Environment.GetCommandLineArgs().Contains("--self-check"))
        {
            CaptureMathChecks.Run();
            Exit();
            return;
        }
#endif
        instance = new Mutex(initiallyOwned: true, $"Local\\{InstanceName}", out var ownsInstance);
        if (!ownsInstance)
        {
            ShellIntegration.ActivateExisting();
            instance.Dispose();
            Exit();
            return;
        }
        controller = new CaptureController();
        controller.Start();
    }
}
