namespace System {
  public class Exception {
    public String msg;
    public Exception() {}
    public Exception(String msg) {
      this.msg = msg;
    }
    public String GetMessage() {
      return msg;
    }
  }
}
