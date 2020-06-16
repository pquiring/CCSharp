using System;

namespace System.IO {
  public class OutputStream {
    public OutputStream(int fd) {
      OpenInt(fd);
    }
    public OutputStream(String filename) {
      OpenString(filename);
    }
    public int Write(byte[] array) {
      return WriteByteArray(array);
    }

    private unsafe void* Value;
    private extern void OpenInt(int fd);
    private extern void OpenString(String filename);
    private extern int WriteByteArray(byte[] array);
  }
}
