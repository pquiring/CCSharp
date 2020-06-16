namespace System {
  public class SByte {
    static String ToString(sbyte value) {
      char[] buf = new char[4];
      int pos = 3;
      bool neg = value < 0;
      do {
        buf[pos--] = (char)((value % 10) + '0');
        value /= 10;
      } while (value != 0);
      if (neg) {
        buf[pos--] = '-';
      }
      pos++;
      return new String(buf, pos, 4 - pos);
    }
  }
}
