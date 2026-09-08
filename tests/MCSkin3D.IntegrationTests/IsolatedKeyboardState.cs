using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

// SetKeyboardState changes only this test thread's input-state table. It does not
// send keystrokes or change input state in the user's applications.
internal sealed class IsolatedKeyboardState : IDisposable
{
    private readonly byte[] saved = new byte[256];

    internal IsolatedKeyboardState()
    {
        if (!GetKeyboardState(saved) || !SetKeyboardState(new byte[256]))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Dispose()
    {
        if (!SetKeyboardState(saved))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetKeyboardState([Out] byte[] state);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetKeyboardState(byte[] state);
}
