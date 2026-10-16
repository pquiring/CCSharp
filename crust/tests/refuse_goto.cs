// refuse: goto
using System;
class P {
  static void F() { int i = 0; top: i++; if (i < 3) goto top; }
  public static int Main() { return 0; }
}
