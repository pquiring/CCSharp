// refuse: LINQ
using System;
using System.Linq;
class P {
  static void F() { var q = from x in new int[3] select x; }
  public static int Main() { return 0; }
}
