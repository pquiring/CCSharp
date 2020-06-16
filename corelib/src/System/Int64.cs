namespace System {
  public class Int64 {
    static String ToString(long value) {
      char[] buf = new char[20];
      int pos = 19;
      bool neg = value < 0;
      do {
        buf[pos--] = (char)((value % 10) + '0');
        value /= 10;
      } while (value != 0);
      if (neg) {
        buf[pos--] = '-';
      }
      pos++;
      return new String(buf, pos, 20 - pos);
    }
  }
}
