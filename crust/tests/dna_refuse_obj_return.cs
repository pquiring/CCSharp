// dna
// refuse: returning an object of
using System;
class ManagedAttribute : Attribute { }
class NativeAttribute : Attribute { }
[Managed] class Counter { public Counter(int s) { } }
[Managed] class Factory { public static Counter Make() { return new Counter(1); } }
[Native] class Program { static int Main() { Counter c = Factory.Make(); return 0; } }
