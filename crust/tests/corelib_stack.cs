using System;
using System.Collections.Generic;

// Stack<T>: Push, Peek, Pop (which reads the last and removes it), Count and Clear, of numbers, and of arena references
class MaxInstancesAttribute : Attribute { public MaxInstancesAttribute(int n) { } }

[MaxInstances(8)]
class Node { public int Id; public Node Next; }

class Program {
  static int Main() {
    Stack<int> s = new Stack<int>();
    for (int i = 1; i <= 5; i++) s.Push(i * i);
    Console.WriteLine(s.Count + " " + s.Peek());
    int sum = 0;
    while (s.Count > 2) sum = sum * 10 + s.Pop();
    Console.WriteLine(sum + " " + s.Count + " " + s.Peek());
    s.Push(100);
    int top = s.Pop();
    Console.WriteLine(top + " " + s.Pop() + " " + s.Pop() + " " + s.Count);
    Stack<Node> work = new Stack<Node>();
    Node a = new Node(); a.Id = 7;
    Node b = new Node(); b.Id = 9; b.Next = a;
    work.Push(a); work.Push(b);
    int walked = 0;
    while (work.Count > 0) {
      Node n = work.Pop();
      walked = walked * 100 + n.Id;
      if (n.Next != null) work.Push(n.Next);
    }
    Console.WriteLine(walked);
    s.Push(1); s.Push(2); s.Clear();
    Console.WriteLine(s.Count);
    return walked % 256;
  }
}
