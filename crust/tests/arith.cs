using System;
class Arith {
  public static int Main() {
    int a = 7, b = 3;
    Console.WriteLine(a + b);
    Console.WriteLine(a - b);
    Console.WriteLine(a * b);
    Console.WriteLine(a / b);
    Console.WriteLine(a % b);
    Console.WriteLine(-a / b);
    Console.WriteLine(-a % b);
    Console.WriteLine(a << 2);
    Console.WriteLine(a >> 1);
    Console.WriteLine(1 << 33);          // count masked: 1 << 1
    int s = 40;
    Console.WriteLine(1 << s);           // masked at run time: 1 << 8
    Console.WriteLine(a & b);
    Console.WriteLine(a | b);
    Console.WriteLine(a ^ b);
    Console.WriteLine(~a);
    int big = int.MaxValue;
    big = big + 1;                       // wraps
    Console.WriteLine(big);
    long l = 3000000000L;
    Console.WriteLine(l * 4);
    uint u = 4000000000;
    Console.WriteLine(u + 500000000u);   // wraps
    byte by = 250;
    by += 10;                            // wraps to 4
    Console.WriteLine(by);
    short sh = 32767;
    sh++;
    Console.WriteLine(sh);
    Console.WriteLine(int.MinValue);
    Console.WriteLine(ulong.MaxValue);
    bool t = a > b && b > 0;
    Console.WriteLine(t);
    Console.WriteLine(!t);
    return 0;
  }
}
