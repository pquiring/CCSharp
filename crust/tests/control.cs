using System;
class Control {
  static int Fib(int n) { if (n < 2) return n; return Fib(n - 1) + Fib(n - 2); }
  static int Collatz(int n) {
    int steps = 0;
    while (n != 1) {
      if (n % 2 == 0) n /= 2; else n = 3 * n + 1;
      steps++;
    }
    return steps;
  }
  public static int Main() {
    int sum = 0;
    for (int i = 0; i < 10; i++) {
      if (i == 3) continue;
      if (i == 8) break;
      sum += i;
    }
    Console.WriteLine(sum);
    int j = 0;
    do { j += 2; } while (j < 9);
    Console.WriteLine(j);
    for (int x = 0, y = 10; x < y; x += 3, y -= 2) Console.WriteLine($"x={x} y={y}");
    Console.WriteLine(Fib(15));
    Console.WriteLine(Collatz(27));
    int k = 5;
    return k > 4 ? 1 : 0;
  }
}
