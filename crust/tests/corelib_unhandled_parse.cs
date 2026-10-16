using System;
class Prog {
  public static int Main() {
    Console.WriteLine("start");
    int n = int.Parse("12");
    Console.WriteLine(n);
    int bad = int.Parse("twelve");       // FormatException
    Console.WriteLine("never");
    return bad;
  }
}
