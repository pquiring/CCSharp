namespace System {
  public class AttributeUsageAttribute : Attribute {
    public AttributeUsageAttribute() {}
    public AttributeUsageAttribute(AttributeTargets targets) {}
    public bool AllowMultiple
      {
        get { return false; }
        set { }
      }

    public bool Inherited
      {
        get { return false; }
        set { }
      }
  }
}
