using System.Collections.Generic;

static class Scripts {
  static Early[] pool;
  static List<int> free;
  static int high;
  public static void Init() { pool = new Early[8]; free = new List<int>(); high = 0; }
  public static Early AddEarly() {
    int slot;
    if (free.Count > 0) { slot = free[free.Count - 1]; free.RemoveAt(free.Count - 1); } else { slot = high; high++; }
    Early s = pool[slot];
    if (s == null) { s = new Early(); pool[slot] = s; } else s.Reset();
    s.On = slot + 1;
    return s;
  }
  public static void Free(Early e) { free.Add(e.On - 1); }
  public static int Count() { return high - free.Count; }
}
