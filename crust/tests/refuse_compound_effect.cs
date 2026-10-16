// refuse: C# reads the left side first
class P {
  static int total;
  static int Bump() { total += 10; return 1; }
  public static int Main() { total += Bump(); return total; }
}
