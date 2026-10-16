// refuse: generic methods
using System;
class P {
  static T Id<T>(T x) { return x; }
  public static int Main() { return 0; }
}
