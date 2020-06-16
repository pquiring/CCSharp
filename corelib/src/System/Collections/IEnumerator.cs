using System;

namespace System.Collections {
  public interface IEnumerator {
    bool MoveNext();
    Object Current {
      get;
    }
    void Reset();
  }
  public interface IEnumerator<T> {
    bool MoveNext();
    T Current {
      get;
    }
    void Reset();
  }
}
