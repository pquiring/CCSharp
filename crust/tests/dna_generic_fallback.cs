// dna
using System;
class ManagedAttribute : Attribute { }
// Crust has no generic methods.  A caller of one is refused too, so it falls back as well, and with the callee there is no boundary left.
[Managed] class Script { public static T Id<T>(T x) { return x; } public static int Twice(int x) { return x * 2; } }
class Program { static int Main() { Console.WriteLine(Script.Id<int>(3)); Console.WriteLine(Script.Id<string>("s")); Console.WriteLine(Script.Twice(4)); return 3; } }
