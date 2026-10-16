using System;
using System.Collections.Generic;

// Stack<T> of a struct and of a class that holds a list: Pop copies the element out and removes it, which has to work when the element is a struct
// declared in the same file, after the code that uses it
class Program {
  static int Main() {
    Stack<Pt> s = new Stack<Pt>();
    for (int i = 0; i < 4; i++) s.Push(new Pt(i, i * 10));
    Pt top = s.Peek();
    Pt a = s.Pop();
    Pt b = s.Pop();
    Console.WriteLine(top.X + " " + a.Y + " " + b.X + " " + s.Count);
    Stack<Box> boxes = new Stack<Box>();
    Box x = new Box(); x.Items.Add(5); x.Items.Add(6);
    boxes.Push(x);
    Box y = new Box(); y.Items.Add(9);
    boxes.Push(y);
    Box got = boxes.Pop();
    Console.WriteLine(got.Items.Count + " " + got.Items[0] + " " + boxes.Count);
    return a.Y + b.X;
  }
}
struct Pt { public int X, Y; public Pt(int x, int y) { X = x; Y = y; } }
class Box { public List<int> Items = new List<int>(); }
