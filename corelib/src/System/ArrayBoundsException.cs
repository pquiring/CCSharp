namespace System {
  public class ArrayBoundsException : Exception {
    public int Index, Size;
    public ArrayBoundsException() {}
    public ArrayBoundsException(String msg) : base(msg) {
    }
    public ArrayBoundsException(int Index, int Size) {
      this.Index = Index;
      this.Size = Size;
    }
  }
}
