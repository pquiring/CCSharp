#include <QFile>

void System::IO::OutputStream::OpenInt(int fd) {
  QFile *file = new QFile();
  if (!file->open(fd, QIODevice::WriteOnly | QIODevice::Text | QIODevice::Unbuffered)) {
    throw new System::Exception();
  }
  Value = (void*)file;
}

void System::IO::OutputStream::OpenString(System::String *filename) {
  QFile *file = new QFile(QString((QChar*)filename->Value->Array, filename->Value->Length));
  if (!file->open(QIODevice::WriteOnly)) {
    throw new System::Exception();
  }
  Value = (void*)file;
}

int System::IO::OutputStream::WriteByteArray(Core::FixedArray$T<uint8> *array) {
  QFile *file = (QFile*)Value;
  return file->write((char*)array->Array, array->Length);
}
