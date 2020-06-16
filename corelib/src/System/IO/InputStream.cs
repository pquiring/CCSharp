using System;

namespace System.IO {
  public class InputStream {
    public InputStream(int fd) {
      OpenInt(fd);
    }
    public InputStream(String filename) {
      OpenString(filename);
    }

    public int Read(byte[] array) {
      return ReadByteArray(array);
    }
    public String ReadLine() {
      return null;
    }
    public int Available() {
      return AvailableRead();
    }

    private unsafe void* Value;
    private extern void OpenInt(int fd);
    private extern void OpenString(String filename);
    private extern int ReadByteArray(byte[] array);
    private extern String ReadString();
    private extern int AvailableRead();
  }
}
