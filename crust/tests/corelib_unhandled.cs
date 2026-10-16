using System;
// There is no `catch` in the Crust subset, so an exception can never be observed: it is an unhandled
// exception and ends the process, exactly like .NET (exit status 134, output before it kept).
class Prog {
  public static int Main() {
    Console.WriteLine("before");
    string s = "abc";
    Console.WriteLine(s.Substring(1, 1));
    string t = s.Substring(2, 5);        // out of range
    Console.WriteLine("never");
    return 0;
  }
}
