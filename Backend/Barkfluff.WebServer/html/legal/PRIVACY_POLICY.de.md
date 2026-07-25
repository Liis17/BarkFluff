# BarkFluff Datenschutzerklärung

**Gültig ab:** 24. Januar 2026  
**Letzte Aktualisierung:** 17. Juni 2026

> Diese Übersetzung dient nur der Bequemlichkeit. Im Falle von Abweichungen ist die russische Fassung maßgebend.

## 1. Einleitung

Diese Erklärung beschreibt die Daten, die in der aktuellen BarkFluff-Plattform verarbeitet werden: Konto, Profil, Geräte, Nachrichten, Dateien, Client-Versionen, öffentliche Profilseiten und der Support-Chat auf der Website.

## 2. Welche Daten verarbeitet werden

### 2.1 Konto und Profil

| Daten | Verwendung | Speicherung |
| --- | --- | --- |
| E-Mail | Registrierung, Anmeldung, Bestätigungen, Zugangswiederherstellung, Benachrichtigungen | Users / Identity |
| Benutzername | Anmeldung, Suche, öffentliche Profilseite, Anzeige in Chats | Users |
| Passwort-Hash | Passwortprüfung; das Passwort im Klartext wird nicht gespeichert | Identity |
| Vorname, Nachname, Bio | Benutzerprofil und öffentliche Profildaten | Users |
| Avatar und Profil-Poster | Gestaltung des Profils und der öffentlichen Seite | Files / Minio + Users-Metadaten |
| Datenschutz- und Personalisierungseinstellungen | Anzeige der Profildaten und Erscheinungsbild des Clients | Users |

### 2.2 Geräte und Sitzungen

| Daten | Verwendung | Speicherung |
| --- | --- | --- |
| Device ID | Bindung des Refresh-Tokens, Liste aktiver Sitzungen, Widerruf einer Sitzung | Identity.RefreshTokens, Users.UserDevices |
| Gerätename | Anzeige und Umbenennung des Geräts | Users.UserDevices |
| Betriebssystem, App-Name | Client-Kompatibilität und Geräteliste | Users.UserDevices |
| Location | Anzeige des Geräts in der Sitzungsliste | Users.UserDevices |
| Firebase-Token | Push-Benachrichtigungen über Firebase Cloud Messaging | Users.UserDevices |
| Refresh-Token | Verlängerung der Sitzung und Abmeldung vom Gerät | Identity.RefreshTokens |

Die IP-Adresse wird in den gRPC-Service-Metadaten-Headern übertragen und zur Verarbeitung der aktuellen Anfragen verwendet.

### 2.3 Nachrichten, Chats und Dateien

| Daten | Verwendung | Speicherung |
| --- | --- | --- |
| Reguläre Nachrichten | Einzel- und Gruppenchats, Synchronisierung, Read Receipts, Bearbeitung und Löschung | Messages / PostgreSQL |
| Private verschlüsselte Chats | 1-zu-1-Chats mit clientseitiger Verschlüsselung | Messages speichert Chiffretext und Chat-Metadaten |
| Geheime Chats | 1-zu-1-Chats zwischen bestimmten Geräten | Der Server leitet den Envelope weiter und puffert ihn temporär |
| Anhänge und Vorschauen | Übertragung von Dateien, Bildern, Videos, Dokumenten, Audio und Avataren | Files / Minio + PostgreSQL-Metadaten |
| Lesestatus | Anzeige gelesener Nachrichten | Messages |

### 2.4 Online-Status und Updates

Onliner speichert und liefert den Online-Status und die Zeit der letzten Aktivität. Updates übermittelt Echtzeit-Ereignisse per gRPC-Streaming an die Clients.

### 2.5 Website und Support

WebServer liefert die Startseite, öffentliche Profilseiten, Legal-Seiten, Installationsskripte, Client-Versionen und die öffentliche Profil-REST-API. Nachrichten aus dem Support-Formular der Website werden im Speicher des WebServer-Prozesses gehalten und über die Telegram Bot API an den Administrator weitergeleitet.

Beim Klick auf „Im Browser schreiben" auf einer öffentlichen Profilseite erzeugt WebServer ein kurzlebiges Cookie `bf_open_chat`, damit der Web-Client den Chat mit dem ausgewählten Benutzer öffnet.

## 3. Wofür die Daten verwendet werden

- Registrierung, Anmeldung, 2FA, Passwortwiederherstellung und Sitzungsverwaltung;
- Betrieb von Profilen, Benutzersuche, öffentlichen Seiten und Datenschutzeinstellungen;
- Zustellung von Nachrichten, Dateien, Read Receipts, Echtzeit-Updates und Push-Benachrichtigungen;
- Export der Benutzerdaten über `Users.ExportData`;
- Benutzersupport über die Website, E-Mail und den Telegram-Bot;
- Protokollierung von Fehlern, Metriken und Sicherheitsereignissen.

## 4. Datenschutz

### 4.1 Datenübertragung

- Clients und Microservices verwenden gRPC/HTTP/2 und HTTPS/TLS am äußeren Perimeter.
- Die Autorisierung von gRPC-Anfragen verwendet XAuth-Metadaten: `x-auth-token`, `x-device-id`, `x-device-name`, `x-ip`, `x-os`, `x-app-name`, `x-app-version`.
- Interne asynchrone Ereignisse laufen über RabbitMQ / MassTransit.

### 4.2 Passwörter, Tokens und 2FA

- Passwörter werden als Hashes gespeichert.
- Access Token — ein JWT im Header `x-auth-token`; die Lebensdauer wird durch die Identity-Konfiguration festgelegt.
- Der Refresh-Token ist an die Device ID gebunden und wird in Identity gespeichert.
- 2FA unterstützt TOTP-Authenticator und E-Mail-Codes.

### 4.3 Verschlüsselte Chats

- **Private Chats:** der Server speichert den Chiffretext `EncryptedMessage`, Nonce und AAD; der Schlüssel wird auf dem Client aus der Passphrase und dem Chat-Salt abgeleitet.
- **Geheime Chats:** der Server arbeitet mit einem an Geräte gebundenen opaken Envelope, leitet ihn an den Empfänger weiter und puffert ihn mit einer TTL von 24 Stunden.
- **Reguläre Chats:** Nachrichten werden im Messages-Dienst gespeichert und sind der Serverlogik zugänglich, die Synchronisierung, Export und Chat-Funktionen bereitstellt.

## 5. Weitergabe an Dritte

Wir verkaufen keine personenbezogenen Daten. Daten werden an externe Anbieter nur zum Betrieb der aktuellen Funktionen weitergegeben:

- **SMTP:** E-Mail-Benachrichtigungen, Bestätigungscodes, Zugangswiederherstellung.
- **Firebase Cloud Messaging:** Push-Benachrichtigungen an Geräte mit Firebase-Token.
- **Telegram Bot API:** Verarbeitung von Nachrichten aus dem Support-Chat der Website.
- **Hosting und Infrastruktur:** Betrieb von Diensten, Datenbanken, Dateispeicher, RabbitMQ, Redis und Seq.

## 6. Rechte der Nutzer

- **Zugang und Export:** der Export liefert die JSON-Dateien `profile.json`, `messages.json` und `files.json`.
- **Berichtigung:** Profil, Bio, Avatar, Poster, Datenschutz, Geräte und Passwort können über die Client-Funktionen geändert werden, soweit verfügbar.
- **Löschung:** einen Antrag auf Löschung des Kontos und der zugehörigen personenbezogenen Daten können Sie an privacy@barkfluff.com senden.
- **Widerruf einer Sitzung:** eine aktive Sitzung kann über die Geräteverwaltung beendet werden.

## 7. Löschung von Nachrichten und Daten

- Reguläre Nachrichten können über die Messages-API gelöscht werden.
- Private verschlüsselte Nachrichten werden beim Löschen als gelöscht markiert und der Chiffretext wird entfernt.
- Geheime Nachrichten werden nach Bestätigung der Zustellung oder Ablauf der TTL aus dem temporären Puffer entfernt.
- Für die Löschung eines Kontos und von Daten, die nicht durch Client-Funktionen abgedeckt sind, verwenden Sie eine Support-Anfrage.

## 8. Cookies und lokale Speicherung

- Native Clients verwenden lokalen Speicher für Tokens, Einstellungen und Caches.
- Der Windows-Client speichert `GlobalParam.json` und kann diese Datei mit einer PIN schützen.
- Der macOS-Client verwendet für Tokens die Keychain.
- WebServer verwendet das Cookie `bf_open_chat` ausschließlich für den Übergang von einer öffentlichen Profilseite zum Web-Client.

## 9. Kinder

BarkFluff ist nicht für Nutzer unter 13 Jahren bestimmt. Wenn Sie glauben, dass ein Kind unter 13 Jahren ein Konto erstellt hat, kontaktieren Sie uns: privacy@barkfluff.com.

## 10. Änderungen der Erklärung

Bei Änderungen dieser Erklärung wird das Datum „Aktualisiert" angepasst. Über wesentliche Änderungen kann über die App oder per E-Mail informiert werden.

## 11. Kontakt

- Datenschutz: privacy@barkfluff.com
- Support: support@barkfluff.com
- Sicherheit: security@barkfluff.com
- Website: https://barkfluff.com
