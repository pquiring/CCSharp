namespace Core {
  void npe() {
    throw new System::NullReferenceException();
  }

  void abe() {
    throw new System::ArrayBoundsException();
  }

  void abe(int idx, int size) {
    throw new System::ArrayBoundsException(idx,size);
  }
}
