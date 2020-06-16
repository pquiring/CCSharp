using System;

namespace System.IO {
  public class TextInputStream : InputStream {
    public TextInputStream(int fd) : base(fd) {
    }
  }
}
