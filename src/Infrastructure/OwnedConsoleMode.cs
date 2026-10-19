using System;
using System.Runtime.InteropServices;
namespace PrivateStack.Infrastructure {
 public static class OwnedConsoleMode {
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool AttachConsole(uint pid);
  [DllImport("kernel32.dll")]static extern bool FreeConsole();
  [DllImport("kernel32.dll")]static extern uint GetConsoleProcessList(uint[] values,uint length);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr CreateFile(string p,uint a,uint s,IntPtr sec,uint c,uint f,IntPtr t);
  [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr h);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetConsoleMode(IntPtr h,out uint mode);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetConsoleMode(IntPtr h,uint mode);
  public static object DisableQuickEdit(int pid,bool apply) {
   FreeConsole();
   if(!AttachConsole((uint)pid))throw new System.ComponentModel.Win32Exception();
   try {
    var members=new uint[16];uint n=GetConsoleProcessList(members,16);
    if(n==0||n>16)throw new InvalidOperationException("Unexpected console membership");
    bool found=false;
    for(int i=0;i<n;i++) {if(members[i]==(uint)pid)found=true;else if(members[i]!=(uint)System.Diagnostics.Process.GetCurrentProcess().Id)throw new InvalidOperationException("Foreign console member preserved");}
    if(!found)throw new InvalidOperationException("Wrong console");
    var h=CreateFile("CONIN$",apply?0xc0000000u:0x80000000u,3,IntPtr.Zero,3,0,IntPtr.Zero);
    if(h==new IntPtr(-1))throw new System.ComponentModel.Win32Exception();
    try {
     uint before;if(!GetConsoleMode(h,out before))throw new System.ComponentModel.Win32Exception();
     uint after=(before|0x80u)&~0x40u;
     if(apply&&before!=after&&!SetConsoleMode(h,after))throw new System.ComponentModel.Win32Exception();
     return new {status=apply?(before==after?"UNCHANGED":"QUICK_EDIT_DISABLED"):"DRY_RUN",game_pid=pid,old_mode=before,new_mode=apply?after:before,planned_mode=after,other_flags_preserved=(before&~0xc0u)==(after&~0xc0u),input_events_written=0,process_restarted=false};
    }finally{CloseHandle(h);}
   }finally{FreeConsole();}
  }
 }
}
