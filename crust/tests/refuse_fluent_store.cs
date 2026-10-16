// refuse: can only start a chain
class Acc { public Acc Add(int x) { return this; } }
class P { public static int Main() { Acc a = new Acc(); Acc b = a.Add(1); return 0; } }
