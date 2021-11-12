using System.IO;
using Core;

namespace System {
  public class Console {
    public static TextInputStream In = new TextInputStream(0);
    public static TextOutputStream Out = new TextOutputStream(1);
    public static TextOutputStream Error = new TextOutputStream(2);

    public static void WriteLine(String str) {
      Out.WriteLine(str);
    }
    public static void Write(String str) {
      Out.Write(str);
    }
    public static String ReadLine() {
      return In.ReadLine();
    }

    public static void Enable() {
      OS.ConsoleEnable();
    }
    public static void Disable() {
      OS.ConsoleDisable();
    }
    public int GetWidth() {
      return OS.ConsoleWidth();
    }
    public int GetHeight() {
      return OS.ConsoleHeight();
    }
    public int GetPositionX() {
      return OS.ConsolePositionX();
    }
    public int GetPositionY() {
      return OS.ConsolePositionY();
    }
  }
}
