// refuse: changes what C# does
class Node { public int V; public Node(int v) { V = v; } }
class P {
  static int Next() { return 1; }
  static void Use(int a, Node n) { }
  public static int Main() { Use(Next(), new Node(2)); return 0; }
}
