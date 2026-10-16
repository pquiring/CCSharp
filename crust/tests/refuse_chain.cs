// refuse: constructor chaining
using System;
class P {
  int a; P() : this(1) { } P(int x) { a = x; }
  public static int Main() { return 0; }
}
