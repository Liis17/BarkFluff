# BarkFluff Datenschutzerklärung

**Gültig ab:** 24. Januar 2026  
**Letzte Aktualisierung:** 29. Juli 2026

> Diese Übersetzung dient nur der Bequemlichkeit. Im Falle von Abweichungen ist die russische Fassung maßgebend.

## 1. Wer für Ihre Daten verantwortlich ist

BarkFluff ist ein föderiertes Netzwerk unabhängiger Server („Knoten“). Jeder Knoten wird von seinem Administrator bereitgestellt und betrieben; die Knoten kommunizieren untereinander, um Nutzer verschiedener Knoten zu verbinden.

Verantwortlicher für Ihre personenbezogenen Daten ist **der Administrator des Knotens, auf dem Ihr Konto angelegt wurde**. Er betreibt die Dienste, besitzt die Datenbank, den Dateispeicher, die Sicherungskopien und die Protokolle, legt die Speicherfristen fest, beantwortet Ihre Anfragen und ist für die Einhaltung des für ihn geltenden Rechts verantwortlich.

Die Entwickler von BarkFluff veröffentlichen die Software und die Protokollspezifikation. Sie betreiben **keine** fremden Knoten, haben **keinen** Zugriff auf deren Daten, Schlüssel, Datenverkehr oder Sicherungskopien, **können** Daten auf einem fremden Knoten weder herausgeben noch ändern oder löschen und sind **nicht** Verantwortliche für Ihre Daten.

Dieses Dokument beschreibt das Verhalten der Software in der Standardkonfiguration und gilt für den Knoten **barkfluff.com**. Der Administrator jedes anderen Knotens veröffentlicht seine eigene Fassung und darf Konfiguration oder Quellcode ändern — dann gelten seine Bedingungen. Wurde Ihr Konto nicht auf barkfluff.com angelegt, wenden Sie sich an den Administrator Ihres Knotens.

## 2. Was der Knotenadministrator technisch sehen kann

- Gewöhnliche Einzel- und Gruppennachrichten werden auf dem Knoten im Klartext gespeichert. Der Knotenadministrator hat technischen Zugriff auf Datenbank, Dateispeicher und Protokolle und **kann deren Inhalt lesen**.
- Private Chats speichern auf dem Server nur den Geheimtext; der Schlüssel wird auf Ihrem Gerät aus der Passphrase abgeleitet und nie an den Knoten übertragen.
- Geheime Chats werden vom Knoten nur zwischen Geräten weitergeleitet und nicht als Verlauf gespeichert.
- Wenn Sie dem Administrator eines bestimmten Knotens nicht vertrauen möchten, nutzen Sie private oder geheime Chats oder betreiben Sie einen eigenen Knoten.

## 3. Welche Daten verarbeitet werden

### 3.1 Konto und Profil

| Daten | Verwendung | Speicherung |
| --- | --- | --- |
| E-Mail | Registrierung, Anmeldung, Bestätigungen, Zugangswiederherstellung, Benachrichtigungen | Users / Identity |
| Benutzername | Anmeldung, Suche, öffentliche Profilseite, Anzeige in Chats | Users |
| Passwort-Hash | Passwortprüfung; das Passwort im Klartext wird nicht gespeichert | Identity |
| Vorname, Nachname, Bio | Nutzerprofil und öffentliche Profildaten | Users |
| Avatar und Profilposter | Gestaltung des Profils und der öffentlichen Seite | Files / Minio + Users-Metadaten |
| Datenschutz- und Personalisierungseinstellungen | Anzeige der Profildaten, Erscheinungsbild des Clients, Erlaubnis föderierter Chats | Users |

### 3.2 Geräte und Sitzungen

| Daten | Verwendung | Speicherung |
| --- | --- | --- |
| Device ID | Bindung des Refresh-Tokens, Liste aktiver Sitzungen, Sitzungsentzug | Identity.RefreshTokens, Users.UserDevices |
| Gerätename | Anzeige und Umbenennung des Geräts | Users.UserDevices |
| Betriebssystem, App-Name | Client-Kompatibilität und Geräteliste | Users.UserDevices |
| Location | Anzeige des Geräts in der Sitzungsliste | Users.UserDevices |
| Firebase-Token | Push-Benachrichtigungen über Firebase Cloud Messaging | Users.UserDevices |
| Refresh-Token | Verlängerung der Sitzung und Abmeldung vom Gerät | Identity.RefreshTokens |

Die IP-Adresse wird in den gRPC-Metadaten-Headern übertragen und zur Bearbeitung der aktuellen Anfragen verwendet. Diese Daten verlassen Ihren Knoten nicht und werden nicht an andere Knoten übermittelt.

### 3.3 Nachrichten, Chats und Dateien

| Daten | Verwendung | Speicherung |
| --- | --- | --- |
| Gewöhnliche Nachrichten | Einzel- und Gruppenchats, Synchronisierung, Lesebestätigungen, Bearbeiten und Löschen | Messages / PostgreSQL |
| Private verschlüsselte Chats | 1-zu-1-Chats mit clientseitiger Verschlüsselung, nur innerhalb eines Knotens | Messages speichert Geheimtext und Chat-Metadaten |
| Geheime Chats | 1-zu-1-Chats zwischen bestimmten Geräten, nur innerhalb eines Knotens | Der Knoten leitet das Envelope weiter und puffert es vorübergehend |
| Anhänge und Vorschauen | Übertragung von Dateien, Bildern, Videos, Dokumenten, Audio und Avataren | Files / Minio + PostgreSQL-Metadaten |
| Lesestatus | Anzeige gelesener Nachrichten | Messages |

### 3.4 Online-Status und Aktualisierungen

Onliner speichert und liefert den Online-Status und den Zeitpunkt der letzten Aktivität. Updates überträgt Echtzeit-Ereignisse über gRPC-Streaming an die Clients.

### 3.5 Website und Support

WebServer liefert die Startseite, öffentliche Profilseiten, Rechtsseiten, Installationsskripte, Client-Versionen und die öffentliche Profil-REST-API. Nachrichten aus dem Support-Formular der Website werden im Arbeitsspeicher des WebServer-Prozesses gehalten und über die Telegram Bot API an den Knotenadministrator weitergeleitet, sofern er eine solche Weiterleitung eingerichtet hat.

Beim Klick auf „Im Browser schreiben“ auf einer öffentlichen Profilseite erstellt WebServer ein kurzlebiges Cookie `bf_open_chat`, damit der Web-Client den Chat mit dem gewählten Nutzer öffnet.

## 4. Föderierter Datenaustausch

Die Föderation ist standardmäßig deaktiviert (`Federation:Enabled = false`) — der Knotenadministrator schaltet sie ein. Ist sie aktiviert und schreiben Sie mit einem Nutzer eines anderen Knotens, verlässt ein Teil der Daten die Grenze Ihres Knotens.

| Was an einen anderen Knoten geht | Wann | Wie |
| --- | --- | --- |
| Kennung, Benutzername, Name und Profilavatar | Wenn ein Nutzer eines anderen Knotens Sie findet oder Ihre Profilkarte öffnet | Profilabfrage zwischen Knoten unter Beachtung Ihrer Datenschutzeinstellungen |
| Nachrichtentext, Bearbeitungen, Löschungen, Lesebestätigungen | 1-zu-1-Konversation mit einem Nutzer eines anderen Knotens | Signierte Ereignisse mit garantierter Zustellung und Wiederholungen |
| Dateien und Anhänge | Wenn Ihr Gesprächspartner eine Datei öffnet | Streaming auf Anfrage seines Knotens; es wird keine Kopie im Voraus angelegt |
| Online-Status und Zeitpunkt der letzten Aktivität | Solange ein föderierter Chat aktiv ist | Laufender Stream zwischen Knoten; Ihre Datenschutzeinstellungen gelten |
| „Schreibt …“ | Während Sie eine Nachricht tippen | Einmalige Benachrichtigung ohne Speicherung |

Wichtig zu verstehen:

- Föderierte Chats sind ausschließlich 1-zu-1. Gruppenchats zwischen Knoten werden nicht unterstützt.
- **Föderierte Nachrichten sind nicht Ende-zu-Ende-verschlüsselt.** Ihr Inhalt ist sowohl Ihrem Knoten als auch dem Knoten des Gesprächspartners zugänglich. Private und geheime Chats funktionieren nur innerhalb eines Knotens.
- Der empfangende Knoten wird zum **eigenständigen Verantwortlichen** für seine Kopie. Weder Ihr Knoten noch die Entwickler kontrollieren, wie lange und auf welche Weise er diese Daten aufbewahrt. Das Modell entspricht dem der E-Mail.
- Das Löschen einer Nachricht oder eines Kontos auf Ihrem Knoten **löscht keine** Kopien, die bereits an andere Knoten zugestellt wurden. Eine automatische Weitergabe von Löschungen über die Föderation gibt es derzeit nicht.
- Sie können eingehende föderierte Einzelchats in den Datenschutzeinstellungen ablehnen — ein Versuch, von einem anderen Knoten aus einen Chat mit Ihnen zu beginnen, wird dann abgewiesen.
- Der Administrator Ihres Knotens kann einen ganzen Knoten sperren — der Datenaustausch mit ihm endet.

**Technische Garantien und ihre Grenzen.** Ereignisse zwischen Knoten werden mit Ed25519 signiert, Verbindungen nutzen TLS mit Prüfung des Server-Schlüsselfingerabdrucks, Anfragen werden innerhalb eines begrenzten Zeitfensters signiert. Das belegt, **von welchem Knoten** die Daten stammen und dass sie unterwegs nicht verändert wurden, gibt aber keinerlei Garantie dafür, wie der Administrator jenes Knotens mit der erhaltenen Kopie umgeht.

## 5. Wofür die Daten verwendet werden

- Registrierung, Anmeldung, 2FA, Passwortwiederherstellung und Sitzungsverwaltung;
- Profile, Nutzersuche, öffentliche Seiten und Datenschutzeinstellungen;
- Zustellung von Nachrichten, Dateien, Lesebestätigungen, Echtzeit-Updates und Push-Benachrichtigungen;
- Nachrichtenaustausch mit Nutzern anderer Knoten, wenn die Föderation aktiviert ist;
- Export der Nutzerdaten über `Users.ExportData`;
- Nutzerunterstützung über die vom Knotenadministrator eingerichteten Kanäle;
- Protokollierung von Fehlern, Metriken und Sicherheitsereignissen.

## 6. Datenschutzmaßnahmen

### 6.1 Datenübertragung

- Clients und Microservices nutzen gRPC/HTTP/2 und HTTPS/TLS am äußeren Perimeter.
- Die Autorisierung von gRPC-Anfragen nutzt XAuth-Metadaten: `x-auth-token`, `x-device-id`, `x-device-name`, `x-ip`, `x-os`, `x-app-name`, `x-app-version`.
- Interne asynchrone Ereignisse laufen über RabbitMQ / MassTransit.
- Der Verkehr zwischen Knoten läuft über einen separaten gesicherten Kanal — siehe „Verschlüsselung“.

### 6.2 Passwörter, Token und 2FA

- Passwörter werden als Hashes gespeichert.
- Access Token — ein JWT im Header `x-auth-token`; die Lebensdauer wird durch die Identity-Konfiguration Ihres Knotens festgelegt.
- Der Refresh Token ist an die Device ID gebunden und wird in Identity gespeichert.
- 2FA unterstützt TOTP-Authenticator und E-Mail-Codes.

### 6.3 Verschlüsselte Chats

- **Private Chats:** Der Knoten speichert den Geheimtext `EncryptedMessage`, Nonce und AAD; der Schlüssel wird auf dem Client aus Passphrase und Chat-Salt abgeleitet.
- **Geheime Chats:** Der Knoten arbeitet mit einem an Geräte gebundenen opaken Envelope, leitet es an den Empfänger weiter und puffert es mit einer TTL von 24 Stunden.
- **Gewöhnliche Chats:** Nachrichten werden im Messages-Dienst Ihres Knotens gespeichert und sind sowohl der Serverlogik als auch dem Knotenadministrator zugänglich.

## 7. Weitergabe an Dritte

Personenbezogene Daten werden nicht verkauft. Daten werden an externe Anbieter nur weitergegeben, um die vom Knotenadministrator aktivierten Funktionen zu betreiben:

- **Andere Knoten der Föderation:** siehe Abschnitt 4.
- **SMTP:** E-Mail-Benachrichtigungen, Bestätigungscodes, Zugangswiederherstellung.
- **Firebase Cloud Messaging:** Push-Benachrichtigungen an Geräte mit Firebase-Token.
- **Telegram Bot API:** Bearbeitung von Nachrichten aus dem Support-Chat der Website.
- **Hosting und Infrastruktur:** Betrieb von Diensten, Datenbanken, Dateispeicher, RabbitMQ, Redis und Seq.

Welche Anbieter konkret eingesetzt werden, entscheidet der Knotenadministrator: Er kann Push deaktivieren, einen anderen SMTP-Dienst nutzen oder auf Telegram verzichten.

## 8. Rechte der Nutzer

Richten Sie Anfragen zu Ihren Daten an den Administrator Ihres Knotens — nur er kann sie erfüllen.

- **Auskunft und Export:** Der Export liefert die JSON-Dateien `profile.json`, `messages.json` und `files.json`.
- **Berichtigung:** Profil, Bio, Avatar, Poster, Datenschutz, Geräte und Passwort lassen sich über die Client-Funktionen ändern, soweit verfügbar.
- **Löschung:** siehe „Richtlinie zur Kontolöschung“.
- **Sitzungsentzug:** Eine aktive Sitzung kann über die Geräteverwaltung beendet werden.
- **Einschränkung der Föderation:** Eingehende föderierte Einzelchats können in den Datenschutzeinstellungen abgelehnt werden.

## 9. Löschung von Nachrichten und Daten

- Gewöhnliche Nachrichten können über die Messages-API gelöscht werden.
- Private verschlüsselte Nachrichten werden beim Löschen als gelöscht markiert, der Geheimtext wird entfernt.
- Geheime Nachrichten werden nach Zustellbestätigung oder Ablauf der TTL aus dem temporären Puffer entfernt.
- Die Löschung wirkt innerhalb Ihres Knotens. Kopien, die bereits an einen anderen Knoten zugestellt wurden, kann nur dessen Administrator löschen.
- Für die Löschung eines Kontos und von Daten, die nicht über Client-Funktionen abgedeckt sind, wenden Sie sich an den Knotenadministrator.

## 10. Cookies und lokale Speicherung

- Native Clients nutzen den lokalen Speicher für Token, Einstellungen und Caches.
- Der Windows-Client speichert `GlobalParam.json` und kann sie mit einer PIN schützen.
- Der macOS-Client nutzt den Keychain für Token.
- Die Website nutzt die Cookies `barkfluff_chat_id` (Sitzung des Support-Chats, 1 Jahr), `bf_open_chat` (kurzlebiger Wechsel von einer öffentlichen Profilseite zum Web-Client) und `bf_cookie_notice` (Vermerk, dass der Cookie-Hinweis angezeigt wurde, 1 Jahr).
- Der Web-Client nutzt die Cookies `bf_theme` (gewähltes Farbschema, 1 Jahr) und `bf_legal_accepted` (Fassung der akzeptierten Nutzungsbedingungen und Datenschutzerklärung, 1 Jahr); Zugriffstoken werden im localStorage oder sessionStorage des Browsers gespeichert.
- Cookies für Analyse, Werbung oder Tracking werden nicht verwendet.

## 11. Kinder

BarkFluff ist nicht für Nutzer unter 13 Jahren bestimmt. Ein Knotenadministrator darf eine höhere Altersgrenze festlegen. Wenn Sie glauben, dass ein Kind unter 13 Jahren ein Konto erstellt hat, informieren Sie den Administrator des betreffenden Knotens.

## 12. Änderungen der Erklärung

Bei Änderungen dieser Erklärung wird das Datum „Letzte Aktualisierung“ angepasst. Wesentliche Änderungen können über die App oder per E-Mail mitgeteilt werden. Ein Knotenadministrator veröffentlicht Änderungen seiner eigenen Fassung selbst.

## 13. Kontakte

**Administrator des Knotens barkfluff.com** — für Ihre Daten, Export, Löschung, Support und Beschwerden:

- Daten und Datenschutz: privacy@barkfluff.com
- Support: support@barkfluff.com
- Rechtsfragen und Inhaltsbeschwerden: legal@barkfluff.com

**Entwickler der BarkFluff-Software** — für Schwachstellen im Code und Fragen zum Protokoll:

- Sicherheit und Protokoll: security@barkfluff.com

Der Entwickler hat keinen Zugriff auf Daten auf fremden Knoten und kann eine Anfrage zu deren Löschung oder Herausgabe nicht bearbeiten. Wurde Ihr Konto auf einem anderen Knoten angelegt, finden Sie die Kontakte seines Administrators auf der Website dieses Knotens.
