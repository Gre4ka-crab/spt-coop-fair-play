using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
namespace PrivateStack.Infrastructure {
    // Read-only global resource query; no process or memory write rights.
    public static class TtdResources {
        [StructLayout(LayoutKind.Sequential)] struct Performance {
            public uint cb;
            public UIntPtr total, limit, peak, physicalTotal, physicalAvailable,
                cache, kernelTotal, kernelPaged, kernelNonpaged, pageSize;
            public uint handles, processes, threads;
        }
        [DllImport("psapi.dll", SetLastError=true)] static extern bool GetPerformanceInfo(ref Performance p, uint cb);
        public sealed class Reading { public ulong available_physical_bytes, commit_headroom_bytes; }
        public static Reading Read() {
            var p=new Performance(); p.cb=(uint)Marshal.SizeOf(typeof(Performance));
            if(!GetPerformanceInfo(ref p,p.cb)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return new Reading {
                available_physical_bytes=checked(p.physicalAvailable.ToUInt64()*p.pageSize.ToUInt64()),
                commit_headroom_bytes=checked((p.limit.ToUInt64()-p.total.ToUInt64())*p.pageSize.ToUInt64())
            };
        }
    }
}
