// dna
// refuse: delegates are not in the Crust C# subset
using System;
class ManagedAttribute : Attribute { }
class NativeAttribute : Attribute { }
[Native] class K { public static int Run() { Func<int> g = () => 1; return g(); } }
class Program { static int Main() { return K.Run(); } }
