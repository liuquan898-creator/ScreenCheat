namespace ScreenCheat;

internal static class Program
{
    private const string MutexName = @"Local\ScreenCheat.SingleInstance";
    private const string ExitEventName = @"Local\ScreenCheat.Exit";

    /// <summary>
    /// Arguments:
    /// <c>--on</c> / <c>--off</c> start with protection on / off, overriding the "enable on start" setting (on by default);
    /// <c>--exit</c> tells the running instance to quit, then returns;
    /// <c>--test-no-affinity</c> is for verification only: overlays are created without the
    /// capture mark, so a screenshot shows whether they really are see-through.
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        if (HasArg(args, "--exit"))
        {
            if (EventWaitHandle.TryOpenExisting(ExitEventName, out EventWaitHandle? running))
            {
                using (running)
                    running.Set();
            }
            return;
        }

        using var singleInstance = new Mutex(initiallyOwned: true, MutexName, out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show("ScreenCheat 已经在运行，请在系统托盘里找到它。", "ScreenCheat",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        using var exitRequested = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        bool? startEnabled = HasArg(args, "--on") ? true : HasArg(args, "--off") ? false : null;
        using var app = new TrayApp(startEnabled, useAffinity: !HasArg(args, "--test-no-affinity"));
        RegisteredWaitHandle wait = ThreadPool.RegisterWaitForSingleObject(
            exitRequested, (_, _) => app.RequestExit(), null, Timeout.Infinite, executeOnlyOnce: true);
        try
        {
            Application.Run(app);
        }
        finally
        {
            wait.Unregister(null);
        }
    }

    private static bool HasArg(string[] args, string name) => args.Contains(name, StringComparer.OrdinalIgnoreCase);
}
