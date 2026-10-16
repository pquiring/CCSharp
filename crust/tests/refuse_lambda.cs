// refuse: delegates
using System;
class P {
  static void F() { Func<int,int> f = x => x; }
  public static int Main() { return 0; }
}
