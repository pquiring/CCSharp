using System;
enum Color { Red, Green = 5, Blue }
enum Small : byte { A = 1, B = 200 }
class SwitchEnum {
  static int Score(Color c) {
    switch (c) {
      case Color.Red: return 1;
      case Color.Green: return 10;
      default: return 100;
    }
  }
  static int Day(int d) {
    int r = 0;
    switch (d) {
      case 1:
      case 2: r = 12; break;
      case 3: r = 3; break;
      default: r = -1; break;
    }
    return r;
  }
  public static int Main() {
    Console.WriteLine(Score(Color.Red));
    Console.WriteLine(Score(Color.Green));
    Console.WriteLine(Score(Color.Blue));
    Console.WriteLine(Day(1) + Day(2) + Day(3) + Day(9));
    Color c = Color.Blue;
    Console.WriteLine((int)c);
    Small s = Small.B;
    Console.WriteLine((int)s);
    c = (Color)5;
    Console.WriteLine(c == Color.Green);
    return (int)Small.A;
  }
}
