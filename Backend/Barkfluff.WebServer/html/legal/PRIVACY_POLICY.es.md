# Política de privacidad de BarkFluff

**En vigor desde:** 24 de enero de 2026  
**Última actualización:** 17 de junio de 2026

> Esta traducción se proporciona únicamente por comodidad. En caso de discrepancia, prevalece la versión en ruso.

## 1. Introducción

Esta Política describe los datos que se procesan en la plataforma BarkFluff actual: cuenta, perfil, dispositivos, mensajes, archivos, versiones de los clientes, páginas públicas de perfil y el chat de soporte del sitio web.

## 2. Qué datos se procesan

### 2.1 Cuenta y perfil

| Datos | Dónde se usan | Almacenamiento |
| --- | --- | --- |
| Correo electrónico | Registro, inicio de sesión, confirmaciones, recuperación de acceso, notificaciones | Users / Identity |
| Nombre de usuario | Inicio de sesión, búsqueda, página pública de perfil, visualización en los chats | Users |
| Hash de la contraseña | Verificación de la contraseña; la contraseña en texto claro no se almacena | Identity |
| Nombre, apellido, biografía | Perfil del usuario y datos públicos del perfil | Users |
| Avatar y póster del perfil | Diseño del perfil y de la página pública | Files / Minio + metadatos de Users |
| Ajustes de privacidad y personalización | Visualización de los datos del perfil y apariencia del cliente | Users |

### 2.2 Dispositivos y sesiones

| Datos | Dónde se usan | Almacenamiento |
| --- | --- | --- |
| Device ID | Vinculación del refresh token, lista de sesiones activas, revocación de sesión | Identity.RefreshTokens, Users.UserDevices |
| Nombre del dispositivo | Visualización y renombrado del dispositivo | Users.UserDevices |
| Sistema operativo, nombre de la aplicación | Compatibilidad de los clientes y lista de dispositivos | Users.UserDevices |
| Location | Visualización del dispositivo en la lista de sesiones | Users.UserDevices |
| Token de Firebase | Notificaciones push mediante Firebase Cloud Messaging | Users.UserDevices |
| Refresh token | Renovación de la sesión y cierre de sesión en el dispositivo | Identity.RefreshTokens |

La dirección IP se transmite en las cabeceras de metadatos de servicio de gRPC y se utiliza para procesar las solicitudes actuales.

### 2.3 Mensajes, chats y archivos

| Datos | Dónde se usan | Almacenamiento |
| --- | --- | --- |
| Mensajes normales | Chats individuales y de grupo, sincronización, read receipts, edición y eliminación | Messages / PostgreSQL |
| Chats privados cifrados | Chats 1 a 1 con cifrado en el cliente | Messages almacena el texto cifrado y los metadatos del chat |
| Chats secretos | Chats 1 a 1 entre dispositivos concretos | El servidor retransmite el envelope y lo almacena temporalmente en búfer |
| Adjuntos y vistas previas | Transferencia de archivos, imágenes, vídeo, documentos, audio y avatares | Files / Minio + metadatos en PostgreSQL |
| Estados de lectura | Visualización de los mensajes leídos | Messages |

### 2.4 Estados en línea y actualizaciones

Onliner almacena y proporciona el estado en línea y la hora de la última actividad. Updates entrega eventos en tiempo real a los clientes mediante gRPC streaming.

### 2.5 Sitio web y soporte

WebServer sirve la página principal, las páginas públicas de perfil, las páginas legales, los scripts de instalación, las versiones de los clientes y la API REST del perfil público. Los mensajes del formulario de soporte del sitio se guardan en la memoria del proceso WebServer y se reenvían al administrador a través de la API de bots de Telegram.

Al hacer clic en «Escribir en el navegador» en una página pública de perfil, WebServer crea una cookie de corta duración `bf_open_chat` para que el cliente web abra el chat con el usuario seleccionado.

## 3. Para qué se utilizan los datos

- registro, inicio de sesión, 2FA, recuperación de contraseña y gestión de sesiones;
- funcionamiento de los perfiles, la búsqueda de usuarios, las páginas públicas y los ajustes de privacidad;
- entrega de mensajes, archivos, read receipts, actualizaciones en tiempo real y notificaciones push;
- exportación de los datos del usuario mediante `Users.ExportData`;
- soporte a los usuarios a través del sitio web, el correo electrónico y el bot de Telegram;
- registro de errores, métricas y eventos de seguridad.

## 4. Protección de datos

### 4.1 Transmisión de datos

- Los clientes y los microservicios utilizan gRPC/HTTP/2 y HTTPS/TLS en el perímetro externo.
- La autorización de las solicitudes gRPC utiliza metadatos XAuth: `x-auth-token`, `x-device-id`, `x-device-name`, `x-ip`, `x-os`, `x-app-name`, `x-app-version`.
- Los eventos asíncronos internos pasan por RabbitMQ / MassTransit.

### 4.2 Contraseñas, tokens y 2FA

- Las contraseñas se almacenan como hashes.
- Access token: un JWT en la cabecera `x-auth-token`; su tiempo de vida lo define la configuración de Identity.
- El refresh token está vinculado al Device ID y se almacena en Identity.
- El 2FA admite autenticador TOTP y códigos por correo electrónico.

### 4.3 Chats cifrados

- **Chats privados:** el servidor almacena el texto cifrado `EncryptedMessage`, el nonce y el AAD; la clave se deriva en el cliente a partir de la passphrase y la sal del chat.
- **Chats secretos:** el servidor trabaja con un envelope opaco vinculado a dispositivos, lo retransmite al destinatario y lo mantiene en búfer con un TTL de 24 horas.
- **Chats normales:** los mensajes se almacenan en el servicio Messages y son accesibles a la lógica del servidor, que proporciona sincronización, exportación y funciones de chat.

## 5. Comunicación a terceros

No vendemos datos personales. Los datos se comunican a proveedores externos únicamente para el funcionamiento de las características actuales:

- **SMTP:** notificaciones por correo electrónico, códigos de confirmación, recuperación de acceso.
- **Firebase Cloud Messaging:** notificaciones push a dispositivos con token de Firebase.
- **API de bots de Telegram:** tratamiento de los mensajes del chat de soporte del sitio.
- **Alojamiento e infraestructura:** alojamiento de los servicios, las bases de datos, el almacenamiento de archivos, RabbitMQ, Redis y Seq.

## 6. Derechos del usuario

- **Acceso y exportación:** la exportación devuelve los archivos JSON `profile.json`, `messages.json` y `files.json`.
- **Rectificación:** el perfil, la biografía, el avatar, el póster, la privacidad, los dispositivos y la contraseña se pueden modificar mediante las funciones del cliente, donde estén disponibles.
- **Supresión:** puede enviar una solicitud de eliminación de la cuenta y de los datos personales asociados a privacy@barkfluff.com.
- **Revocación de sesión:** una sesión activa se puede finalizar mediante las funciones de gestión de dispositivos.

## 7. Eliminación de mensajes y datos

- Los mensajes normales admiten eliminación mediante la API de Messages.
- Los mensajes privados cifrados se marcan como eliminados al borrarlos y el texto cifrado se limpia.
- Los mensajes secretos se eliminan del búfer temporal tras la confirmación de entrega o al expirar el TTL.
- Para eliminar la cuenta y los datos no cubiertos por las funciones del cliente, utilice una solicitud de soporte.

## 8. Cookies y almacenamiento local

- Los clientes nativos utilizan almacenamiento local para tokens, ajustes y cachés.
- El cliente de Windows guarda `GlobalParam.json` y puede protegerlo con un código PIN.
- El cliente de macOS utiliza el Keychain para los tokens.
- WebServer utiliza la cookie `bf_open_chat` solo para la transición desde una página pública de perfil al cliente web.

## 9. Menores

BarkFluff no está destinado a usuarios menores de 13 años. Si cree que un menor de 13 años ha creado una cuenta, contáctenos: privacy@barkfluff.com.

## 10. Cambios en la política

Cuando esta política cambia, se actualiza la fecha de «Actualización». Los cambios sustanciales pueden comunicarse a través de la aplicación o por correo electrónico.

## 11. Contactos

- Privacidad: privacy@barkfluff.com
- Soporte: support@barkfluff.com
- Seguridad: security@barkfluff.com
- Sitio web: https://barkfluff.com
