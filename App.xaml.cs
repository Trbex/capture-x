using Microsoft.UI.Xaml;

namespace RealCaptureX;

public partial class App : Application
{
    private Mutex? instance;
    private CaptureController? controller;

    public App() => InitializeComponent();

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
        instance = new Mutex(initiallyOwned: true, "Local\\RealCaptureX", out var ownsInstance);
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
