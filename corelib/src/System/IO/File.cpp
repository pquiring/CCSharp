#include <QFileInfo>
#include <QFile>
#include <QDir>

void System::IO::File::Create(System::String* filename) {
  Value = (void*)new QFileInfo(QString((QChar*)filename->Value->Array, filename->Value->Length));
}

void System::IO::File::Destroy() {
  QFileInfo* fileinfo = (QFileInfo*)Value;
  delete fileinfo;
}

bool System::IO::File::Exists() {
  QFileInfo* fileinfo = (QFileInfo*)Value;
  return fileinfo->exists();
}

bool System::IO::File::IsDirectory() {
  QFileInfo* fileinfo = (QFileInfo*)Value;
  return fileinfo->isDir();
}

bool System::IO::File::Copy(System::String* dest) {
  QFileInfo* fileinfo = (QFileInfo*)Value;
  QFile file(fileinfo->filePath());
  return file.copy(QString((QChar*)dest->Value->Array, dest->Value->Length));
}

Core::FixedArray$T<System::IO::File*>* System::IO::File::List() {
  QFileInfo* fileinfo = (QFileInfo*)Value;
  if (!fileinfo->isDir()) return nullptr;
  QDir dir = fileinfo->dir();
  QFileInfoList list = dir.entryInfoList();
  int length = list.size();
  Core::FixedArray$T<System::IO::File*>* filelist = new(length) Core::FixedArray$T<System::IO::File*>(&Core::Type_System_IO_File);
  for(int i=0;i<length;i++) {
    filelist->at(i) = new System::IO::File((void*)(new QFileInfo(list.at(i))));
  }
  return filelist;
}

bool System::IO::File::MakeFolder() {
  QFileInfo* fileinfo = (QFileInfo*)Value;
  if (!fileinfo->isDir()) return false;
  QDir dir = fileinfo->dir();
  return dir.mkdir(".");
}

bool System::IO::File::MakeFolders() {
  QFileInfo* fileinfo = (QFileInfo*)Value;
  if (!fileinfo->isDir()) return false;
  QDir dir = fileinfo->dir();
  return dir.mkpath(".");
}

bool System::IO::File::DeleteFolder() {
  QFileInfo* fileinfo = (QFileInfo*)Value;
  if (!fileinfo->isDir()) return false;
  QDir dir = fileinfo->dir();
  return dir.rmdir(".");
}
