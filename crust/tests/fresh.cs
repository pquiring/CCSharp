using System;
using System.Collections.Generic;
class Node { public int V; public Node(int v) { V = v; } }
class Prog {
  static Node Make(int v) { return new Node(v * 2); }
  static Node Local(int v) { Node n = new Node(v); n.V += 1; return n; }
  static int[] Squares(int n) { int[] a = new int[n]; for (int i = 0; i < n; i++) a[i] = i * i; return a; }
  public static int Main() {
    Node n = Make(21);
    Console.WriteLine(n.V);
    Node m = Local(9);
    Console.WriteLine(m.V);
    int[] s = Squares(5);
    Console.WriteLine(s[4]);
    n = Make(1);
    Console.WriteLine(n.V);
    var xs = new List<Node>();
    xs.Add(new Node(1));
    xs.Add(new Node(2));
    int t = 0;
    foreach (Node x in xs) t += x.V;
    Console.WriteLine(t);
    return 0;
  }
}
