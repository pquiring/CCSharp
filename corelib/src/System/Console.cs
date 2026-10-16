namespace System {

  /*
    Console.Write / WriteLine are intrinsics: the emitter lowers them to one printf (see CrustEmitter.ConsoleCall),
    because the format string is built from the C# argument's *type*.  They accept strings, ints, bools and
    interpolated strings; the overloads that exist only so that other code binds (double, char, object) are refused
    at the C# line with the reason.
  */
  public static class Console {
    public static extern void WriteLine();
    public static extern void WriteLine(string value);
    public static extern void WriteLine(int value);
    public static extern void WriteLine(uint value);
    public static extern void WriteLine(long value);
    public static extern void WriteLine(ulong value);
    public static extern void WriteLine(bool value);
    public static extern void WriteLine(double value);
    public static extern void WriteLine(char value);
    public static extern void WriteLine(object value);
    public static extern void WriteLine(string format, object arg0);

    public static extern void Write(string value);
    public static extern void Write(int value);
    public static extern void Write(uint value);
    public static extern void Write(long value);
    public static extern void Write(ulong value);
    public static extern void Write(bool value);
    public static extern void Write(double value);
    public static extern void Write(char value);
    public static extern void Write(object value);

    public static extern string ReadLine();                  // not implemented yet
  }
}
