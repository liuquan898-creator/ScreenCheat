namespace ScreenCheat;

internal static class Program
{
    private const string MutexName = @"Local\ScreenCheat.SingleInstance";
    private const string ExitEventName = @"Local\ScreenCheat.Exit";

    /// <summary>
    /// Arguments:
    /// <c>--on</c> / <c>--off</c>: if an instance is running, turn its protection on / off and return
    /// once the screen shows it (exit code 0, or 1 if it did not take effect); otherwise start with
    /// protection on / off, overriding the "enable on start" setting (on by default).
    /// <c>--status</c>: exit code 1 if protection is on, 0 if off, 2 if no instance is running.
    /// <c>--exit</c>: tell the running instance to quit, then return.
    /// <c>--test-no-affinity</c>: verification only; overlays are created without the capture mark,
    /// so a screenshot shows whether they really are see-through.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        if (HasArg(args, "--exit"))
        {
            if (EventWaitHandle.TryOpenExisting(ExitEventName, out EventWaitHandle? running))
            {
                using (running)
                    running.Set();
            }
            return 0;
        }

        int? command = HasArg(args, "--status") ? CommandChannel.Status
            : HasArg(args, "--on") ? CommandChannel.On
            : HasArg(args, "--off") ? CommandChannel.Off
            : null;
        if (command is int c && CommandChannel.TrySend(c, out bool active))
        {
            if (c == CommandChannel.Status)
                return active ? 1 : 0;
            return active == (c == CommandChannel.On) ? 0 : 1;
        }
        if (command == CommandChannel.Status)
            return 2;

        using var singleInstance = new Mutex(initiallyOwned: true, MutexName, out bool isFirst);
        if (!isFirst)
        {
            // Called with a command, the caller is a script: answer with an exit code, not a dialog.
            if (command is not null)
                return 3;
            MessageBox.Show("ScreenCheat 已经在运行，请在系统托盘里找到它。", "ScreenCheat",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        ApplicationConfiguration.Initialize();
        using var exitRequested = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        bool? startEnabled = command == CommandChannel.On ? true : command == CommandChannel.Off ? false : null;
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
        return 0;
    }

    private static bool HasArg(string[] args, string name) => args.Contains(name, StringComparer.OrdinalIgnoreCase);
}
