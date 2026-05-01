# 🎙️ Голосовые и видеозвонки (P2P / SFU)

> Категория: Коммуникации
> Приоритет: 🔴 Высокий
> Сложность: ⭐⭐⭐⭐⭐

---

## Описание

Добавить полноценные **1-on-1 голосовые и видеозвонки**, а также **групповые звонки** (до N участников) прямо внутри мессенджера BarkFluff. Архитектурно это отдельный микросервис `Signaling` + медиа-сервер (SFU).

---

## Ключевые возможности

- Голосовой звонок 1-on-1
- Видеозвонок 1-on-1
- Групповые звонки (до 20-50 участников)
- Демонстрация экрана (screen share)
- Blur/замена фона в видеозвонке
- Запись звонка (опционально, с согласия)
- Индикатор качества соединения

---

## Архитектура

```
Клиент A ──┐                        ┌── Клиент B
           ▼                        ▼
      WebRTC (ICE/STUN/TURN)
           │
    BarkFluff.Signaling (новый микросервис, порт 7025)
           │  gRPC streaming — offer/answer/ICE candidates
           │
    SFU-сервер (mediasoup / Janus / LiveKit)
```

- **Signaling** — новый gRPC-сервис: методы `InitiateCall`, `AcceptCall`, `RejectCall`, `EndCall`, `ExchangeIce`
- **SFU** — LiveKit (Go, self-hosted) или mediasoup (Node.js) для групповых звонков
- **P2P** — для 1-on-1 через WebRTC без SFU
- **Updates** сервис расширяется событиями `IncomingCall`, `CallEnded`, `CallMissed`
- **Notification** — push через Firebase при входящем звонке (CloudMessaging)

---

## Изменения в существующих сервисах

| Сервис | Изменение |
|--------|-----------|
| [[../Backend/Updates]] | Новые события: IncomingCall, CallState |
| [[../Backend/CloudMessaging]] | Push при входящем вызове (высокий приоритет) |
| [[../Shared/Proto]] | Новый `signaling.proto` |
| [[../Клиенты/Android]] | WebRTC Android SDK, новый экран звонка |
| [[../Клиенты/Windows-WPF]] | WebRTC .NET биндинг или нативный экран |

---

## Клиентские экраны

- `CallActivity` / `CallScreen` — полноэкранный UI звонка
- Входящий звонок overlay поверх любого экрана
- Миникарточка активного звонка в списке чатов

---

## Зависимости

- `LiveKit Server` (Docker) или `mediasoup`
- WebRTC SDK: Android (`io.getstream:webrtc-android`), Swift (WebRTC.framework)
- STUN/TURN серверы (coturn self-hosted)
