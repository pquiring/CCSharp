bool System::Type::IsPrimitive() {
  Core::Class* cls = (Core::Class*)Value;
  return cls->primitive;
}

namespace Core {
  bool IsPrimitive(System::Type* type) {
    return type->IsPrimitive();
  }
}
