using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace PrivateStack.Infrastructure
{
    public static class WindowsConsoleSignal
    {
        private delegate bool Handler(uint signal);
        private static readonly Handler Ignore = signal => true;
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool FreeConsole();
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool AttachConsole(uint processId);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool SetConsoleCtrlHandler(Handler handler, bool add);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool GenerateConsoleCtrlEvent(uint signal, uint processGroupId);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern uint GetConsoleProcessList([Out] uint[] processes, uint count);

        // Call only in a dedicated short-lived helper process.
        public static void SendBreak(int targetProcessId)
        {
            FreeConsole();
            if (!AttachConsole((uint)targetProcessId)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var members = new uint[16];
                var count = GetConsoleProcessList(members, (uint)members.Length);
                if (count == 0 || count > members.Length) throw new InvalidOperationException("Console membership unavailable");
                for (var i=0; i<count; i++)
                    if (members[i] != targetProcessId && members[i] != Process.GetCurrentProcess().Id)
                        throw new InvalidOperationException("Console contains an unowned process; signal refused");
                SetConsoleCtrlHandler(Ignore, true);
                if (!GenerateConsoleCtrlEvent(1, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
                Thread.Sleep(250);
            }
            finally { FreeConsole(); }
        }
    }
}
