using System;
using System.Text;
class Prog {
  static void Fill(StringBuilder sb, int n) {
    for (int i = 0; i < n; i++) { sb.Append(i); sb.Append(","); }
  }
  public static int Main() {
    StringBuilder sb = new StringBuilder();
    sb.Append("a").Append(3).Append("b").Append(true);
    Console.WriteLine(sb.ToString());
    Console.WriteLine(sb.Length);
    sb.AppendLine();
    sb.AppendLine("line2");
    sb.Append(-5L).Append(7u).Append(9UL);
    Console.WriteLine(sb.ToString());
    Fill(sb, 4);                          // a StringBuilder is a class: the callee mutates the caller's builder
    Console.WriteLine(sb.Length);
    string snap = sb.ToString();
    sb.Clear();
    Console.WriteLine(sb.Length);
    sb.Append("x");
    Console.WriteLine(snap.Length);
    Console.WriteLine(sb.ToString() + snap.Substring(0, 3));
    return sb.Length;
  }
}
