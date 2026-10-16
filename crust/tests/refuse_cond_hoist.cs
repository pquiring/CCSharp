// refuse: only conditionally evaluated
class P {
  static int n;
  static string Name() { n++; return "x"; }
  static bool Same(string s) { return s == "xx"; }
  public static int Main() { bool ok = n == 0; if (ok && Same("x" + Name())) { return 1; } return 0; }
}
