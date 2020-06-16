using System;

namespace System.Collections {
  public interface IEnumerable {
    IEnumerator GetEnumerator();
  }
  public interface IEnumerable<T> {
    IEnumerator<T> GetEnumerator();
  }
}
