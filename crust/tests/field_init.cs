using System;
using System.Collections.Generic;
using System.Text;
class Holder {
  public List<int> Items = new List<int>();
  public Dictionary<int, int> Map = new Dictionary<int, int>();
  public StringBuilder Log = new StringBuilder();
  public int Count = 5;
  public void Put(int k) { Items.Add(k); Map[k] = k * 2; Log.Append(k).Append(";"); }
}
class Prog {
  public static int Main() {
    Holder h = new Holder();
    h.Put(3); h.Put(4);
    Console.WriteLine(h.Items.Count + h.Count);
    Console.WriteLine(h.Map[4]);
    Console.WriteLine(h.Log.ToString());
    return h.Items[1];
  }
}
