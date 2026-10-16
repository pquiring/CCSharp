// dna
// refuse: only classes can be held by handle
using System;
class ManagedAttribute : Attribute { }
class NativeAttribute : Attribute { }
[Managed] struct P { public int x; public P(int a) { x = a; } public int Get() { return x; } }
[Native] class Program { static int Main() { P p = new P(1); return p.Get(); } }
