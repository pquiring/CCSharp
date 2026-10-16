// refuse: would alias
class Node { public int V; }
class P {
  static void F() { Node a = new Node(); Node b = a; }
  public static int Main() { return 0; }
}
