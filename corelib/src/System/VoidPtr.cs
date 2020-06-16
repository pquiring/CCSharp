namespace System {
  public struct VoidPtr {
    public unsafe void* Value;
    public bool IsNull() {
      unsafe {
        return Value == null;
      }
    }
  }
}
