// dna
// refuse: cannot be reached through a handle
using System;
class ManagedAttribute : Attribute { }
class NativeAttribute : Attribute { }
[Managed] class Counter { public int n; public Counter(int s) { n = s; } }
[Native] class Program { static int Main() { Counter c = new Counter(1); return c.n; } }
