// refuse: conflict
class P {
  static int n;
  static int Next() { n++; return n; }
  public static int Main() { int c = 0; while (Next() < Next() + 5) { c++; if (c > 3) break; } return c; }
}
