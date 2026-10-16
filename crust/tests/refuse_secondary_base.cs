// refuse: secondary base
interface IA { int A(); }
interface IB { int B(); }
class K : IA, IB { public int A() { return 1; } public int B() { return 2; } }
class P {
  static int UseB(IB b) { return b.B(); }
  public static int Main() { K k = new K(); return UseB(k); }
}
