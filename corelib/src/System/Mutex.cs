namespace System {
  public class Mutex {
    public Mutex() {
      Create();
    }
    ~Mutex() {
      Destroy();
    }
    public extern void Lock();
    private extern void Unlock();
    public extern void Wait();
    public extern void NotifyOne();
    public extern void NotifyAll();

    private unsafe void* NativeMutex;
    private unsafe void* NativeCondition;
    private int Count;
    private Thread Owner;
    private extern void Create();
    private extern void Destroy();
  }
}
