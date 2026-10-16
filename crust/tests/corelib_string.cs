using System;
class Prog {
  static string Shout(string s) { return s.ToUpper() + "!"; }
  public static int Main() {
    string s = "  Hello, World  ";
    string t = s.Trim();
    Console.WriteLine(t);
    Console.WriteLine(t.Length);
    Console.WriteLine(t.ToUpper());
    Console.WriteLine(t.ToLower());
    Console.WriteLine(t.Substring(7));
    Console.WriteLine(t.Substring(7, 3));
    Console.WriteLine(t.IndexOf("World"));
    Console.WriteLine(t.IndexOf("zzz"));
    Console.WriteLine(t.LastIndexOf("o"));
    Console.WriteLine(t.Contains("lo, W"));
    Console.WriteLine(t.Contains("nope"));
    Console.WriteLine(t.StartsWith("Hell"));
    Console.WriteLine(t.EndsWith("World"));
    Console.WriteLine(t.EndsWith("Hell"));
    Console.WriteLine(t.Replace("l", "L"));
    Console.WriteLine(t.Replace("World", "There"));
    Console.WriteLine(Shout(t.Substring(0, 5)));
    Console.WriteLine(string.IsNullOrEmpty(""));
    Console.WriteLine(string.IsNullOrEmpty(t));
    Console.WriteLine(t.Equals("Hello, World"));
    Console.WriteLine(42.ToString() + "|" + (-7).ToString() + "|" + true.ToString() + "|" + 3000000000L.ToString());
    int n = 123;
    string ns = n.ToString();
    Console.WriteLine(ns.Length);
    Console.WriteLine("abc".ToUpper());
    Console.WriteLine(t.Substring(0, 5) + t.Substring(5, 2));
    if (t.StartsWith("Hello") && t.Contains("World")) Console.WriteLine("both");
    return t.IndexOf(",");
  }
}
