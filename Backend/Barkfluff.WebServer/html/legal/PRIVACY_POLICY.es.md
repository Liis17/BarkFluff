# Política de privacidad de BarkFluff

**En vigor desde:** 24 de enero de 2026  
**Última actualización:** 29 de julio de 2026

> Esta traducción se proporciona únicamente por comodidad. En caso de discrepancia, prevalece la versión en ruso.

## 1. Quién es responsable de sus datos

BarkFluff es una red federada de servidores independientes («nodos»). Cada nodo lo despliega y mantiene su administrador; los nodos se comunican entre sí para conectar a usuarios de nodos distintos.

El responsable del tratamiento de sus datos personales es **el administrador del nodo en el que se creó su cuenta**. Él aloja los servicios, posee la base de datos, el almacenamiento de archivos, las copias de seguridad y los registros, determina los plazos de conservación, responde a sus solicitudes y responde del cumplimiento de la legislación que le resulte aplicable.

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
| Correo electrónico | Registro, inicio de sesión, confirmaciones, recuperación de acceso, notificaciones | Users / Identity |
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

WebServer sirve la página principal, las páginas públicas de perfil, las páginas legales, los scripts de instalación, las versiones de los clientes y la API REST pública del perfil. Los mensajes del formulario de soporte del sitio se guardan en la memoria del proceso de WebServer y se reenvían al administrador del nodo a través de la API de bots de Telegram, si él ha configurado dicho reenvío.

Al pulsar «Escribir en el navegador» en una página pública de perfil, WebServer crea una cookie de corta duración `bf_open_chat` para que el cliente web abra el chat con el usuario seleccionado.

## 4. Intercambio federado de datos

La federación está desactivada por defecto (`Federation:Enabled = false`); la activa el administrador del nodo. Cuando está activada y usted conversa con un usuario de otro nodo, parte de los datos cruza la frontera de su nodo.

| Qué sale hacia otro nodo | Cuándo | Cómo |
| --- | --- | --- |
| Identificador, nombre de usuario, nombre y avatar del perfil | Cuando un usuario de otro nodo le encuentra o abre su ficha | Consulta de perfil entre nodos, respetando sus ajustes de privacidad |
| Texto de los mensajes, ediciones, eliminaciones, confirmaciones de lectura | Conversación 1 a 1 con un usuario de otro nodo | Eventos firmados con entrega garantizada y reintentos |
| Archivos y adjuntos | Cuando su interlocutor abre un archivo | Entrega en streaming a petición de su nodo; no se crea una copia por adelantado |
| Estado de conexión y hora de última actividad | Mientras el chat federado esté activo | Flujo en vivo entre nodos; se aplican sus ajustes de privacidad |
| «Escribiendo…» | Mientras escribe un mensaje | Notificación puntual que no se almacena |

Lo importante que debe entender:

- Los chats federados son solo individuales, 1 a 1. Los chats de grupo entre nodos no están soportados.
- **Los mensajes federados no tienen cifrado de extremo a extremo.** Su contenido es accesible tanto para su nodo como para el nodo de su interlocutor. Los chats privados y secretos funcionan solo dentro de un mismo nodo.
- El nodo que recibe los datos se convierte en **responsable independiente** de su copia. Ni su nodo ni los desarrolladores controlan durante cuánto tiempo ni de qué modo conserva esos datos. El modelo es el mismo que el del correo electrónico.
- Eliminar un mensaje o una cuenta en su nodo **no elimina** las copias ya entregadas a otros nodos. Actualmente no existe propagación automática del borrado a través de la federación.
- Puede rechazar los chats privados federados entrantes en los ajustes de privacidad: entonces se rechazará cualquier intento de iniciar un chat con usted desde otro nodo.
- El administrador de su nodo puede bloquear un nodo entero, con lo que cesará el intercambio de datos con él.

**Garantías técnicas y sus límites.** Los eventos entre nodos se firman con Ed25519, las conexiones usan TLS con verificación de la huella de la clave del servidor y las solicitudes se firman dentro de una ventana temporal limitada. Esto acredita **de qué nodo** proceden los datos y que no fueron alterados en tránsito, pero no ofrece garantía alguna sobre cómo trata el administrador de ese nodo la copia recibida.

## 5. Para qué se usan los datos

- registro, inicio de sesión, 2FA, recuperación de contraseña y gestión de sesiones;
- perfiles, búsqueda de usuarios, páginas públicas y ajustes de privacidad;
- entrega de mensajes, archivos, confirmaciones de lectura, actualizaciones en tiempo real y notificaciones push;
- intercambio de mensajes con usuarios de otros nodos cuando la federación está activada;
- exportación de los datos del usuario mediante `Users.ExportData`;
- soporte a los usuarios por los canales que haya configurado el administrador del nodo;
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

Los datos personales no se venden. Los datos se ceden a proveedores externos únicamente para el funcionamiento de las funciones que haya activado el administrador del nodo:

- **Otros nodos de la federación:** véase la sección 4.
- **SMTP:** notificaciones por correo, códigos de confirmación, recuperación de acceso.
- **Firebase Cloud Messaging:** notificaciones push a dispositivos con token de Firebase.
- **API de bots de Telegram:** tratamiento de los mensajes del chat de soporte del sitio.
- **Alojamiento e infraestructura:** hospedaje de servicios, bases de datos, almacenamiento de archivos, RabbitMQ, Redis y Seq.

El conjunto concreto de proveedores lo elige el administrador del nodo: puede desactivar las push, usar otro servicio SMTP o prescindir de Telegram.

## 8. Derechos del usuario

Dirija las solicitudes sobre sus datos al administrador de su nodo: solo él puede atenderlas.

- **Acceso y exportación:** la exportación devuelve los archivos JSON `profile.json`, `messages.json` y `files.json`.
- **Rectificación:** el perfil, la biografía, el avatar, el póster, la privacidad, los dispositivos y la contraseña pueden modificarse mediante las funciones del cliente donde estén disponibles.
- **Supresión:** véase la «Política de eliminación de cuenta».
- **Revocación de sesión:** una sesión activa puede cerrarse mediante la gestión de dispositivos.
- **Limitación de la federación:** los chats privados federados entrantes pueden rechazarse en los ajustes de privacidad.

## 9. Eliminación de mensajes y datos

- Los mensajes normales admiten eliminación mediante la API de Messages.
- Los mensajes privados cifrados se marcan como eliminados y su texto cifrado se borra.
- Los mensajes secretos se eliminan del búfer temporal tras la confirmación de entrega o al expirar el TTL.
- La eliminación surte efecto dentro de su nodo. Las copias ya entregadas a otro nodo solo puede eliminarlas el administrador de ese nodo.
- Para eliminar la cuenta y los datos no cubiertos por las funciones del cliente, contacte con el administrador del nodo.

## 10. Cookies y almacenamiento local

- Los clientes nativos usan el almacenamiento local para tokens, ajustes y cachés.
- El cliente de Windows guarda `GlobalParam.json` y puede protegerlo con un PIN.
- El cliente de macOS usa el Keychain para los tokens.
- El sitio usa las cookies `barkfluff_chat_id` (sesión del chat de soporte, 1 año), `bf_open_chat` (paso efímero de una página pública de perfil al cliente web) y `bf_cookie_notice` (registro de que se mostró el aviso sobre cookies, 1 año).
- El cliente web usa las cookies `bf_theme` (esquema de color elegido, 1 año) y `bf_legal_accepted` (revisión aceptada de las Condiciones de uso y la Política de privacidad, 1 año); los tokens de acceso se guardan en el localStorage o sessionStorage del navegador.
- No se usan cookies de analítica, publicidad ni seguimiento.

## 11. Menores

BarkFluff no está destinado a usuarios menores de 13 años. El administrador de un nodo puede fijar un límite de edad superior. Si cree que un menor de 13 años ha creado una cuenta, informe al administrador del nodo correspondiente.

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
