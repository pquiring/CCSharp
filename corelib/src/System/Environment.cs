using Crust;

namespace System {
  public static class Environment {
    public const string NewLine = "\n";
    [Cpp("cs_tick_ms()"), CppInclude("\"cs/time.h\"")]  public static extern long TickCount64 { get; }
    [Cpp("exit({0})"), CppInclude("<stdlib.h>")]        public static extern void Exit(int exitCode);
  }
}
