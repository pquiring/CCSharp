static class Log {
  public static int Count;
  static int rng = 12345;
  public static int Next() { rng = rng * 1103515245 + 12345; return (rng >> 8) & 0xffff; }
  public static int Summarise(int done) { return (done + Count) & 0x7f; }
}
