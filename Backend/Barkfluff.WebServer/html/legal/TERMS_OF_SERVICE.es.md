# Términos de servicio de BarkFluff

**En vigor desde:** 24 de enero de 2026  
**Última actualización:** 29 de julio de 2026

> Esta traducción se proporciona únicamente por comodidad. En caso de discrepancia, prevalece la versión en ruso.

## 1. Con quién celebra este acuerdo

BarkFluff es una red federada de servidores independientes («nodos»). Cada nodo lo despliega y mantiene su administrador; los nodos se comunican entre sí para conectar a usuarios de nodos distintos.

El servicio se lo presta **el administrador del nodo en el que se creó su cuenta**. Este acuerdo se celebra entre usted y él. Los desarrolladores de BarkFluff no son parte de este acuerdo: publican el software, pero no le prestan un servicio de comunicación ni gestionan nodos ajenos.

Este texto se refiere al nodo **barkfluff.com**. El administrador de cualquier otro nodo publica su propia versión de los términos y puede establecer sus propias normas. Si su cuenta no se creó en barkfluff.com, rigen los términos de su nodo.

Al usar BarkFluff, usted acepta estos Términos y los documentos relacionados: la Política de privacidad, la Eliminación de cuenta y el Cifrado.

## 2. Funciones y ámbitos de responsabilidad

- **El desarrollador de BarkFluff** escribe el código, publica las versiones de los clientes y la especificación del protocolo, y recibe los informes de vulnerabilidades. No aloja nodos ajenos, no tiene acceso a sus datos y no responde de su funcionamiento, disponibilidad, moderación ni de los actos de sus administradores.
- **El administrador del nodo** despliega, actualiza y configura el nodo, responde de la seguridad y la licitud del tratamiento de los datos de sus usuarios, de las copias de seguridad, de la moderación y de la atención a las solicitudes. También decide si activa la federación y con qué nodos se comunica.
- **Usted** responde de sus dispositivos, su contraseña y sus códigos 2FA, así como del contenido que envía y publica.

## 3. El servicio

Un nodo de BarkFluff se compone de microservicios de backend a los que se conectan los clientes de Windows, Android, macOS y el cliente web. Un nodo puede ofrecer un sitio web público, el proxy gRPC-Web, el portal de desarrolladores, almacenamiento de archivos, actualizaciones en tiempo real, notificaciones push, notificaciones por correo y un chat de soporte; el conjunto concreto lo decide su administrador.

- **Cuenta**: la cuenta de usuario con correo electrónico, nombre de usuario, contraseña, perfil y dispositivos. La cuenta pertenece a un nodo concreto y no es válida en otro nodo.
- **Contenido**: mensajes, archivos, imágenes, avatares, pósteres, reacciones, ajustes y otros datos que usted cree o suba.
- **Cliente**: la aplicación o la interfaz web de BarkFluff que usa la API gRPC/gRPC-Web.

## 4. Requisitos de uso

- Debe tener al menos 13 años. El administrador de un nodo puede fijar un límite de edad superior.
- Al registrarse debe indicar un correo electrónico y un nombre de usuario válidos.
- Usted responde de la custodia de su contraseña, de sus códigos 2FA y de los dispositivos con sesión activa.
- Si pierde un dispositivo, puede cerrar la sesión activa mediante las funciones de gestión de dispositivos.

## 5. Uso permitido

- comunicación personal y de trabajo;
- chats individuales y de grupo;
- intercambio de archivos y contenido multimedia;
- chats privados cifrados y chats secretos;
- conversación con usuarios de otros nodos, si el administrador de su nodo ha activado la federación;
- uso de las páginas públicas de perfil y del cliente web;
- uso de los materiales abiertos de proto/API a través del portal de desarrolladores;
- despliegue de un nodo propio conforme a la licencia del software.

## 6. Uso prohibido

- infringir la ley, fraude, phishing, software malicioso e intentos de acceso no autorizado;
- amenazas, acoso, doxing, suplantación de otra persona;
- envío masivo de mensajes no deseados, automatización de spam y sobrecarga de los servicios, tanto del nodo propio como de nodos ajenos;
- publicación o transmisión de contenido ilícito;
- elusión de restricciones de seguridad, explotación de vulnerabilidades e interferencia en la infraestructura;
- abuso del canal federado: falsificación de la identidad de otro nodo, elusión de sus restricciones y límites.

El administrador de un nodo puede ampliar esta lista con sus propias normas.

## 7. Moderación y bloqueos

- En caso de infracción, el administrador del nodo puede restringir el acceso, eliminar el contenido infractor, cerrar sesiones, bloquear o eliminar la cuenta y dirigirse a las autoridades competentes cuando la ley lo exija.
- La moderación actúa dentro del nodo. El administrador de su nodo no puede eliminar contenido en un nodo ajeno y no responde de la moderación de otros.
- El administrador puede bloquear un nodo entero. En tal caso cesa la conversación con sus usuarios, mientras que los mensajes ya recibidos permanecen en su nodo.
- Su nodo responde únicamente de sus propios usuarios y acredita su identidad ante los nodos vecinos. Del contenido procedente de otros nodos responden esos nodos y sus usuarios.

## 8. Qué significa la federación para usted

- La federación está desactivada por defecto; la activa el administrador del nodo.
- Los chats federados son solo individuales, 1 a 1. No se admiten los chats de grupo entre nodos.
- **Los mensajes federados no tienen cifrado de extremo a extremo.** Su contenido es accesible tanto para su nodo como para el nodo de su interlocutor. Los chats privados y secretos funcionan solo dentro de un mismo nodo.
- Al conversar con un usuario de otro nodo, usted entrega de hecho sus mensajes y archivos al control del administrador de ese nodo. Eliminarlos por su parte no elimina la copia de él.
- Los chats privados federados entrantes pueden rechazarse en los ajustes de privacidad.
- Los detalles figuran en la Política de privacidad, sección «Intercambio federado de datos».

## 9. Contenido y derechos

- Usted conserva los derechos sobre su contenido.
- Usted concede al administrador del nodo el derecho a almacenar, transmitir, sincronizar, mostrar y tratar su contenido en la medida necesaria para el funcionamiento de las funciones del servicio, incluida su transmisión a otro nodo cuando usted mismo inicia una conversación federada.
- Los derechos sobre el código, el diseño, el nombre y los logotipos de BarkFluff pertenecen a los titulares del proyecto o a los licenciantes correspondientes. El nombre y los logotipos no se transfieren al administrador de un nodo junto con el derecho a desplegar el software.
- Los componentes abiertos y los contratos proto se usan conforme a las licencias y la documentación correspondientes.

## 10. El software

El software de BarkFluff lo proporciona el desarrollador «tal cual», sin garantía alguna, incluidas las garantías de idoneidad para fines concretos, ausencia de errores o continuidad del funcionamiento. La responsabilidad por la elección, la configuración, la actualización y la explotación de un nodo recae en su administrador.

Al desplegar su propio nodo, usted se convierte en administrador y asume las obligaciones de responsable del tratamiento de los datos personales de sus usuarios, incluida la publicación de sus propios términos y su propia política de privacidad.

## 11. Disponibilidad y cambios del servicio

El nodo se ofrece «tal cual». Son posibles fallos, tareas de mantenimiento y errores del cliente o de los servicios de backend. El administrador del nodo puede cambiar el conjunto de funciones, limitarlas, así como suspender o cesar por completo el funcionamiento del nodo, incluso sin sucesor. Exporte con antelación los datos importantes.

## 12. Limitación de responsabilidad

- **El desarrollador** no responde del funcionamiento ni de la disponibilidad de ningún nodo, de los actos u omisiones de sus administradores, de la conservación y el tratamiento de los datos en ellos, del contenido de los usuarios, ni de los daños derivados del uso del software.
- **El administrador del nodo** no responde de daños indirectos, de la pérdida de datos por actos del usuario, del compromiso del dispositivo o de la contraseña, de los actos de terceros, del contenido recibido de otros nodos ni de la indisponibilidad de proveedores externos, incluidos correo electrónico, Firebase, Telegram, alojamiento e infraestructura de red.

## 13. Modificación del acuerdo

Cuando el acuerdo cambia, se actualiza la fecha de «Última actualización». Si sigue usando el nodo tras una actualización, acepta la nueva versión. El administrador de un nodo publica por su cuenta los cambios de su propia versión.

## 14. Ley aplicable y controversias

La ley aplicable y el procedimiento de resolución de controversias se determinan por la jurisdicción del administrador de su nodo. Para cuestiones relativas al nodo barkfluff.com, dirija su solicitud a legal@barkfluff.com; el plazo de respuesta es de hasta 30 días.

## 15. Contactos

**Administrador del nodo barkfluff.com**: para el servicio, la cuenta, la moderación y las reclamaciones:

- Cuestiones legales y reclamaciones sobre contenido: legal@barkfluff.com
- Soporte: support@barkfluff.com
- Datos y privacidad: privacy@barkfluff.com

**Desarrollador del software BarkFluff**: para vulnerabilidades en el código y cuestiones del protocolo:

- Seguridad y protocolo: security@barkfluff.com

Si su cuenta se creó en otro nodo, busque los datos de contacto de su administrador en el sitio web de ese nodo: ni el desarrollador ni el administrador de barkfluff.com toman decisiones sobre sus usuarios.

## 16. Consentimiento

Al usar BarkFluff, usted confirma que ha leído el acuerdo, que entiende que el servicio lo presta el administrador de un nodo, que acepta cumplir las normas y que ha cumplido 13 años.
