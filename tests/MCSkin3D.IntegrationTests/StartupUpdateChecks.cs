using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MCSkin3D.Forms;

internal static class StartupUpdateChecks
{
    internal static void Run(Form editor, Action<bool, string> check)
    {
        var method = typeof(Splash).GetMethod("CheckForUpdatesAfterLoadingAsync", BindingFlags.Static | BindingFlags.NonPublic);
        check(method != null, "Deferred automatic update entry point exists");
        int uiThread = Environment.CurrentManagedThreadId;
        int notifications = 0;
        int notificationThread = 0;
        IWin32Window notificationOwner = null;
        Action<IWin32Window> notify = owner =>
        {
            notifications++;
            notificationThread = Environment.CurrentManagedThreadId;
            notificationOwner = owner;
        };

        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task operation = Invoke(editor, () => pending.Task);
        check(!operation.IsCompleted && editor.Visible, "Editor remains open while automatic update request is pending");
        bool eventDispatched = false;
        editor.BeginInvoke((Action)(() => eventDispatched = true));
        PumpUntil(() => eventDispatched);
        check(!operation.IsCompleted && notifications == 0, "Editor dispatches input/events without waiting for an update response");
        pending.SetResult(true);
        PumpUntil(() => operation.IsCompleted);
        operation.GetAwaiter().GetResult();
        check(notifications == 1 && ReferenceEquals(notificationOwner, editor), "Available update is shown with the editor as owner");
        check(notificationThread == uiThread, "Update notification returns to the editor UI thread");

        Invoke(editor, () => Task.FromResult(false)).GetAwaiter().GetResult();
        check(notifications == 1, "No available update produces no automatic dialog");

        using var closedOwner = new Form { ShowInTaskbar = false, Width = 100, Height = 100 };
        closedOwner.Show(editor);
        pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        operation = Invoke(closedOwner, () => pending.Task);
        closedOwner.Close();
        pending.SetResult(true);
        PumpUntil(() => operation.IsCompleted);
        operation.GetAwaiter().GetResult();
        check(notifications == 1, "Closing the editor before an update response suppresses the notification");
        bool requested = false;
        Invoke(closedOwner, () => { requested = true; return Task.FromResult(true); }).GetAwaiter().GetResult();
        check(!requested && notifications == 1, "Closed editor does not start another automatic update request");

        Task Invoke(Form owner, Func<Task<bool>> request) => (Task)method.Invoke(null, new object[] { owner, request, notify });
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var deadline = Stopwatch.StartNew();
        while (!condition())
        {
            if (deadline.Elapsed > TimeSpan.FromSeconds(5))
                throw new TimeoutException("Deferred update did not complete while pumping editor messages.");
            Application.DoEvents();
            Thread.Sleep(1);
        }
    }
}
