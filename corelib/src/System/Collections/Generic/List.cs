using Crust;

namespace System.Collections.Generic {

  /* List<T> is Crust's vector<T>.  Indexing is unchecked, as in C. */
  [Cpp("std::vector<{T0}>")]
  public sealed class List<T> : IEnumerable<T> {
    public List() {}
    public List(int capacity) {}                          // (a hint: the list is built empty -- see CrustEmitter.CollectionCtor)
    public List(List<T> collection) {}                    // (a copy of another List<T> whose elements are not owned classes)
    [Cpp("(int){this}.size()")]                public extern int Count { get; }
    public extern T this[int index] { get; set; }
    // set-only on purpose: assigning is a real `reserve` (a hint a program cannot see), and there is no capacity to read back that .NET and C would agree on
    [CppSet("{this}.reserve({0})")]            public extern int Capacity { set; }
    [Cpp("{this}.push_back({0})")]             public extern void Add(T item);
    [Cpp("{this}.clear()")]                    public extern void Clear();

    // `foreach` is lowered to a range-for by the emitter; the enumerator exists so that the C# compiler binds it.
    public extern Enumerator GetEnumerator();
    extern IEnumerator<T> IEnumerable<T>.GetEnumerator();
    extern System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator();
    public struct Enumerator : IEnumerator<T> {
      public extern T Current { get; }
      extern object System.Collections.IEnumerator.Current { get; }
      public extern bool MoveNext();
      public extern void Reset();
      public extern void Dispose();
    }

    // (an index out of range is unchecked, as indexing is: C# throws, here it is the C vector's behaviour)
    [Cpp("{this}.erase({this}.begin() + {0})")]                        public extern void RemoveAt(int index);
    [Cpp("{this}.insert({this}.begin() + {0}, {1})")]                  public extern void Insert(int index, T item);

    [Cpp("__cs_reverse_{M0}({this})", Helper = "static void __cs_reverse_{M0}(std::vector<{T0}> &v) { int i = 0; int j = (int)v.size() - 1; while (i < j) { {T0} t = v[i]; v[i] = v[j]; v[j] = t; i = i + 1; j = j - 1; } }")]
    public extern void Reverse();
    public extern bool Contains(T item);                  // not implemented yet
    public extern bool Remove(T item);
    public extern int IndexOf(T item);
    public extern void Sort();
    public extern void Sort(Comparison<T> comparison);                // not implemented: it calls a delegate
    public extern void Sort(IComparer<T> comparer);                   // not implemented: it calls through an interface
    public extern T[] ToArray();
  }

  /* Stack<T> is Crust's vector<T> used from the back.  Peek and Pop of an empty stack are unchecked, as indexing is: C# throws, here it is the C vector's behaviour.
     There is no enumerator: .NET enumerates a stack from the top, a range-for over a vector goes from the bottom, so `foreach` is not offered (it does not bind). */
  [Cpp("std::vector<{T0}>")]
  public sealed class Stack<T> {
    public Stack() {}
    [Cpp("(int){this}.size()")]                public extern int Count { get; }
    [Cpp("{this}.push_back({0})")]             public extern void Push(T item);
    [Cpp("{this}[(int){this}.size() - 1]")]    public extern T Peek();
    [Cpp("__cs_stack_pop_{M0}({this})", Helper = "static {T0} __cs_stack_pop_{M0}(std::vector<{T0}> &v) { {T0} x = v[(int)v.size() - 1]; v.pop_back(); return x; }")]
    public extern T Pop();
    [Cpp("{this}.clear()")]                    public extern void Clear();
  }

  /* Dictionary<K,V> is Crust's ordered map<K,V>.  Reading a missing key is unchecked (it inserts a default). */
  [Cpp("std::map<{T0}, {T1}>")]
  public sealed class Dictionary<TKey, TValue> {
    public Dictionary() {}
    [Cpp("(int){this}.size()")]                       public extern int Count { get; }
    public extern TValue this[TKey key] { get; set; }
    [Cpp("({this}.count({0}) > 0)")]                  public extern bool ContainsKey(TKey key);
    [Cpp("({this}.erase({0}) > 0)")]                  public extern bool Remove(TKey key);
    [Cpp("{this}.clear()")]                           public extern void Clear();

    public extern ICollection<TValue> Values { get; }     // not implemented yet
    public extern ICollection<TKey> Keys { get; }
    public extern void Add(TKey key, TValue value);       // not implemented yet
    public extern bool TryGetValue(TKey key, out TValue value);
  }
}
