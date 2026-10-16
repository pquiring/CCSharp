using Crust;

namespace System.Diagnostics {
  /* StartNew() is the only way to make one: a .NET Stopwatch does not run until it is started. */
  [Cpp("co::Timer"), CppInclude("\"cs/time.h\"")]
  public sealed class Stopwatch {
    private Stopwatch() {}
    [Cpp("cs_stopwatch_new()")]               public static extern Stopwatch StartNew();
    [Cpp("{this}.restart()")]                 public extern void Restart();
    [Cpp("{this}.restart()")]                 public extern void Start();
    [Cpp("{this}.ms()")]                      public extern long ElapsedMilliseconds { get; }
  }
}
