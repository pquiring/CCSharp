// dna
// refuse: is generic
using System;
class ManagedAttribute : Attribute { }
class NativeAttribute : Attribute { }
[Managed] class Box<T> { T v; public T Get() { return v; } }
[Native] class Program { static int Main() { Box<int> b = new Box<int>(); return b.Get(); } }
