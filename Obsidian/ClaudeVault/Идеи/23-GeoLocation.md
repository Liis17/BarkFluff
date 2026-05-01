# 🗺️ Шаринг геолокации в чате

> Категория: Коммуникации / UX
> Платформы: **Android**, **iOS**, macOS (ограничено)
> Приоритет: 🟡 Средний
> Сложность: ⭐⭐⭐

---

## Описание

Отправить **статичную геометку** («Я здесь») или включить **живую геолокацию** (live location) которая обновляется в реальном времени и видна всем участникам чата на карте внутри мессенджера.

---

## Ключевые возможности

- Отправить статичную метку: карточка с картой, адресом и координатами
- Live Location: поделиться геолокацией на 15 мин / 1 час / до отключения
- Все участники чата видят иконку пользователя на живой карте
- Счётчик оставшегося времени прямо в пузыре
- Остановить live location в любой момент
- Открыть в сторонней карте (Google Maps / Яндекс.Карты / Apple Maps / 2GIS)
- Адрес человекочитаемый (reverse geocoding)

---

## Архитектура

### Геолокация как тип сообщения

```protobuf
message LocationContent {
  double latitude = 1;
  double longitude = 2;
  string address = 3;         // от reverse geocoding (на клиенте)
  bool is_live = 4;
  google.protobuf.Timestamp live_expires_at = 5;
  string live_session_id = 6; // для обновлений live location
}
```

### Live Location через Updates стриминг

- Клиент отправляет `UpdateLiveLocation(lat, lng, session_id)` каждые 5 сек
- Новый метод в [[../Backend/Messages]] или отдельный сервис
- [[../Backend/Updates]] транслирует `LiveLocationUpdate` всем участникам чата
- Redis хранит последние координаты `live:{session_id}` с TTL = `live_expires_at`

```protobuf
rpc SendLocation(SendLocationRequest) returns (MessageResponse);
rpc StartLiveLocation(StartLiveLocationRequest) returns (LiveLocationResponse);
rpc UpdateLiveLocation(UpdateLiveLocationRequest) returns (Empty);   // client streaming
rpc StopLiveLocation(StopLiveLocationRequest) returns (Empty);
```

---

## Изменения в существующих сервисах

| Сервис | Изменение |
|--------|-----------|
| [[../Backend/Messages]] | Тип сообщения `LOCATION`, методы Send/Start/Stop |
| [[../Backend/Updates]] | Событие `LiveLocationUpdate` |
| [[../Shared/Proto]] | `LocationContent`, методы в `messages.proto` |

---

## Клиентские особенности

### Android

```kotlin
// FusedLocationProviderClient (Google Play Services)
val fusedClient = LocationServices.getFusedLocationProviderClient(context)

// Для live location — WorkManager + ForegroundService
// android.permission.ACCESS_FINE_LOCATION
// android.permission.FOREGROUND_SERVICE_LOCATION (Android 14+)

// Карта — Yandex MapKit или OSMDroid (без Google зависимости)
// Reverse geocoding — Nominatim API (бесплатный, self-hosted опционально)
```

### iOS / macOS

```swift
// CLLocationManager + CLLocationManagerDelegate
// MapKit для отображения карты (SwiftUI Map{})
// MKLocalSearch для reverse geocoding (нативный, бесплатный)
locationManager.requestWhenInUseAuthorization()
locationManager.startUpdatingLocation()
```

---

## UI — карточка геолокации

```
┌─────────────────────────────┐
│  [  🗺️ Карта (статика)   ]  │
│                             │
│  📍 ул. Арбат, 10, Москва   │
│  55.7522° N, 37.5960° E     │
│                             │
│  [ Открыть в картах ↗ ]     │
└─────────────────────────────┘
```

Live location — дополнительно: аватар движется по карте, «Иван делится геолокацией · осталось 45 мин».

