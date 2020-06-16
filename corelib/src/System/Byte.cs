namespace System {
  public class Byte {
    static String ToString(byte value) {
      char[] buf = new char[3];
      int pos = 2;
      do {
        buf[pos--] = (char)((value % 10) + '0');
        value /= 10;
      } while (value != 0);
      pos++;
      return new String(buf, pos, 3 - pos);
    }
  }
}
