// dna
using System;
class ManagedAttribute : Attribute { }
// An exception that nothing catches ends the process exactly as it does in .NET: the output before it kept, killed by SIGABRT.
[Managed]
class Script {
  public static int Boom(int n) { if (n > 0) throw new InvalidOperationException("boom"); return 0; }
}
class Program {
  static int Main() {
    Console.WriteLine("before");
    Console.WriteLine(Script.Boom(0));
    Console.WriteLine(Script.Boom(1));
    Console.WriteLine("never");
    return 0;
  }
}
