// refuse: `null` is not in the Crust C# subset
class P {
  static void F() { string s = null; }
  public static int Main() { return 0; }
}
