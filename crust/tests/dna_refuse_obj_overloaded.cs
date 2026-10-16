// dna
// refuse: is overloaded
using System;
class ManagedAttribute : Attribute { }
class NativeAttribute : Attribute { }
[Managed] class Counter { public Counter() { } public int Add(int a) { return a; } public int Add(long a) { return (int)a; } }
[Native] class Program { static int Main() { Counter c = new Counter(); return c.Add(1); } }
