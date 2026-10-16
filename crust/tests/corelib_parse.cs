using System;
class Prog {
  public static int Main() {
    Console.WriteLine(int.Parse("42"));
    Console.WriteLine(int.Parse(" -17 "));
    Console.WriteLine(int.Parse("+5"));
    Console.WriteLine(Convert.ToInt32("2147483647"));
    Console.WriteLine(long.Parse("9223372036854775807"));
    Console.WriteLine(long.Parse("-9223372036854775808"));
    int v;
    Console.WriteLine(int.TryParse("123", out v));
    Console.WriteLine(v);
    Console.WriteLine(int.TryParse("12x", out v));
    Console.WriteLine(int.TryParse("", out v));
    Console.WriteLine(int.TryParse("2147483648", out v));       // overflow
    Console.WriteLine(int.TryParse("-2147483648", out v));
    Console.WriteLine(v);
    long big;
    Console.WriteLine(long.TryParse("9223372036854775808", out big));
    Console.WriteLine(long.TryParse("123456789012", out big));
    Console.WriteLine(big);
    string s = "7";
    int sum = 0;
    for (int i = 0; i < 3; i++) sum += int.Parse(s);
    Console.WriteLine(sum);
    Console.WriteLine(Convert.ToString(99) + Convert.ToString(true));
    return sum;
  }
}
