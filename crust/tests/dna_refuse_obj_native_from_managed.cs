// dna
// refuse: through their static methods only
using System;
class ManagedAttribute : Attribute { }
class Vec { public int x; public Vec(int a) { x = a; } public int Get() { return x; } }
[Managed] class Script { public static int Go() { Vec v = new Vec(3); return v.Get(); } }
class Program { static int Main() { return Script.Go(); } }
