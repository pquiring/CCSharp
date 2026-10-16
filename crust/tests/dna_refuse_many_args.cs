// dna
// refuse: takes 18 C arguments
using System;
class ManagedAttribute : Attribute { }
class NativeAttribute : Attribute { }
// native code calls a managed method with eighteen arguments: DNA_Call takes at most 16 (the other direction has no limit: see dna_wide)
[Managed] class Script { public static int F(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n, int o, int p, int q, int r) { return a + b + c + d + e + f + g + h + i + j + k + l + m + n + o + p + q + r; } }
[Native] class Vec { public static int Go() { return Script.F(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18); } }
class Program { static int Main() { return Vec.Go(); } }
