namespace System {
  public class Thread {
    public void Start() {
      Create();
    }
    public extern void Join();
    public virtual void Run() {}
    ~Thread() {
      Destroy();
    }
    public extern static Thread Current();

    private extern void GC_add_thread();
    private extern void GC_delete_thread();
    private extern void GC_setup_main_thread();
    private Thread Prev;  //GC linked list
    private Thread Next;  //GC linked list

    private unsafe void* StdThread;
    private unsafe void* NativeHandle;
    private unsafe void* StackStart;
    private unsafe void* StackCurrent;
    private extern void Create();
    private extern void Destroy();
  }
}
