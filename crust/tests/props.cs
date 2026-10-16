using System;
class Temp {
  int c;
  public int Celsius { get { return c; } set { c = value; } }
  public int Fahrenheit { get { return c * 9 / 5 + 32; } }
  public int Double => c * 2;
  public static int Made { get; set; }
  public int Hits { get; set; } = 10;
}
class Prog {
  public static int Main() {
    Temp t = new Temp();
    t.Celsius = 100;
    Console.WriteLine(t.Fahrenheit);
    t.Celsius += 5;
    t.Celsius++;
    Console.WriteLine(t.Celsius);
    Console.WriteLine(t.Double);
    Temp.Made = 3;
    Temp.Made *= 2;
    Console.WriteLine(Temp.Made);
    t.Hits -= 4;
    Console.WriteLine(t.Hits);
    return 0;
  }
}
