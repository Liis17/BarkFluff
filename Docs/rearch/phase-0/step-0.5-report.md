# Отчёт по этапу 0.5 — выбор Ed25519-библиотеки для .NET 10

Дата: 2026-07-17. Прототип: консольный проект `dotnet new console -f net10.0` в scratchpad (не в репозитории, не коммитился). Все цифры — фактический прогон на этой машине (`dotnet run -c Release`), не оценки.

## 1. Есть ли Ed25519 в BCL .NET 10?

**Нет.** Проверено через Context7 (`/dotnet/runtime`, документы `System.Security.Cryptography/docs/PostQuantumCrypto.SecurityDesign.md`). В .NET 10 `System.Security.Cryptography` появились **ML-DSA** (FIPS 204, пост-квантовая, классы `MLDsa`/`MLDsaCng`/`MLDsaOpenSsl`) и **Composite ML-DSA** (`CompositeMLDsa`, гибридные схемы вида `MLDsa65WithEd25519`) — это НЕ то же самое, что классический RFC 8032 Ed25519. `CompositeMLDsaAlgorithm.MLDsa65WithEd25519` использует Ed25519 только как один из двух компонентов гибридной подписи (сигнатура = ML-DSA-подпись + Ed25519-подпись, обе проверяются вместе); формат несовместим с обычными Ed25519-подписями сторонних нод и не подходит для протокола, где сигнатура — это чистый 64-байтный Ed25519 blob (RFC 8032, как требуется в [02-trust-and-certs.md](../02-trust-and-certs.md)). Отдельного класса `Ed25519`/`SignatureAlgorithm.Ed25519` в BCL нет.

`Microsoft.Bcl.Cryptography` — полифилл для старых .NET Standard/.NET Framework, на современном .NET 10 не нужен и Ed25519 не добавляет.

**Вывод:** BCL исключён, нужна сторонняя библиотека.

## 2-6. Кандидаты, вердикты

| Кандидат | Версия (NuGet, актуальная) | Лицензия | Managed/Native | Chiseled | RFC 8032 TEST1-3 | sign ops/sec | verify ops/sec | Raw 32-byte export |
|---|---|---|---|---|---|---|---|---|
| **BouncyCastle.Cryptography** | 2.6.2 (31.07.2025) | MIT | 100% managed | **Структурно OK** (см. ниже) | PASS | 21 110 | 14 293 | Да (`GetEncoded()`) |
| **NSec.Cryptography** | 26.4.0 (30.04.2026) | MIT | Native (libsodium via NuGet `libsodium` 1.0.22) | Не проверено эмпирически в этой сессии | PASS | 31 430 | 11 866 | Да (`Export(KeyBlobFormat.RawPrivateKey)`) |
| **Geralt** | 4.3.0 (14.06.2026) | Unlicense | Native (libsodium) | Не проверено, не прогонялся в прототипе | — | — | — | По документации — да (тонкая обёртка libsodium, тот же профиль рисков, что NSec) |
| **Chaos.NaCl** / **Chaos.NaCl.Standard** | 1.0.0 (05.02.2020) | MIT-подобная | Managed | — | — | — | Исключён без бенчмарка: NuGet-пакет `Chaos.NaCl.Standard` не обновлялся с 2020-02-05, единственный релиз, репозиторий неактивен — не проходит критерий поддержки из плана |

### Отклонение от плана: chiseled-проверка не выполнена эмпирически

План требует реально собрать `Dockerfile` на базе `mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled` и прогнать в контейнере (п.3 методики). В этой сессии Docker не использовался по прямому указанию пользователя ("не трогай докер, твоя задача только написать код и проверить сборку"). Это не заменяет требуемую проверку, а осознанно её не выполняет — фиксирую как открытый пункт для Фазы 1 перед реальной интеграцией.

Что можно сказать без контейнера, по чисто архитектурным основаниям:
- **BouncyCastle.Cryptography** — 100% managed C#, ноль P/Invoke, ноль нативных `.so`. Собирается и работает везде, где работает сам .NET runtime — chiseled-риск как категория отсутствует конструктивно, а не "предположительно". Это ровно то, что сам план указывает как решающий довод: «Managed-решение (BouncyCastle) снимает проблему полностью».
- **NSec.Cryptography** / **Geralt** — тянут нативный `libsodium.so` (NuGet-пакет `libsodium` бандлит собранные бинарники per-RID). Обычно это работает и в минимальных образах (self-contained `.so` в publish-выводе), но реальных случаев несовместимости с `*-chiseled` в вебе достаточно (отсутствие `libc`-символов, ICU, иногда `libgcc_s`), чтобы не декларировать "OK" без факта. Это тот самый риск, который просил проверить план — он не снят.

## Производительность

Порог из плана — **≥ 5000 verify/sec на одном ядре**. Оба кандидата, для которых снят бенчмарк, превышают его в 2-6 раз (BouncyCastle: 14 293/sec, NSec: 11 866/sec). Согласно самому плану («если все кандидаты выше порога, производительность перестаёт быть фактором выбора») — **производительность не является решающим критерием**. NSec быстрее на sign (нативный libsodium), BouncyCastle быстрее на verify — обе цифры далеко за пределами реалистичной S2S/presence-нагрузки одной ноды.

## Интероперабельность (RFC 8032 §7.1, TEST 1-3)

Оба кандидата воспроизводят **побайтово идентичную** подпись из референсных векторов RFC 8032 (Ed25519 детерминирован — одинаковый ключ+сообщение всегда даёт одинаковую подпись) и успешно верифицируют собственный и чужой (референсный) результат. Векторы получены двумя независимыми запросами `WebFetch` к `rfc-editor.org/rfc/rfc8032.txt` (первый запрос дал артефакт слипшихся строк из-за суммаризации мелкой моделью — перезапрошен с требованием дословной цитаты по строкам, оба результата в итоге совпали посимвольно) и дополнительно перепроверены через `gh api` по тестовым файлам самих библиотек — `bcgit/bc-csharp` (`crypto/test/src/crypto/test/Ed25519Test.cs`, TEST 1) и `ektrah/nsec` (`tests/Rfc/Ed25519Tests.cs`, TEST 1-5) — оба репозитория используют ровно те же hex-строки. Три независимых источника сошлись — расхождений нет.

## Экспорт/импорт ключей

Оба кандидата отдают/принимают raw 32-байтный seed и raw 32-байтный публичный ключ без ASN.1/PKCS#8-обёртки — подтверждено фактическим запуском (`raw seed length=32 raw pub length=32`), не только по документации:
- BouncyCastle: `Ed25519PrivateKeyParameters.GetEncoded()` / `Ed25519PublicKeyParameters.GetEncoded()`.
- NSec: `Key.Export(KeyBlobFormat.RawPrivateKey)` / `PublicKey.Export(KeyBlobFormat.RawPublicKey)` (требует `KeyCreationParameters.ExportPolicy = KeyExportPolicies.AllowPlaintextExport` — по умолчанию экспорт приватного ключа заблокирован, что само по себе хорошая защита от случайной утечки).

## Выбор: BouncyCastle.Cryptography (2.6.2)

**Обоснование.** Единственный кандидат, который снимает chiseled-риск конструктивно, а не предположительно — критично, поскольку весь бэкенд деплоится из `*-noble-chiseled` и эмпирическая проверка в этой сессии не проводилась. Производительность (14 293 verify/sec) с большим запасом покрывает presence/typing-нагрузку, интероперабельность подтверждена побайтовым совпадением с RFC-векторами из трёх независимых источников, raw-экспорт ключей работает без танцев с ASN.1. NSec/libsodium была бы разумной альтернативой при подтверждённой chiseled-совместимости и более высоком приоритете sign-throughput — но это не тот случай, когда лишняя скорость перевешивает непроверенный риск деплоя.

## Известные ограничения

1. Chiseled-совместимость BouncyCastle не проверена *эмпирически* в этой сессии (только архитектурно — managed-код). Дешёвая, но не сделанная проверка для Фазы 1: `dotnet publish` + `docker build` на `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` + запуск sign/verify внутри.
2. Geralt не прогонялся в прототипе (тот же профиль рисков, что NSec — native libsodium); если Фаза 1 захочет пересмотреть выбор в пользу более высокого throughput, Geralt стоит добавить в сравнение вместе с реальной chiseled-проверкой NSec/Geralt.
3. BouncyCastle 2.3.0 и ранее уязвим к CVE-2024-30172 (зацикливание verify на специально сконструированной подписи/ключе) — исправлено в 2.3.1+; выбранная версия 2.6.2 не подвержена, но при апгрейде в будущем проверять changelog.
4. Бенчмарк — простой Stopwatch-цикл на 10k операций на одном ядре разработческой машины (Windows), не BenchmarkDotNet и не прогон на целевом Linux/chiseled окружении; абсолютные цифры не переносить в SLA буквально, для решающего порога (5000/sec) запас достаточен.

## Ключевые сниппеты (для Фазы 1)

### Генерация пары
```csharp
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

var random = new SecureRandom();
var kpGen = new Ed25519KeyPairGenerator();
kpGen.Init(new KeyGenerationParameters(random, 256));
var kp = kpGen.GenerateKeyPair();
var privateKey = (Ed25519PrivateKeyParameters)kp.Private;
var publicKey = (Ed25519PublicKeyParameters)kp.Public;

byte[] rawSeed = privateKey.GetEncoded(); // 32 байта, raw
byte[] rawPub = publicKey.GetEncoded();   // 32 байта, raw
```

### Подпись
```csharp
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Signers;

ISigner signer = new Ed25519Signer();
signer.Init(true, privateKey);
signer.BlockUpdate(payloadBytes, 0, payloadBytes.Length);
byte[] signature = signer.GenerateSignature(); // 64 байта
```

### Проверка
```csharp
signer.Init(false, publicKey); // тот же Ed25519Signer, либо новый экземпляр
signer.BlockUpdate(payloadBytes, 0, payloadBytes.Length);
bool ok = signer.VerifySignature(signature);
```

### Импорт из raw-байтов (из Configuration / `/.well-known`)
```csharp
var privateKey = new Ed25519PrivateKeyParameters(rawSeedBytes, 0); // 32 байта
var publicKey = new Ed25519PublicKeyParameters(rawPubBytes, 0);    // 32 байта
```

## Пакет для будущего `BarkFluff.Federation`

```xml
<PackageReference Include="BouncyCastle.Cryptography" Version="2.6.2" />
```
Managed, доп. `Grpc.Tools`/native-зависимостей не требует.
