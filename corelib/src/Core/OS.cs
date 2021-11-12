using System;

namespace Core {
  public class OS {
    public extern static void ThreadInit(Thread thread);
    public extern static void ThreadSuspend(Thread thread);
    public extern static void ThreadResume(Thread thread);
    public extern static void ThreadGetHandle(Thread thread);
    public extern static int GetThreadContextCount();
    public extern static unsafe void GetThreadContext(Thread thread, void** context);
    public extern static int AllocateVirtualPages(int chain,int page,int cnt);
    public extern static void ConsoleEnable();
    public extern static void ConsoleDisable();
    public extern static int ConsoleWidth();
    public extern static int ConsoleHeight();
    public extern static int ConsolePositionX();
    public extern static int ConsolePositionY();
  }
}
