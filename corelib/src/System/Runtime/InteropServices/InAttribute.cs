/*
  Roslyn requires System.Runtime.InteropServices.InAttribute to bind any `in` parameter (CS0518: "Predefined type ... is not
  defined or imported"). The corelib replaces the .NET reference assemblies, so it declares it. No meaning in Crust.
*/
namespace System.Runtime.InteropServices {

  [AttributeUsage(AttributeTargets.Parameter)]
  public sealed class InAttribute : Attribute {}
}
