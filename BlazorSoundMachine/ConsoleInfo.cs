namespace BlazorSoundMachine
{
    using System;
    using System.Runtime.InteropServices;

    static class ConsoleInfo
    {
        const int HideValue = 0;
        const int RestoreValue = 9;
        const int ShowValue = 5;
        static IntPtr hConsole = IntPtr.Zero;
        static IntPtr hProcessId = IntPtr.Zero;
        static IntPtr hWindowThreadProcessId = IntPtr.Zero;

        static ConsoleInfo()
        {
            hConsole = GetConsoleWindow();
            GetWindowThreadProcessId(hConsole, ref hWindowThreadProcessId);
            hProcessId = GetCurrentProcessId();
        }

        public static bool LaunchedFromConsole => hProcessId != hWindowThreadProcessId;

        public static bool HideConsole() => ShowWindow(hConsole, HideValue);

        public static bool ShowConsole() => ShowWindow(hConsole, ShowValue) | ShowWindow(hConsole, RestoreValue) | SetForegroundWindow(new IntPtr(hConsole.ToInt64() | 0x01));

        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcessId();

        [DllImport("user32.dll")]
        static extern int GetWindowThreadProcessId(IntPtr hWnd, ref IntPtr processId);

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
