namespace System.Runtime.Versioning {
  public class TargetFrameworkAttribute: Attribute {
    public String FrameworkDisplayName { get; set; }
    public TargetFrameworkAttribute(String a) {
    }
  }
}
