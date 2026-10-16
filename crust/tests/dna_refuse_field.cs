// dna
// refuse: uses the static field
using System;
class ManagedAttribute : Attribute { }
class NativeAttribute : Attribute { }
[Managed] class Script { public static int counter; }
[Native] class Program { static int Main() { return Script.counter; } }
