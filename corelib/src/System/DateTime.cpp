#include <QDateTime>

int64 System::DateTime::CurrentTimeEpoch() {
  return QDateTime::currentMSecsSinceEpoch();
}
