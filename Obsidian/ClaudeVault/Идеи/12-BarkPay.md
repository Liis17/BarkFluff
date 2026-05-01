# 💸 BarkPay — встроенные переводы и платежи

> Категория: Монетизация / FinTech
> Приоритет: 🔴 Сложный, но высокоценный
> Сложность: ⭐⭐⭐⭐⭐

---

## Описание

**BarkPay** — встроенная платёжная система внутри мессенджера. Пользователи могут переводить деньги друг другу прямо в чате, оплачивать товары в Mini-Apps ([[03-Bots]]), делить счёт в группах. Монетизация платформы через комиссию с транзакций.

---

## Ключевые возможности

### Переводы
- Отправить деньги пользователю прямо в чате (как сообщение)
- Запросить деньги (request money) — отправить «счёт» в чат
- Split Bill — разделить сумму на N участников группы
- История транзакций с фильтрацией
- Мгновенный перевод (между пользователями BarkFluff — бесплатно)

### Кошелёк
- Внутренний кошелёк BarkFluff Balance
- Пополнение через банковскую карту / крипту / СБП (Россия)
- Вывод на карту
- Мультивалютный (RUB / USD / EUR / USDT)

### Бизнес-возможности
- Оплата в ботах / Mini-Apps через `BarkFluff.pay(amount, description)`
- Донаты на каналах ([[06-Channels]])
- Платные подписки на каналы (recurring payments)
- Инвойсы для бизнес-аккаунтов

---

## Архитектура

```
Новый микросервис: BarkFluff.Payments (порт 7060)
     │
     ├── PostgreSQL — кошельки, транзакции (ACID!)
     ├── Redis — блокировки на транзакции (distributed lock)
     ├── Внешние платёжные шлюзы: Stripe, ЮKassa, USDT (TRC-20)
     └── RabbitMQ — события PaymentCompleted → Messages, Notification
```

### Безопасность транзакций

```
Двойная запись (Double-Entry Bookkeeping):
  DEBIT  sender_wallet  -100 RUB
  CREDIT receiver_wallet +100 RUB
  → атомарная транзакция PostgreSQL
```

- PIN-код или биометрия для подтверждения платежа
- Лимиты на транзакции (дневной, разовый)
- 2FA обязательна для вывода средств
- Полный audit log всех операций (неизменяемый)
- PCI DSS — никогда не хранить CVV/номера карт (только токены шлюза)

### gRPC методы

```protobuf
rpc GetWallet(GetWalletRequest) returns (WalletResponse);
rpc SendMoney(SendMoneyRequest) returns (TransactionResponse);
rpc RequestMoney(RequestMoneyRequest) returns (PaymentRequestResponse);
rpc TopUpWallet(TopUpRequest) returns (TopUpResponse);
rpc WithdrawFunds(WithdrawRequest) returns (WithdrawResponse);
rpc GetTransactionHistory(GetTransactionHistoryRequest) returns (TransactionHistoryResponse);
```

---

## Изменения в существующих сервисах

| Сервис | Изменение |
|--------|-----------|
| [[../Backend/Messages]] | Новый тип сообщения `PAYMENT` (карточка перевода в пузыре) |
| [[../Backend/Notification]] | Email-квитанция после транзакции |
| [[../Backend/Identity]] | Обязательная верификация (KYC) для вывода средств |
| [[../Backend/CloudMessaging]] | Push при получении перевода |
| [[../Shared/Proto]] | `payments.proto` |
| [[../Shared/Queue]] | `PaymentCompletedEvent`, `PaymentRequestedEvent` |

---

## UI

- Кнопка 💸 в тулбаре набора сообщений
- Карточка платежа в чате: сумма, получатель, статус (✅ Завершено / ⏳ Ожидает)
- Экран кошелька: баланс, кнопки «Пополнить» / «Вывести» / «Отправить»
- История транзакций с иконками категорий
- Экран подтверждения с биометрией (Face ID / Fingerprint)
- Анимация «монетки летят» при отправке 🎉

---

## Правовые аспекты

- Лицензия платёжного оператора (зависит от юрисдикции)
- KYC (Know Your Customer) при превышении лимитов
- AML (Anti-Money Laundering) мониторинг
- Отдельная юридическая сущность для платёжного сервиса
