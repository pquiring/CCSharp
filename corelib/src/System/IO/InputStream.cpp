#include <QFile>
#include <QByteArray>

void System::IO::InputStream::OpenInt(int fd) {
  QFile *file = new QFile();
  if (!file->open(fd, QIODevice::ReadOnly | QIODevice::Unbuffered)) {
    throw new System::Exception();
  }
  Value = (void*)file;
}

void System::IO::InputStream::OpenString(System::String *filename) {
  QFile *file = new QFile(QString((QChar*)filename->Value->Array, filename->Value->Length));
  if (!file->open(QIODevice::ReadOnly)) {
    throw new System::Exception();
  }
  Value = (void*)file;
}

int System::IO::InputStream::ReadByteArray(Core::FixedArray$T<uint8> *array) {
  QFile *file = (QFile*)Value;
  return file->read((char*)array->Array, array->Length);
}

System::String* System::IO::InputStream::ReadString() {
  QFile *file = (QFile*)Value;
  QByteArray ba = file->readLine();
  return Core::utf8ToString((const char*)ba.constData());
}

int System::IO::InputStream::AvailableRead() {
  QFile *file = (QFile*)Value;
  return file->bytesAvailable();
}
