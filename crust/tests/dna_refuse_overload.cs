// dna
// refuse: is overloaded
using System;
class ManagedAttribute : Attribute { }
class NativeAttribute : Attribute { }
[Managed] class Script { public static int Go(int a) { return a; } public static int Go(long a) { return (int)a; } }
[Native] class Program { static int Main() { return Script.Go(1); } }
