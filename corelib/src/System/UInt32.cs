namespace System {
  public class UInt32 {
    static String ToString(ulong value) {
      char[] buf = new char[10];
      int pos = 9;
      do {
        buf[pos--] = (char)((value % 10) + '0');
        value /= 10;
      } while (value != 0);
      pos++;
      return new String(buf, pos, 10 - pos);
    }
  }
}
