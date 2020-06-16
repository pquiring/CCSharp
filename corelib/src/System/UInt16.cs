namespace System {
  public class UInt16 {
    static String ToString(ushort value) {
      char[] buf = new char[5];
      int pos = 4;
      do {
        buf[pos--] = (char)((value % 10) + '0');
        value /= 10;
      } while (value != 0);
      pos++;
      return new String(buf, pos, 5 - pos);
    }
  }
}
