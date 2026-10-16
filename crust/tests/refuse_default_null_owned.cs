// refuse: there is no null for it
using System;
class Owned { public int V; }
class Program {
  static int F(Owned o = null) { return 1; }
  static int Main() { return F(); }
}
