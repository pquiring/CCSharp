// dna
using System;
// The entry point is managed (it has a try/catch), and calls native kernels.
class Kernel {
  public static int Fib(int n) { int a = 0, b = 1; for (int i = 0; i < n; i++) { int t = a + b; a = b; b = t; } return a; }
  public static int Sum(int[] a) { int t = 0; for (int i = 0; i < a.Length; i++) t += a[i]; return t; }
  public static void Scale(int[] a, int k) { for (int i = 0; i < a.Length; i++) a[i] = a[i] * k; }
}

class Program {
  static int Main() {
    int rc = 0;
    try {
      int[] a = new int[5];
      for (int i = 0; i < a.Length; i++) a[i] = Kernel.Fib(i + 5);
      Console.WriteLine(Kernel.Sum(a));
      Kernel.Scale(a, 3);
      Console.WriteLine(a[0] + " " + a[4]);
      Console.WriteLine(Kernel.Sum(a));
      rc = 3;
    } catch (Exception) {
      rc = 99;
    }
    return rc;
  }
}
