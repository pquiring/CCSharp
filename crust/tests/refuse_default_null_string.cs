// refuse: a string is never null in Crust
using System;
class Program {
  static string Name(string s = null) { return "x"; }
  static int Main() { Console.WriteLine(Name()); return 0; }
}
