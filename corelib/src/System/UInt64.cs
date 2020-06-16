namespace System {
  public class UInt64 {
    static String ToString(ulong value) {
      char[] buf = new char[20];
      int pos = 19;
      do {
        buf[pos--] = (char)((value % 10) + '0');
        value /= 10;
      } while (value != 0);
      pos++;
      return new String(buf, pos, 20 - pos);
    }
  }
}
