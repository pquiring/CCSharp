// refuse: returning an existing object
class Part { public int V; }
class P { static Part Id(Part q) { return q; } public static int Main() { return 0; } }
