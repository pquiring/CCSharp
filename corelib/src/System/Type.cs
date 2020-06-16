namespace System {
  public class Type {
    private Type() {}
    private unsafe Type(void* value) {Value = value;}

    public extern bool IsPrimitive();

    private unsafe void* Value;  //$class*
  }
}
