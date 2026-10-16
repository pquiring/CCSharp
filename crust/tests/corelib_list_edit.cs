using System;
using System.Collections.Generic;
class MaxInstancesAttribute : Attribute { public MaxInstancesAttribute(int n) { } }

[MaxInstances(8)]
class Item { public int V; public Item(int v) { V = v; } }

class Program {
  static int Sum(List<int> l) { int t = 0; for (int i = 0; i < l.Count; i++) t += l[i] * (i + 1); return t; }

  static int Main() {
    List<int> a = new List<int>();
    for (int i = 1; i <= 6; i++) a.Add(i * 10);
    a.RemoveAt(0);
    Console.WriteLine(a.Count + " " + a[0] + " " + a[4]);
    a.RemoveAt(2);
    Console.WriteLine(a.Count + " " + a[2] + " " + Sum(a));
    a.Insert(1, 7);
    Console.WriteLine(a.Count + " " + a[0] + " " + a[1] + " " + a[2]);
    a.Insert(0, 1);
    a.Insert(a.Count, 2);
    Console.WriteLine(a.Count + " " + a[0] + " " + a[a.Count - 1]);
    a.RemoveAt(a.Count - 1);
    Console.WriteLine(Sum(a));

    // the swap-remove an engine uses to drop an element in O(1)
    List<Item> items = new List<Item>();
    for (int i = 0; i < 5; i++) items.Add(new Item(i));
    int at = 1;
    items[at] = items[items.Count - 1];
    items.RemoveAt(items.Count - 1);
    int t = 0;
    foreach (Item it in items) t = t * 10 + it.V;
    Console.WriteLine(items.Count + " " + t);
    Item extra = new Item(9);
    items.Insert(0, extra);
    Console.WriteLine(items[0].V + " " + items.Count);
    return a.Count;
  }
}
