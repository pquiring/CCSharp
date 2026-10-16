// refuse: try
using System;
class P {
  static void F() { try { F(); } catch (Exception e) { } }
  public static int Main() { return 0; }
}
