// refuse: only supported in a plain statement
class S0 { public virtual int V() { return 0; } }
class S1 : S0 { }
class P {
  static bool Ok(S0 s) { return s.V() == 0; }
  public static int Main() { S1 x = new S1(); int n = 0; while (Ok(x) && n < 1) { n++; } return n; }
}
