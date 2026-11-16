using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Talvora.Tray;

internal static partial class Program
{
    /// <summary>One WPF Application per process: D105v runs in its own child process, like N8.</summary>
    private static async Task AssertD105vCardStaysOnScreenInChildAsync()
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--d105v-only");
        using var child = Process.Start(start)
            ?? throw new InvalidOperationException("D105v: isolated WPF process did not start.");
        var output = child.StandardOutput.ReadToEndAsync();
        var errors = child.StandardError.ReadToEndAsync();
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90));
        if (child.ExitCode != 0 || !(await output).Contains("D105V_CARD_STAYS_ON_SCREEN_GREEN", StringComparison.Ordinal))
            throw new InvalidOperationException("D105v isolated window regression failed: " + await errors);
        Console.WriteLine("D105V_CARD_STAYS_ON_SCREEN_GREEN");
    }

    /// <summary>
    /// D105v (audit 65 live check): the desktop card has no minimize or maximize box, a minimize request
    /// (Win+Down, Alt+Space) does nothing, and when something minimizes it anyway it comes straight back without
    /// taking the focus. A minimized card is a 160×28 strip with no taskbar button to restore it.
    /// </summary>
    private static async Task AssertD105vCardStaysOnScreenAsync()
    {
        Exception? failure = null;
        var completed = NewSignal();
        var thread = new Thread(() =>
        {
            ControlCenterApplication? application = null;
            try
            {
                application = new ControlCenterApplication(smokeTest: true);
                var window = new DesktopProgressNotificationWindow();
                try
                {
                    window.Update(WorklogTestFrames.Running("d105v", codePreview: null));
                    window.Show();
                    window.UpdateLayout();
                    var handle = new WindowInteropHelper(window).Handle;
                    var style = D105vNative.GetWindowLongPtr(handle, -16).ToInt64();
                    if ((style & 0x00020000) != 0 || (style & 0x00010000) != 0)
                        throw new InvalidOperationException($"D105v: the card window must have no minimize or maximize box, style 0x{style:X8}.");

                    _ = D105vNative.SendMessage(handle, 0x0112, new IntPtr(0xF020), IntPtr.Zero); // WM_SYSCOMMAND, SC_MINIMIZE
                    D105vPump(window.Dispatcher);
                    if (D105vNative.IsIconic(handle))
                        throw new InvalidOperationException("D105v: a minimize command must leave the card on screen.");

                    _ = D105vNative.ShowWindow(handle, 7); // SW_SHOWMINNOACTIVE, as an outside tool does
                    D105vPump(window.Dispatcher);
                    if (D105vNative.IsIconic(handle) || window.WindowState != WindowState.Normal)
                        throw new InvalidOperationException("D105v: a minimized card must come back on screen at once.");
                    if (D105vNative.GetForegroundWindow() == handle)
                        throw new InvalidOperationException("D105v: bringing the card back must not take the focus.");
                }
                finally
                {
                    window.Close();
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                application?.Shutdown();
                completed.TrySetResult(true);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(90));
        if (!thread.Join(TimeSpan.FromSeconds(2)))
            throw new InvalidOperationException("D105v: the STA thread did not exit.");
        if (failure is not null)
            throw new InvalidOperationException("D105v: the card window did not stay on screen.", failure);
    }

    private static void D105vPump(Dispatcher dispatcher)
    {
        for (var round = 0; round < 3; round++)
        {
            dispatcher.Invoke(static () => { }, DispatcherPriority.ContextIdle);
        }
    }
}

internal static class D105vNative
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    internal static extern IntPtr SendMessage(IntPtr windowHandle, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(IntPtr windowHandle);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();
}
