/*
  Roslyn from .NET SDK 9 and later insists on System.Reflection.DefaultMemberAttribute for every type that declares an
  indexer (CS0656: "Missing compiler required member"). This corelib is compiled INSTEAD of the .NET reference assemblies, so
  it has to declare the attribute itself, or every program that touches List<T>[i] or string[i] is refused before it starts.
  The attribute carries no meaning in Crust: there is no reflection.
*/
namespace System.Reflection {

  [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
  public sealed class DefaultMemberAttribute : Attribute {
    public DefaultMemberAttribute(string memberName) {}
  }
}
