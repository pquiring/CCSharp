// dna
// refuse: in one inheritance family
using System;
class ManagedAttribute : Attribute { }
class NativeAttribute : Attribute { }
interface I { int V(); }
[Native] class A : I { public int V() { return 1; } }
[Managed] class B : I { public int V() { return 2; } }
class Program { static int Main() { return 0; } }
