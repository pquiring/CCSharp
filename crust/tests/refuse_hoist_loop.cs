// refuse: has no address
class Node { public int V; public Node(int v) { V = v; } }
class P {
  static bool Use(Node n) { return n.V > 0; }
  public static int Main() { int i = 0; while (Use(new Node(i))) { i--; } return 0; }
}
