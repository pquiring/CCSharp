namespace System {
  public class Int32 {
    static String ToString(int value) {
      char[] buf = new char[11];
      int pos = 10;
      bool neg = value < 0;
      do {
        buf[pos--] = (char)((value % 10) + '0');
        value /= 10;
      } while (value != 0);
      if (neg) {
        buf[pos--] = '-';
      }
      pos++;
      return new String(buf, pos, 11 - pos);
    }
  }
}
