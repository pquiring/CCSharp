using System;
class Prog {
  public static int Main() {
    Engine e = new Engine();
    e.Run(5);
    Console.WriteLine(e.Total);
    return Util.Triple(e.Total);
  }
}
