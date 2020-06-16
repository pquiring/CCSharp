using System;

namespace System.IO {
  public class TextOutputStream : OutputStream {
    public TextOutputStream(int fd) : base(fd) {
    }
    private static byte[] eol = {(byte)'\r', (byte)'\n'};
    public void WriteLine(String str) {
      Write(str.ToByteArray());
      Write(eol);
    }
    public void Write(String str) {
      Write(str.ToByteArray());
    }
  }
}
