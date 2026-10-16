class Engine {
  public int Total;
  public void Run(int n) { for (int i = 1; i <= n; i++) Total += i; }
}
class Util { public static int Triple(int x) { return x * 3 % 256; } }
