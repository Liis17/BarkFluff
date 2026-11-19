# BarkFluff Datenschutzerklärung

**Gültig ab:** 24. Januar 2026  
**Letzte Aktualisierung:** 19. November 2026 (Fassung 2)

> Diese Übersetzung dient nur der Bequemlichkeit. Im Falle von Abweichungen ist die russische Fassung maßgebend.

## 1. Wer für Ihre Daten verantwortlich ist

BarkFluff ist ein föderiertes Netzwerk unabhängiger Server („Knoten“). Jeder Knoten wird von seinem Administrator bereitgestellt und betrieben; die Knoten kommunizieren untereinander, um Nutzer verschiedener Knoten zu verbinden.

Für den öffentlichen Knoten barkfluff.com ist eine natürliche Person verantwortlich: der Hauptentwickler von BarkFluff mit Sitz in Armenien. Der Hauptserver des Knotens befindet sich in Deutschland. BarkFluff ist ein Open-Source-Projekt und nicht als juristische Person registriert. Die Kontaktadressen des Betreibers stehen am Ende dieses Dokuments.

Die Entwickler von BarkFluff veröffentlichen Software und Protokollspezifikation für andere, von ihren jeweiligen Administratoren betriebene Knoten. In ihrer Rolle als Softwareautoren betreiben sie diese Knoten nicht, haben keinen Zugriff auf deren Daten, Schlüssel, Datenverkehr oder Sicherungskopien und bearbeiten keine Anfragen zu diesen Daten. Der Hauptentwickler betreibt den öffentlichen Knoten barkfluff.com, wie oben beschrieben, gesondert als natürliche Person.

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
| E-Mail | Registrierung, Anmeldung, Bestätigungen, Zugangswiederherstellung, Benachrichtigungen, einschließlich Mitteilungen über Kontosperrung und -löschung | Users / Identity; Grund und Datum einer Beschränkung werden beim Konto gespeichert |
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

Einige Clients senden einen IP-Wert im gRPC-Metadaten-Header x-ip-address. Der Knoten übernimmt diesen clientseitig übermittelten Wert in den Request-Kontext; er ist keine vertrauenswürdige IP-Quelle und gehört nicht zu den XFed-Headern für Knoten-zu-Knoten-Anfragen. Das bedeutet nicht, dass die Netzwerk-IP externen Diensten verborgen bleibt: Der entfernte Endpunkt sieht die Quell-IP seiner Verbindung; siehe Abschnitt 7.

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

WebServer stellt die öffentliche Website, Profilseiten, Rechtsdokumente und die öffentliche Profil-REST-API bereit. Auf barkfluff.com werden Nachrichten aus dem Support-Formular im Arbeitsspeicher des WebServer-Prozesses als lokaler Verlauf gehalten und an den Telegram-Support-Chat weitergeleitet. Der lokale Verlauf geht beim Neustart des Prozesses verloren. Laut Betreiber wird der Telegram-Support-Chat automatisch einmal pro Woche geleert; dies ist eine regelmäßige Chat-Bereinigung und keine individuelle Aufbewahrungsfrist je Nachricht. Ein Zeitplan für die Bereinigung des lokalen Verlaufs ist nicht bestätigt.

Anfragen an die dienstlichen E-Mail-Adressen des Betreibers können ohne festgelegte Höchstfrist gespeichert werden. Laut Betreiber werden vom Dienst versandte Codes nicht im eigenen Postfach aufbewahrt; dies sagt nichts über die Verarbeitung in Identity oder das Postfach des Empfängers aus.

Beim Klick auf „Im Browser schreiben“ auf einer öffentlichen Profilseite erstellt WebServer ein kurzlebiges Cookie `bf_open_chat`, damit der Web-Client den Chat mit dem gewählten Nutzer öffnet.

### 3.6 Anrufe, Bots und Push-Benachrichtigungen

Calls speichert technische Angaben zur Anrufsitzung in der Knotendatenbank, darunter Teilnehmer, Anruftyp, Status sowie Start-, Annahme- und Endzeit. Anrufmedien werden über LiveKit weitergeleitet. Laut Betreiber des öffentlichen Knotens zeichnet der Server Anrufmedien nicht auf. Diese Aussage bezieht sich nicht auf Anrufmetadaten, Daten auf den Geräten der Teilnehmer oder andere Knoten; Hosting und Aufbewahrungsfristen von LiveKit sind nicht bestätigt.

Wenn Sie einem Bot eine Nachricht oder Datei senden, wird sie über die API an diesen Bot übermittelt. Der Bot-Betreiber kann die empfangenen Daten nach eigenen Regeln verarbeiten oder speichern. Ein Push-Token des Geräts wird in Users gespeichert; der Backend-Code sendet Benachrichtigungen bei aktivierter Funktion über Firebase Cloud Messaging. Ein gewöhnlicher mobiler FCM-Daten-Payload kann chat_id, sender_id, sender_name, avatar_url, chat_title, chat_avatar_url, is_group_chat, content_type, image_url, message_id, message_text (eine auf 100 Zeichen gekürzte Vorschau) und attachment_count enthalten. Ein Payload für private Nachrichten enthält private_chat_id, event_id und sender_user_id; eines für geheime Nachrichten enthält event_id, sender_user_id und sender_device_id. Beide Payloads enthalten keinen Nachrichtentext. Die Nutzung von APNs oder Push-Diensten anderer Plattformen ist nicht bestätigt.

## 4. Föderierter Datenaustausch

Die Föderation ist standardmäßig deaktiviert (`Federation:Enabled = false`) — der Knotenadministrator schaltet sie ein. Ist sie aktiviert und schreiben Sie mit einem Nutzer eines anderen Knotens, verlässt ein Teil der Daten die Grenze Ihres Knotens.

| Was an einen anderen Knoten geht | Wann | Wie |
| --- | --- | --- |
| Kennung, Benutzername, Name und Profilavatar | Wenn ein Nutzer eines anderen Knotens Sie findet oder Ihre Profilkarte öffnet | Profilabfrage zwischen Knoten unter Beachtung Ihrer Datenschutzeinstellungen |
| Nachrichtentext, Bearbeitungen, Löschungen, Lesebestätigungen | 1-zu-1-Konversation mit einem Nutzer eines anderen Knotens | Signierte Ereignisse; eine Warteschlange versucht die Zustellung erneut |
| Dateien und Anhänge | Wenn Ihr Gesprächspartner eine Datei öffnet | Streaming auf Anfrage seines Knotens; es wird keine Kopie im Voraus angelegt |
| Online-Status und Zeitpunkt der letzten Aktivität | Solange ein föderierter Chat aktiv ist | Laufender Stream zwischen Knoten; Ihre Datenschutzeinstellungen gelten |
| „Schreibt …“ | Während Sie eine Nachricht tippen | Einmalige Benachrichtigung ohne Speicherung |

Wichtig zu verstehen:

- Föderierte Chats sind ausschließlich 1-zu-1. Gruppenchats zwischen Knoten werden nicht unterstützt.
- **Föderierte Nachrichten sind nicht Ende-zu-Ende-verschlüsselt.** Ihr Inhalt ist sowohl Ihrem Knoten als auch dem Knoten des Gesprächspartners zugänglich. Private und geheime Chats funktionieren nur innerhalb eines Knotens.
- Der empfangende Knoten wird zum **eigenständigen Verantwortlichen** für seine Kopie. Weder Ihr Knoten noch die Entwickler kontrollieren, wie lange und auf welche Weise er diese Daten aufbewahrt. Das Modell entspricht dem der E-Mail.
- Die Löschung einer föderierten Nachricht wird als Ereignis an beteiligte Knoten gesendet und nach Empfang und Prüfung angewendet; Zustellung und Anwendung erfolgen nicht synchron. Bereits heruntergeladene Dateien oder Bildschirmaufnahmen werden nicht gelöscht. Die Kontolöschung auf barkfluff.com wird nicht an andere Knoten weitergegeben und löscht dort bereits übermittelte Daten nicht.
- Sie können eingehende föderierte Einzelchats in den Datenschutzeinstellungen ablehnen — ein Versuch, von einem anderen Knoten aus einen Chat mit Ihnen zu beginnen, wird dann abgewiesen.
- Der Administrator Ihres Knotens kann einen ganzen Knoten sperren — der Datenaustausch mit ihm endet.

**Technische Garantien und ihre Grenzen.** Ereignisse zwischen Knoten werden mit Ed25519 signiert, Verbindungen nutzen TLS mit Prüfung des Server-Schlüsselfingerabdrucks, Anfragen werden innerhalb eines begrenzten Zeitfensters signiert. Das belegt, **von welchem Knoten** die Daten stammen und dass sie unterwegs nicht verändert wurden, gibt aber keinerlei Garantie dafür, wie der Administrator jenes Knotens mit der erhaltenen Kopie umgeht.

## 5. Wofür die Daten verwendet werden

- Registrierung, Anmeldung, 2FA, Passwortwiederherstellung und Sitzungsverwaltung;
- Profile, Nutzersuche, öffentliche Seiten und Datenschutzeinstellungen;
- Zustellung von Nachrichten, Dateien, Lesebestätigungen, Echtzeit-Updates und Push-Benachrichtigungen;
- Nachrichtenaustausch mit Nutzern anderer Knoten, wenn die Föderation aktiviert ist;
- Export der Nutzerdaten über `Users.ExportData`;
- Nutzerunterstützung über die vom Knotenadministrator eingerichteten Kanäle; Bearbeitung von Anträgen auf Kontobeschränkung und -löschung, Versand entsprechender Mitteilungen an die Konto-E-Mail-Adresse und Speicherung des Grundes und Datums einer Beschränkung zusammen mit dem Konto. Der Grund wird bei endgültiger Kontolöschung entfernt;
- Protokollierung von Fehlern, Metriken und Sicherheitsereignissen.

## 6. Datenschutzmaßnahmen

### 6.1 Datenübertragung

- Clients und Microservices nutzen gRPC/HTTP/2 und HTTPS/TLS am äußeren Perimeter.
- Die Autorisierung von gRPC-Anfragen nutzt XAuth-Metadaten: `x-auth-token`, `x-device-id`, `x-device-name`, `x-ip-address`, `x-os`, `x-app-name`, `x-app-version`.
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

Personenbezogene Daten werden nicht verkauft. Abhängig von Funktion und Konfiguration des öffentlichen Knotens können Daten übermittelt werden an:

- andere Föderationsknoten — siehe Abschnitt 4; jeder empfangende Knoten ist für seine eigene Kopie verantwortlich;
- den konfigurierten SMTP-Dienst für Konto-E-Mails; der konkrete E-Mail-Zustelldienst ist nicht bestätigt;
- Firebase Cloud Messaging für Push-Benachrichtigungen, sofern aktiviert;
- die Telegram Bot API zur Weiterleitung von Nachrichten aus dem Website-Support;
- den konfigurierten LiveKit-Server für Anrufe; Hosting, Betreiber und Verarbeitung von Metadaten sind nicht bestätigt;
- Die geprüften Antworten der Startseite und der Datenschutzerklärung enthielten die Header server: cloudflare und Network Error Logging (NEL); der Browser lädt Google Fonts von fonts.googleapis.com und fonts.gstatic.com.

Ein externer Dienst kann die Quell-IP seiner Netzwerkverbindung sehen: Bei einer direkten Browseranfrage ist dies die IP des Browsers, bei einer Serveranfrage die ausgehende IP des Knotens. Das bedeutet nicht, dass der Anbieter diese Adresse speichert; seine Verarbeitung und Aufbewahrungsfristen sind nicht bestätigt. Anbieterpraktiken lassen sich nicht aus der Speicherung des Knotens ableiten.


## 8. Rechte der Nutzer

Richten Sie Anfragen zu Ihren Daten an den Administrator Ihres Knotens — nur er kann sie erfüllen.

- **Auskunft und Export:** Der Export liefert die JSON-Dateien `profile.json`, `messages.json` und `files.json`.
- **Berichtigung:** Profil, Bio, Avatar, Poster, Datenschutz, Geräte und Passwort lassen sich über die Client-Funktionen ändern, soweit verfügbar.
- **Löschung:** Nach Bearbeitung des Antrags bleibt das Konto 7 Tage im Status „Löschung ausstehend“. Danach beginnt die Bereinigung im Hintergrund; sie kann sich verzögern, wenn sie nach einem Fehler wiederholt werden muss. Einzelheiten stehen in der [Richtlinie zur Kontolöschung](/legal/account-deletion).
- **Sitzungsentzug:** Eine aktive Sitzung kann über die Geräteverwaltung beendet werden.
- **Einschränkung der Föderation:** Eingehende föderierte Einzelchats können in den Datenschutzeinstellungen abgelehnt werden.

## 9. Löschung von Nachrichten und Daten

- Gewöhnliche Nachrichten können über die Messages-API gelöscht werden.
- Private verschlüsselte Nachrichten werden beim Löschen als gelöscht markiert, der Geheimtext wird entfernt.
- Geheime Nachrichten werden nach Zustellbestätigung oder Ablauf der TTL aus dem temporären Puffer entfernt.
- Die Löschung einer föderierten Nachricht wird an beteiligte Knoten gesendet und nach Empfang und Prüfung angewendet; Zustellung und Anwendung erfolgen nicht synchron. Bereits heruntergeladene Dateien oder Bildschirmaufnahmen werden nicht gelöscht. Die Kontolöschung auf barkfluff.com wird nicht an andere Knoten weitergegeben und löscht dort bereits übermittelte Daten nicht.
- Für die Löschung eines Kontos und von Daten, die nicht über Client-Funktionen abgedeckt sind, wenden Sie sich an den Betreiber. Nach Bearbeitung des Antrags bleibt das Konto 7 Tage im Status „Löschung ausstehend“; danach beginnt die Bereinigung im Hintergrund und kann sich verzögern, wenn sie nach einem Fehler wiederholt werden muss. Siehe die [Richtlinie zur Kontolöschung](/legal/account-deletion).

### Aufbewahrung auf dem öffentlichen Knoten

Laut Betreiber gibt es für Protokolle und Sicherungskopien des öffentlichen Knotens keine festgelegte Höchstaufbewahrungsfrist. Daten, die zuvor darin enthalten sein könnten, können nach der Kontolöschung weiterbestehen. Das bedeutet nicht, dass bestimmte Daten zwingend in einer Kopie enthalten sind oder Kopien immer vorhanden sind oder aufbewahrt werden; die Aufbewahrung durch Drittanbieter wird hiermit nicht beschrieben. Für andere Datenkategorien wurden keine bestätigten betrieblichen Aufbewahrungsfristen mitgeteilt.

## 10. Cookies und lokale Speicherung

- Die Website verwendet barkfluff_chat_id (Kennung des Support-Chats, bis zu 1 Jahr), bf_open_chat (Wechsel von einer öffentlichen Profilseite zum Web-Client, bis zu 5 Minuten) und bf_cookie_notice (Bestätigung des Hinweises, bis zu 1 Jahr).
- Die Website verwendet localStorage bf_lang für die Sprache und bf_domain_notice_v1, um das Schließen des Domain-Hinweises zu speichern; ein eigenes Ablaufdatum ist nicht gesetzt.
- Der Web-Client verwendet bf_theme (Design), bf_lang (Sprache) und bf_legal_accepted (angenommene Fassung der Bedingungen und Datenschutzerklärung), jeweils bis zu 1 Jahr. Zugriffstoken liegen im localStorage oder sessionStorage.
- Native Clients speichern Token, Einstellungen und Caches lokal; Windows kann GlobalParam.json per PIN schützen, macOS verwendet den Schlüsselbund.
- Die öffentliche Website lädt Schriftarten von fonts.googleapis.com und fonts.gstatic.com. Cloudflare-Header und Network Error Logging (NEL) wurden auf Startseite und Datenschutzerklärung bestätigt; der Browser kann Netzwerkfehlerberichte an den konfigurierten Endpunkt senden.
- BarkFluff setzt nicht absichtlich Cookies für Analyse oder Werbung. Externe Anfragen können technische Daten an Anbieter senden; siehe Abschnitt 7.


## 11. Kinder

Der öffentliche Knoten barkfluff.com ist nur für Nutzer ab 16 Jahren bestimmt. Andere Knoten legen eigene Altersregeln fest. Wenn Sie glauben, dass sich eine Person unter 16 Jahren auf barkfluff.com registriert hat, schreiben Sie an privacy@barkfluff.com.

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
