# Diagnóstico de DeepSeek en Bali Desktop 1.3

## Última solicitud registrada

El registro de consumo conservó literalmente «DeepSeek inaccesible. Los comandos locales siguen disponibles.». Modelo `deepseek-flash`, duración 53 ms, sin tokens informados. Se registró una reserva estimada de USD 0,00015. La versión anterior descartaba la excepción interna y no guardó un estado HTTP. El mensaje técnico original de esa petición no es recuperable del registro.

## Reproducción en Windows

Se compiló un diagnóstico con la misma configuración anterior del compilador y se ejecutó un `GET /models` sin autorización. DNS resolvió el dominio y TCP 443 abrió. El cliente .NET reprodujo la cadena `HttpRequestException 0x80131500 → WebException 0x80131509 SecureChannelFailure`. El ejecutable carecía de metadatos de destino .NET y `System.Net` anunció `Ssl3, Tls`. El fallo ocurrió durante la negociación, antes de un estado HTTP. La hipótesis se confirmó repitiendo la prueba tras declarar .NET Framework 4.8.1. `System.Net` pasó a `SystemDefault`, la sonda directa negoció TLS 1.3 con validación de certificado y `HttpClient` recibió HTTP 401 al omitir la credencial intencionalmente. Windows también recuperó la clave almacenada desde DPAPI CurrentUser, en memoria y sin transmitirla o imprimirla. [Reproducción anterior](../evidence/deepseek-tls-antes.txt) · [Prueba con destino corregido](../evidence/deepseek-tls-despues.txt) · [Ejecutable instalado](../evidence/deepseek-tls-version-instalada.txt)

El 401 de diagnóstico confirma una respuesta HTTP sin autenticar. La aceptación de la clave, el saldo y la disponibilidad del modelo autenticado requieren una prueba controlada. La documentación oficial confirma `POST https://api.deepseek.com/chat/completions`, `Authorization: Bearer`, `deepseek-flash` y `deepseek-v4-pro`. [DeepSeek Chat Completions](https://api-docs.deepseek.com/api/create-chat-completion/) · [Modelos vigentes](https://api-docs.deepseek.com/quick_start/pricing/) · [TLS en .NET Framework](https://learn.microsoft.com/en-us/dotnet/framework/network-programming/tls)

## Corrección

- El ejecutable declara explícitamente .NET Framework 4.8.1 en su ensamblado. Windows selecciona el protocolo y conserva la validación de certificados.
- La interfaz y el registro distinguen DNS, proxy, TCP, TLS, confianza de certificados, timeout y estados HTTP, incluidos 401, 402, 403, 404, 429 y 500. Los diagnósticos de transporte incluyen tipos, HRESULT y estado de red. Se excluyen cuerpos HTTP, mensajes libres de excepciones y credenciales.
- **Diagnóstico HTTPS sin cargo** ejecuta únicamente comprobaciones de DNS, TCP, TLS y un GET sin clave. **Prueba controlada · puede generar cargo** sigue exigiendo autorización por separado.

Pasaron 98 comprobaciones, incluidas las 96 anteriores. Las pruebas del proveedor HTTP usan un transporte simulado. Durante este diagnóstico no se realizó ningún POST de inferencia. [Registro automatizado](../evidence/unit.txt)