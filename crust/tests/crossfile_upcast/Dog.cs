// The base list lives in THIS file; the upcast that needs it is in Prog.cs. The emitter used to resolve this file's syntax with
// Prog.cs's semantic model and crash: "Syntax node is not within syntax tree".
class Dog : Animal {
  public override int Sound() { return 2; }
}
class Puppy : Dog {
  public override int Sound() { return 3; }
}
