namespace System {
  public class Int16 {
    static String ToString(long value) {
      char[] buf = new char[6];
      int pos = 5;
      bool neg = value < 0;
      do {
        buf[pos--] = (char)((value % 10) + '0');
        value /= 10;
      } while (value != 0);
      if (neg) {
        buf[pos--] = '-';
      }
      pos++;
      return new String(buf, pos, 6 - pos);
    }
  }
}
