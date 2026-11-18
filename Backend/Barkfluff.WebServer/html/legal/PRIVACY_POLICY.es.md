# Política de privacidad de BarkFluff

**En vigor desde:** 24 de enero de 2026  
**Última actualización:** 19 de noviembre de 2026

> Esta traducción se proporciona únicamente por comodidad. En caso de discrepancia, prevalece la versión en ruso.

## 1. Quién es responsable de sus datos

BarkFluff es una red federada de servidores independientes («nodos»). Cada nodo lo despliega y mantiene su administrador; los nodos se comunican entre sí para conectar a usuarios de nodos distintos.

En el nodo público barkfluff.com, el responsable del tratamiento es una persona física: el desarrollador principal de BarkFluff, que opera desde Armenia. El servidor principal del nodo está en Alemania. BarkFluff es un proyecto de código abierto y no está registrado como persona jurídica. Los contactos del operador figuran al final de este documento.

Los desarrolladores de BarkFluff publican el software y la especificación del protocolo. **No** alojan nodos ajenos, **no** tienen acceso a sus datos, claves, tráfico ni copias de seguridad, **no** pueden entregar, modificar ni eliminar datos en un nodo ajeno y **no** son responsables del tratamiento de sus datos.

Este documento describe el comportamiento del software en su configuración estándar y se aplica al nodo **barkfluff.com**. El administrador de cualquier otro nodo publica su propia versión y puede modificar la configuración o el código fuente; en ese caso rigen sus condiciones. Si su cuenta no se creó en barkfluff.com, diríjase al administrador de su nodo.

## 2. Qué puede ver técnicamente el administrador del nodo

- Los mensajes normales, individuales y de grupo, se almacenan en el nodo en texto claro. El administrador del nodo tiene acceso técnico a la base de datos, al almacenamiento de archivos y a los registros, y **puede leer su contenido**.
- Los chats privados almacenan en el servidor únicamente el texto cifrado; la clave se deriva en su dispositivo a partir de la passphrase y nunca se envía al nodo.
- Los chats secretos solo son retransmitidos por el nodo entre dispositivos y no se conservan como historial.
- Si no está dispuesto a confiar en el administrador de un nodo concreto, use chats privados o secretos, o despliegue su propio nodo.

## 3. Qué datos se procesan

### 3.1 Cuenta y perfil

| Datos | Dónde se usan | Almacenamiento |
| --- | --- | --- |
| Correo electrónico | Registro, inicio de sesión, confirmaciones, recuperación de acceso y notificaciones, incluidos los avisos de bloqueo y eliminación de la cuenta | Users / Identity; el motivo y la fecha de la restricción se guardan con la cuenta |
| Nombre de usuario | Inicio de sesión, búsqueda, página pública de perfil, visualización en los chats | Users |
| Hash de la contraseña | Verificación de la contraseña; la contraseña en texto claro no se almacena | Identity |
| Nombre, apellidos, biografía | Perfil del usuario y datos públicos del perfil | Users |
| Avatar y póster del perfil | Diseño del perfil y de la página pública | Files / Minio + metadatos de Users |
| Ajustes de privacidad y personalización | Visualización de los datos del perfil, apariencia del cliente, permiso para chats federados | Users |

### 3.2 Dispositivos y sesiones

| Datos | Dónde se usan | Almacenamiento |
| --- | --- | --- |
| Device ID | Vinculación del refresh token, lista de sesiones activas, revocación de sesión | Identity.RefreshTokens, Users.UserDevices |
| Nombre del dispositivo | Visualización y renombrado del dispositivo | Users.UserDevices |
| SO, nombre de la aplicación | Compatibilidad de clientes y lista de dispositivos | Users.UserDevices |
| Location | Visualización del dispositivo en la lista de sesiones | Users.UserDevices |
| Token de Firebase | Notificaciones push mediante Firebase Cloud Messaging | Users.UserDevices |
| Refresh token | Renovación de la sesión y cierre de sesión en el dispositivo | Identity.RefreshTokens |

La dirección IP se transmite en las cabeceras de metadatos de servicio de gRPC y se usa para procesar las solicitudes en curso. Estos datos no salen de su nodo ni se transmiten a otros nodos.

### 3.3 Mensajes, chats y archivos

| Datos | Dónde se usan | Almacenamiento |
| --- | --- | --- |
| Mensajes normales | Chats individuales y de grupo, sincronización, confirmaciones de lectura, edición y eliminación | Messages / PostgreSQL |
| Chats privados cifrados | Chats 1 a 1 con cifrado en el cliente, solo dentro de un mismo nodo | Messages almacena el texto cifrado y los metadatos del chat |
| Chats secretos | Chats 1 a 1 entre dispositivos concretos, solo dentro de un mismo nodo | El nodo retransmite el envelope y lo almacena temporalmente |
| Adjuntos y vistas previas | Transferencia de archivos, imágenes, vídeo, documentos, audio y avatares | Files / Minio + metadatos en PostgreSQL |
| Estados de lectura | Visualización de los mensajes leídos | Messages |

### 3.4 Estados de conexión y actualizaciones

Onliner almacena y sirve el estado de conexión y la hora de la última actividad. Updates entrega eventos en tiempo real a los clientes mediante streaming gRPC.

### 3.5 Sitio web y soporte

WebServer ofrece el sitio público, las páginas de perfil, los documentos legales y la API REST de perfiles públicos. En barkfluff.com, los mensajes del formulario de soporte se mantienen en la memoria del proceso WebServer y se reenvían al chat de soporte de Telegram. Según el operador, el chat de soporte de Telegram se limpia automáticamente una vez por semana; se trata de una limpieza periódica del chat, no de un plazo individual para cada mensaje. El historial local del chat de soporte web también se mantiene en la memoria del proceso WebServer y no se ha confirmado un calendario de limpieza.

Las solicitudes enviadas a las direcciones de servicio del operador pueden conservarse sin un plazo máximo establecido. El operador indica que los correos generados automáticamente por el servicio, incluidos los códigos, no se guardan en su propio buzón; esto no describe el tratamiento en Identity ni el almacenamiento en el buzón del destinatario.

Al pulsar «Escribir en el navegador» en una página pública de perfil, WebServer crea una cookie de corta duración `bf_open_chat` para que el cliente web abra el chat con el usuario seleccionado.

### 3.6 Llamadas, bots y notificaciones push

Calls guarda en la base de datos del nodo datos técnicos de la sesión de llamada, incluidos los participantes, el tipo y el estado de la llamada, y las horas de inicio, respuesta y finalización. Los medios de la llamada se retransmiten mediante LiveKit. El operador del nodo público indica que el servidor no graba los medios de las llamadas. Esta declaración no abarca los metadatos, los datos en los dispositivos de los participantes ni otros nodos; no se han confirmado el alojamiento de LiveKit ni sus plazos de conservación.

Si envía un mensaje o archivo a un bot, se entrega a ese bot mediante la API. El operador del bot puede tratar o guardar lo que recibe según sus propias reglas. El token push del dispositivo se guarda en Users; el código backend envía notificaciones mediante Firebase Cloud Messaging cuando está habilitado. No se ha confirmado si se usa APNs ni la configuración de entrega push de otras plataformas.

## 4. Intercambio federado de datos

La federación está desactivada por defecto (`Federation:Enabled = false`); la activa el administrador del nodo. Cuando está activada y usted conversa con un usuario de otro nodo, parte de los datos cruza la frontera de su nodo.

| Qué sale hacia otro nodo | Cuándo | Cómo |
| --- | --- | --- |
| Identificador, nombre de usuario, nombre y avatar del perfil | Cuando un usuario de otro nodo le encuentra o abre su ficha | Consulta de perfil entre nodos, respetando sus ajustes de privacidad |
| Texto de los mensajes, ediciones, eliminaciones, confirmaciones de lectura | Conversación 1 a 1 con un usuario de otro nodo | Eventos firmados; una cola reintenta la entrega |
| Archivos y adjuntos | Cuando su interlocutor abre un archivo | Entrega en streaming a petición de su nodo; no se crea una copia por adelantado |
| Estado de conexión y hora de última actividad | Mientras el chat federado esté activo | Flujo en vivo entre nodos; se aplican sus ajustes de privacidad |
| «Escribiendo…» | Mientras escribe un mensaje | Notificación puntual que no se almacena |

Lo importante que debe entender:

- Los chats federados son solo individuales, 1 a 1. Los chats de grupo entre nodos no están soportados.
- **Los mensajes federados no tienen cifrado de extremo a extremo.** Su contenido es accesible tanto para su nodo como para el nodo de su interlocutor. Los chats privados y secretos funcionan solo dentro de un mismo nodo.
- El nodo que recibe los datos se convierte en **responsable independiente** de su copia. Ni su nodo ni los desarrolladores controlan durante cuánto tiempo ni de qué modo conserva esos datos. El modelo es el mismo que el del correo electrónico.
- La eliminación de un mensaje federado se envía como evento a los nodos participantes y se aplica tras su recepción y validación; la entrega y aplicación no son sincrónicas. No elimina archivos ya descargados ni capturas de pantalla. La eliminación de una cuenta en barkfluff.com no se propaga a otros nodos ni borra datos ya entregados.
- Puede rechazar los chats privados federados entrantes en los ajustes de privacidad: entonces se rechazará cualquier intento de iniciar un chat con usted desde otro nodo.
- El administrador de su nodo puede bloquear un nodo entero, con lo que cesará el intercambio de datos con él.

**Garantías técnicas y sus límites.** Los eventos entre nodos se firman con Ed25519, las conexiones usan TLS con verificación de la huella de la clave del servidor y las solicitudes se firman dentro de una ventana temporal limitada. Esto acredita **de qué nodo** proceden los datos y que no fueron alterados en tránsito, pero no ofrece garantía alguna sobre cómo trata el administrador de ese nodo la copia recibida.

## 5. Para qué se usan los datos

- registro, inicio de sesión, 2FA, recuperación de contraseña y gestión de sesiones;
- perfiles, búsqueda de usuarios, páginas públicas y ajustes de privacidad;
- entrega de mensajes, archivos, confirmaciones de lectura, actualizaciones en tiempo real y notificaciones push;
- intercambio de mensajes con usuarios de otros nodos cuando la federación está activada;
- exportación de los datos del usuario mediante `Users.ExportData`;
- soporte a los usuarios por los canales que haya configurado el administrador del nodo; tramitación de solicitudes de restricción y eliminación de cuentas, envío de avisos relacionados al correo de la cuenta y almacenamiento del motivo y la fecha de la restricción junto con la cuenta. El motivo se elimina cuando la cuenta se elimina definitivamente;
- registro de errores, métricas y eventos de seguridad.

## 6. Protección de datos

### 6.1 Transmisión de datos

- Los clientes y los microservicios usan gRPC/HTTP/2 y HTTPS/TLS en el perímetro externo.
- La autorización de las solicitudes gRPC usa metadatos XAuth: `x-auth-token`, `x-device-id`, `x-device-name`, `x-ip`, `x-os`, `x-app-name`, `x-app-version`.
- Los eventos asíncronos internos pasan por RabbitMQ / MassTransit.
- El tráfico entre nodos va por un canal seguro aparte; véase «Cifrado».

### 6.2 Contraseñas, tokens y 2FA

- Las contraseñas se almacenan como hashes.
- Access token: un JWT en la cabecera `x-auth-token`; su vigencia la fija la configuración de Identity de su nodo.
- El refresh token está vinculado al Device ID y se almacena en Identity.
- 2FA admite autenticador TOTP y códigos por correo electrónico.

### 6.3 Chats cifrados

- **Chats privados:** el nodo almacena el texto cifrado `EncryptedMessage`, el nonce y el AAD; la clave se deriva en el cliente a partir de la passphrase y la sal del chat.
- **Chats secretos:** el nodo trabaja con un envelope opaco vinculado a dispositivos, lo retransmite al destinatario y lo almacena en búfer con un TTL de 24 horas.
- **Chats normales:** los mensajes se almacenan en el servicio Messages de su nodo y son accesibles tanto para la lógica del servidor como para el administrador del nodo.

## 7. Cesión a terceros

Los datos personales no se venden. Según la función y la configuración del nodo público, los datos pueden comunicarse a:

- otros nodos de la federación; consulte la sección 4. Cada nodo receptor responde de su propia copia;
- el servicio SMTP configurado para los correos de la cuenta; no se ha confirmado el proveedor de entrega;
- Firebase Cloud Messaging para notificaciones push, si está habilitado;
- la API de bots de Telegram para reenviar mensajes de soporte del sitio;
- el servidor LiveKit configurado para llamadas; no se han confirmado su alojamiento, operador ni tratamiento de metadatos;
- Las respuestas de la página principal y la política comprobadas incluían los encabezados server: cloudflare y Network Error Logging (NEL); el navegador carga Google Fonts desde fonts.googleapis.com y fonts.gstatic.com.

Los proveedores externos pueden recibir datos técnicos de red necesarios para entregar solicitudes o informes. No se han confirmado su configuración, ubicaciones de tratamiento ni plazos de conservación, salvo los datos sobre el operador indicados aquí. No deben inferirse las prácticas de los proveedores a partir del almacenamiento del nodo.


## 8. Derechos del usuario

Dirija las solicitudes sobre sus datos al administrador de su nodo: solo él puede atenderlas.

- **Acceso y exportación:** la exportación devuelve los archivos JSON `profile.json`, `messages.json` y `files.json`.
- **Rectificación:** el perfil, la biografía, el avatar, el póster, la privacidad, los dispositivos y la contraseña pueden modificarse mediante las funciones del cliente donde estén disponibles.
- **Supresión:** una vez tramitada la solicitud, la cuenta queda en estado de eliminación pendiente durante 7 días; después comienza la limpieza en segundo plano y puede tardar más si debe repetirse tras un error. Consulte la [Política de eliminación de cuenta](/legal/account-deletion).
- **Revocación de sesión:** una sesión activa puede cerrarse mediante la gestión de dispositivos.
- **Limitación de la federación:** los chats privados federados entrantes pueden rechazarse en los ajustes de privacidad.

## 9. Eliminación de mensajes y datos

- Los mensajes normales admiten eliminación mediante la API de Messages.
- Los mensajes privados cifrados se marcan como eliminados y su texto cifrado se borra.
- Los mensajes secretos se eliminan del búfer temporal tras la confirmación de entrega o al expirar el TTL.
- La eliminación de un mensaje federado se envía a los nodos participantes y se aplica tras su recepción y validación; la entrega y aplicación no son sincrónicas. No elimina archivos ya descargados ni capturas de pantalla. La eliminación de una cuenta en barkfluff.com no se propaga a otros nodos ni borra los datos ya entregados.
- Para eliminar la cuenta y los datos no cubiertos por las funciones del cliente, contacte con el operador del nodo. Una vez tramitada la solicitud, la cuenta permanece en estado de eliminación pendiente durante 7 días; después comienza la limpieza en segundo plano y puede retrasarse si debe repetirse tras un error. Consulte la [Política de eliminación de cuenta](/legal/account-deletion).

### Conservación en el nodo público

Según el operador, no se ha establecido un plazo máximo de conservación para los registros y las copias de seguridad del nodo público. Los datos que pudieran haberse incluido anteriormente pueden permanecer después de eliminar la cuenta. Esto no significa que una copia contenga necesariamente datos concretos ni que las copias existan o se conserven siempre; esta declaración no describe la conservación por proveedores externos. No se han facilitado plazos operativos confirmados para otras categorías de datos.

## 10. Cookies y almacenamiento local

- El sitio usa barkfluff_chat_id (identificador del chat de soporte, hasta 1 año), bf_open_chat (paso de una página pública de perfil al cliente web, hasta 5 minutos) y bf_cookie_notice (confirmación del aviso, hasta 1 año).
- El sitio usa localStorage bf_lang para el idioma y bf_domain_notice_v1 para recordar que se cerró el aviso sobre la historia del dominio; no se ha fijado una caducidad separada.
- El cliente web usa bf_theme (tema), bf_lang (idioma) y bf_legal_accepted (revisión aceptada de los términos y la política), hasta 1 año. Los tokens de acceso se guardan en localStorage o sessionStorage.
- Los clientes nativos usan almacenamiento local para tokens, ajustes y cachés; Windows puede proteger GlobalParam.json con PIN y macOS usa Keychain.
- El sitio público carga fuentes desde fonts.googleapis.com y fonts.gstatic.com. Se confirmaron encabezados de Cloudflare y Network Error Logging (NEL) en la página principal y la política; el navegador puede enviar informes de errores de red al endpoint configurado.
- BarkFluff no instala deliberadamente cookies de analítica o publicidad. Las solicitudes externas pueden enviar datos técnicos a proveedores; véase la sección 7.


## 11. Menores

El nodo público barkfluff.com es solo para usuarios de 16 años o más. Otros nodos establecen sus propias reglas de edad. Si cree que un usuario menor de 16 años se ha registrado en barkfluff.com, escriba a privacy@barkfluff.com.

## 12. Cambios en la política

Cuando esta política cambia, se actualiza la fecha de «Última actualización». Los cambios significativos pueden comunicarse a través de la aplicación o por correo electrónico. El administrador de un nodo publica por su cuenta los cambios de su propia versión.

## 13. Contactos

**Administrador del nodo barkfluff.com**: para sus datos, exportación, eliminación, soporte y reclamaciones:

- Datos y privacidad: privacy@barkfluff.com
- Soporte: support@barkfluff.com
- Cuestiones legales y reclamaciones sobre contenido: legal@barkfluff.com

**Desarrollador del software BarkFluff**: para vulnerabilidades en el código y cuestiones del protocolo:

- Seguridad y protocolo: security@barkfluff.com

El desarrollador no tiene acceso a los datos de nodos ajenos y no puede atender una solicitud de eliminación o entrega de los mismos. Si su cuenta se creó en otro nodo, busque los datos de contacto de su administrador en el sitio web de ese nodo.
