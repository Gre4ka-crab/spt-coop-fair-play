using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace PrivateStack.Infrastructure
{
    public sealed class WindowsProcessIdentity
    {
        public int Pid { get; private set; }
        public string Path { get; private set; }
        public DateTime StartUtc { get; private set; }
        public bool Alive { get; private set; }
        [DllImport("kernel32.dll", SetLastError=true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref uint length);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool GetProcessTimes(IntPtr process, out long created, out long exited, out long kernel, out long user);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool GetExitCodeProcess(IntPtr process, out uint code);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr process);
        [DllImport("advapi32.dll", SetLastError=true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError=true)] private static extern bool GetTokenInformation(IntPtr token, int kind, out uint value, uint size, out uint returned);

        public static bool IsElevated(int pid)
        {
            var process=OpenProcess(0x1000,false,pid);
            if(process==IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try {
                IntPtr token;
                if(!OpenProcessToken(process,8,out token)) throw new Win32Exception(Marshal.GetLastWin32Error());
                try {
                    uint elevated, returned;
                    if(!GetTokenInformation(token,20,out elevated,4,out returned)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    return elevated!=0;
                } finally { CloseHandle(token); }
            } finally { CloseHandle(process); }
        }

        // Read-only identity with QUERY_LIMITED_INFORMATION. No VM, termination,
        // debug privilege or security changes; handle pins one process instance.
        public static WindowsProcessIdentity Query(int pid)
        {
            if (pid <= 0) throw new ArgumentOutOfRangeException(nameof(pid));
            var handle = OpenProcess(0x1000, false, pid);
            if (handle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                if (error == 87) return null; // PID no longer exists
                throw new Win32Exception(error);
            }
            try
            {
                long created, exited, kernel, user; uint code;
                if (!GetProcessTimes(handle, out created, out exited, out kernel, out user)) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (!GetExitCodeProcess(handle, out code)) throw new Win32Exception(Marshal.GetLastWin32Error());
                // A retained exited process can reject image queries with error31.
                // Its signaled exit time is authoritative; never count it as alive.
                if (exited!=0)
                    return new WindowsProcessIdentity { Pid=pid, StartUtc=DateTime.FromFileTimeUtc(created), Alive=false };
                var path = new StringBuilder(32768); uint length = 32768;
                if (!QueryFullProcessImageName(handle, 0, path, ref length)) {
                    int error=Marshal.GetLastWin32Error();
                    // Handle the exit-between-queries race, without hiding errors
                    // on a live/unreadable process or weakening path/start checks.
                    if (GetProcessTimes(handle,out created,out exited,out kernel,out user) && exited!=0)
                        return new WindowsProcessIdentity { Pid=pid, StartUtc=DateTime.FromFileTimeUtc(created), Alive=false };
                    throw new Win32Exception(error);
                }
                return new WindowsProcessIdentity { Pid=pid, Path=path.ToString(), StartUtc=DateTime.FromFileTimeUtc(created), Alive=code==259 };
            }
            finally { CloseHandle(handle); }
        }
    }
}
