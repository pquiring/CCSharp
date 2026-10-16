// refuse: loop header
class P {
  static string Name(int i) { return "a"; }
  public static int Main() { int n = 0; while (n < 3 && Name(n) + "x" == "ax") { n++; } return 0; }
}
