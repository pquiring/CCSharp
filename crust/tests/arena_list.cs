using System;
class MaxInstancesAttribute : Attribute { public MaxInstancesAttribute(int n) { } }

// An arena class: a reference is a pointer into N static slots.  Assignment copies it, null is nothing, == compares references.
[MaxInstances(16)]
class Node {
  public int Value;
  public Node Next;
  public Node(int v) { Value = v; }
  public int Sum() { int t = 0; Node n = this; while (n != null) { t += n.Value; n = n.Next; } return t; }
  public int Length() { int c = 0; Node n = this; while (n != null) { c++; n = n.Next; } return c; }
}

class Program {
  static Node Push(Node head, int v) { Node n = new Node(v); n.Next = head; return n; }

  static Node Reverse(Node head) {
    Node prev = null;
    while (head != null) { Node next = head.Next; head.Next = prev; prev = head; head = next; }
    return prev;
  }

  static int Main() {
    Node head = null;
    for (int i = 1; i <= 5; i++) head = Push(head, i * i);
    Console.WriteLine(head.Sum());
    Console.WriteLine(head.Length());
    Node r = Reverse(head);
    Console.WriteLine(r.Value);
    Console.WriteLine(r.Next.Value);
    Node a = r;
    Node b = r;
    Console.WriteLine(a == b ? 1 : 0);
    Console.WriteLine(a != null ? 1 : 0);
    Node c = new Node(r.Value);
    Console.WriteLine(a == c ? 1 : 0);             // equal contents, but not the same object
    c = a;                                         // aliasing: both name the same object now
    c.Value = 99;
    Console.WriteLine(a.Value);
    return a.Length();
  }
}
