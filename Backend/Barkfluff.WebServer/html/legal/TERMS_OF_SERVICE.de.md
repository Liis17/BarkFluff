# BarkFluff Nutzungsbedingungen

**Gültig ab:** 24. Januar 2026  
**Letzte Aktualisierung:** 29. Juli 2026

> Diese Übersetzung dient nur der Bequemlichkeit. Im Falle von Abweichungen ist die russische Fassung maßgebend.

## 1. Mit wem Sie diese Vereinbarung schließen

BarkFluff ist ein föderiertes Netzwerk unabhängiger Server („Knoten“). Jeder Knoten wird von seinem Administrator bereitgestellt und betrieben; die Knoten kommunizieren untereinander, um Nutzer verschiedener Knoten zu verbinden.

Der Dienst wird Ihnen von **dem Administrator des Knotens erbracht, auf dem Ihr Konto angelegt wurde**. Diese Vereinbarung wird zwischen Ihnen und ihm geschlossen. Die Entwickler von BarkFluff sind nicht Partei dieser Vereinbarung: Sie veröffentlichen die Software, erbringen Ihnen aber keinen Kommunikationsdienst und betreiben keine fremden Knoten.

Dieser Text gilt für den Knoten **barkfluff.com**. Der Administrator jedes anderen Knotens veröffentlicht seine eigene Fassung der Bedingungen und darf eigene Regeln aufstellen. Wurde Ihr Konto nicht auf barkfluff.com angelegt, gelten die Bedingungen Ihres Knotens.

Mit der Nutzung von BarkFluff stimmen Sie diesen Bedingungen und den zugehörigen Dokumenten zu: der Datenschutzerklärung, der Kontolöschung und der Verschlüsselung.

## 2. Rollen und Verantwortungsbereiche

- **Der BarkFluff-Entwickler** schreibt den Code, veröffentlicht Client-Releases und die Protokollspezifikation und nimmt Schwachstellenmeldungen entgegen. Er betreibt keine fremden Knoten, hat keinen Zugriff auf deren Daten und ist nicht verantwortlich für deren Betrieb, Verfügbarkeit, Moderation oder das Handeln ihrer Administratoren.
- **Der Knotenadministrator** stellt den Knoten bereit, aktualisiert und konfiguriert ihn, ist verantwortlich für die Sicherheit und Rechtmäßigkeit der Verarbeitung der Daten seiner Nutzer, für Sicherungskopien, für die Moderation und für die Beantwortung von Anfragen. Er entscheidet auch, ob die Föderation aktiviert wird und mit welchen Knoten kommuniziert wird.
- **Sie** sind verantwortlich für Ihre Geräte, Ihr Passwort und Ihre 2FA-Codes sowie für die Inhalte, die Sie senden und veröffentlichen.

## 3. Der Dienst

Ein BarkFluff-Knoten besteht aus Backend-Microservices, mit denen sich die Clients für Windows, Android, macOS und der Web-Client verbinden. Ein Knoten kann eine öffentliche Website, den gRPC-Web-Proxy, das Entwicklerportal, Dateispeicher, Echtzeit-Updates, Push-Benachrichtigungen, E-Mail-Benachrichtigungen und einen Support-Chat bereitstellen — den genauen Umfang bestimmt sein Administrator.

- **Konto** — ein Nutzerkonto mit E-Mail, Benutzername, Passwort, Profil und Geräten. Das Konto gehört zu einem bestimmten Knoten und gilt auf einem anderen Knoten nicht.
- **Inhalte** — Nachrichten, Dateien, Bilder, Avatare, Poster, Reaktionen, Einstellungen und andere Daten, die Sie erstellen oder hochladen.
- **Client** — die BarkFluff-Anwendung oder die Weboberfläche, die die gRPC/gRPC-Web-API nutzt.

## 4. Nutzungsvoraussetzungen

- Sie müssen mindestens 13 Jahre alt sein. Ein Knotenadministrator darf eine höhere Altersgrenze festlegen.
- Bei der Registrierung müssen Sie eine gültige E-Mail-Adresse und einen gültigen Benutzernamen angeben.
- Sie sind für die Sicherheit Ihres Passworts, Ihrer 2FA-Codes und der Geräte mit aktiver Sitzung verantwortlich.
- Bei Verlust eines Geräts können Sie die aktive Sitzung über die Geräteverwaltung beenden.

## 5. Zulässige Nutzung

- private und berufliche Kommunikation;
- Einzel- und Gruppenchats;
- Austausch von Dateien und Medien;
- private verschlüsselte Chats und geheime Chats;
- Kommunikation mit Nutzern anderer Knoten, sofern der Administrator Ihres Knotens die Föderation aktiviert hat;
- Nutzung öffentlicher Profilseiten und des Web-Clients;
- Nutzung offener Proto-/API-Materialien über das Entwicklerportal;
- Betrieb eines eigenen Knotens gemäß den Bedingungen der Softwarelizenz.

## 6. Unzulässige Nutzung

- Rechtsverstöße, Betrug, Phishing, Schadsoftware und Versuche unbefugten Zugriffs;
- Drohungen, Belästigung, Doxing, Ausgeben als andere Person;
- massenhafte unerwünschte Nachrichten, Spam-Automatisierung und Überlastung der Dienste — sowohl des eigenen als auch fremder Knoten;
- Veröffentlichung oder Übermittlung rechtswidriger Inhalte;
- Umgehung von Sicherheitsbeschränkungen, Ausnutzung von Schwachstellen und Eingriffe in die Infrastruktur;
- Missbrauch des Föderationskanals: Fälschung der Identität eines fremden Knotens, Umgehung seiner Beschränkungen und Limits.

Ein Knotenadministrator darf diese Liste um eigene Regeln ergänzen.

## 7. Moderation und Sperren

- Bei Verstößen kann der Knotenadministrator den Zugang einschränken, rechtsverletzende Inhalte entfernen, Sitzungen beenden, das Konto sperren oder löschen sowie die zuständigen Behörden einschalten, soweit dies gesetzlich vorgeschrieben ist.
- Die Moderation wirkt innerhalb des Knotens. Der Administrator Ihres Knotens kann keine Inhalte auf einem fremden Knoten entfernen und ist für die Moderation anderswo nicht verantwortlich.
- Der Administrator darf einen ganzen Knoten sperren. In diesem Fall endet die Kommunikation mit dessen Nutzern; bereits empfangene Nachrichten verbleiben auf Ihrem Knoten.
- Ihr Knoten steht nur für seine eigenen Nutzer ein und bestätigt deren Identität gegenüber den Partnerknoten. Für Inhalte von anderen Knoten sind jene Knoten und ihre Nutzer verantwortlich.

## 8. Was die Föderation für Sie bedeutet

- Die Föderation ist standardmäßig deaktiviert; der Knotenadministrator schaltet sie ein.
- Föderierte Chats sind ausschließlich 1-zu-1. Gruppenchats zwischen Knoten werden nicht unterstützt.
- **Föderierte Nachrichten sind nicht Ende-zu-Ende-verschlüsselt.** Ihr Inhalt ist sowohl Ihrem Knoten als auch dem Knoten des Gesprächspartners zugänglich. Private und geheime Chats funktionieren nur innerhalb eines Knotens.
- Wenn Sie mit einem Nutzer eines anderen Knotens schreiben, geben Sie Ihre Nachrichten und Dateien faktisch in die Kontrolle des Administrators jenes Knotens. Ein Löschen auf Ihrer Seite löscht dessen Kopie nicht.
- Eingehende föderierte Einzelchats können in den Datenschutzeinstellungen abgelehnt werden.
- Einzelheiten finden Sie in der Datenschutzerklärung, Abschnitt „Föderierter Datenaustausch“.

## 9. Inhalte und Rechte

- Sie behalten die Rechte an Ihren Inhalten.
- Sie räumen dem Knotenadministrator das Recht ein, Ihre Inhalte in dem Umfang zu speichern, zu übertragen, zu synchronisieren, anzuzeigen und zu verarbeiten, der für den Betrieb der Dienstfunktionen erforderlich ist — einschließlich der Übermittlung an einen anderen Knoten, wenn Sie selbst eine föderierte Konversation beginnen.
- Die Rechte an Code, Design, Name und Logos von BarkFluff liegen bei den Projektinhabern oder den jeweiligen Lizenzgebern. Name und Logos gehen nicht zusammen mit dem Recht, die Software zu betreiben, auf einen Knotenadministrator über.
- Offene Komponenten und Proto-Verträge werden zu den Bedingungen der jeweiligen Lizenzen und Dokumentation genutzt.

## 10. Die Software

Die BarkFluff-Software wird vom Entwickler „wie besehen“ bereitgestellt, ohne jegliche Gewährleistung, einschließlich der Gewährleistung der Eignung für einen bestimmten Zweck, der Fehlerfreiheit oder des unterbrechungsfreien Betriebs. Die Verantwortung für Auswahl, Konfiguration, Aktualisierung und Betrieb eines Knotens liegt bei dessen Administrator.

Wenn Sie einen eigenen Knoten betreiben, werden Sie Administrator und übernehmen die Pflichten eines Verantwortlichen für die personenbezogenen Daten Ihrer Nutzer, einschließlich der Veröffentlichung eigener Bedingungen und einer eigenen Datenschutzerklärung.

## 11. Verfügbarkeit und Änderungen des Dienstes

Ein Knoten wird „wie besehen“ bereitgestellt. Störungen, Wartungsarbeiten sowie Fehler des Clients oder der Backend-Dienste sind möglich. Der Knotenadministrator kann den Funktionsumfang ändern, Funktionen einschränken sowie den Knoten aussetzen oder vollständig einstellen — gegebenenfalls ohne Nachfolger. Exportieren Sie wichtige Daten rechtzeitig.

## 12. Haftungsbeschränkung

- **Der Entwickler** haftet nicht für Betrieb und Verfügbarkeit irgendeines Knotens, für Handeln oder Unterlassen seiner Administratoren, für die Sicherheit und Verarbeitung der dortigen Daten, für Nutzerinhalte oder für Schäden, die aus der Nutzung der Software entstehen.
- **Der Knotenadministrator** haftet nicht für mittelbare Schäden, Datenverlust durch Handlungen des Nutzers, Kompromittierung von Gerät oder Passwort, Handlungen Dritter, von anderen Knoten empfangene Inhalte sowie die Nichtverfügbarkeit externer Anbieter, einschließlich E-Mail, Firebase, Telegram, Hosting und Netzinfrastruktur.

## 13. Änderung der Vereinbarung

Bei Änderungen der Vereinbarung wird das Datum „Letzte Aktualisierung“ angepasst. Wenn Sie den Knoten nach einer Aktualisierung weiter nutzen, akzeptieren Sie die neue Fassung. Ein Knotenadministrator veröffentlicht Änderungen seiner eigenen Fassung selbst.

## 14. Anwendbares Recht und Streitigkeiten

Das anwendbare Recht und das Verfahren zur Streitbeilegung richten sich nach der Jurisdiktion des Administrators Ihres Knotens. Für Angelegenheiten des Knotens barkfluff.com richten Sie Ihre Anfrage an legal@barkfluff.com; die Bearbeitungsfrist beträgt bis zu 30 Tage.

## 15. Kontakte

**Administrator des Knotens barkfluff.com** — zu Dienst, Konto, Moderation und Beschwerden:

- Rechtsfragen und Inhaltsbeschwerden: legal@barkfluff.com
- Support: support@barkfluff.com
- Daten und Datenschutz: privacy@barkfluff.com

**Entwickler der BarkFluff-Software** — für Schwachstellen im Code und Fragen zum Protokoll:

- Sicherheit und Protokoll: security@barkfluff.com

Wurde Ihr Konto auf einem anderen Knoten angelegt, finden Sie die Kontakte seines Administrators auf der Website dieses Knotens — über dessen Nutzer entscheiden weder der Entwickler noch der Administrator von barkfluff.com.

## 16. Zustimmung

Mit der Nutzung von BarkFluff bestätigen Sie, dass Sie die Vereinbarung gelesen haben, verstehen, dass der Dienst von einem Knotenadministrator erbracht wird, den Regeln zu folgen zustimmen und mindestens 13 Jahre alt sind.
