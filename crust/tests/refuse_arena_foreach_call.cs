// refuse: over arena references needs a variable, field or property
using System;
using System.Collections.Generic;
class MaxInstancesAttribute : Attribute { public MaxInstancesAttribute(int n) { } }
[MaxInstances(4)] class Item { public int V; }
class Program {
  static List<Item> items = new List<Item>();
  static List<Item> All() { return items; }
  static int Main() { items.Add(new Item()); int t = 0; foreach (Item i in All()) t += i.V; return t; }
}
