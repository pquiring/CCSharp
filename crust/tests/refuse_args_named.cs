// refuse: named arguments
using System;
class P {
  static int G(int a, int b) { return a; } static int F() { return G(b: 1, a: 2); }
  public static int Main() { return 0; }
}
