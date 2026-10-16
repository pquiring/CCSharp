// dna
// refuse: only as a number, and only to native code
using System;
class ManagedAttribute : Attribute { }
class NativeAttribute : Attribute { }
[Managed] class Script { public static void Bump(ref int x) { x++; } }
[Native] class Program { static int Main() { int v = 1; Script.Bump(ref v); return v; } }
