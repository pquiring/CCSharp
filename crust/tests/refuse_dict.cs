// refuse: Dictionary
using System;
class P {
  static void F() { var d = new System.Collections.Generic.Dictionary<int,int>(); d.TryGetValue(1, out int v); }
  public static int Main() { return 0; }
}
