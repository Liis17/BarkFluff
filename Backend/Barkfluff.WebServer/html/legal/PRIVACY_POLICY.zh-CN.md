# BarkFluff 隐私政策

**生效日期：** 2026 年 1 月 24 日  
**最后更新：** 2026 年 6 月 17 日

> 本译文仅供参考。如有歧义，以俄文版本为准。

## 1. 引言

本政策说明在当前 BarkFluff 平台中处理的数据：账户、个人资料、设备、消息、文件、客户端版本、公开的个人资料页面以及网站上的支持聊天。

## 2. 处理哪些数据

### 2.1 账户与个人资料

| 数据 | 用途 | 存储位置 |
| --- | --- | --- |
| 电子邮件 | 注册、登录、确认、找回访问权限、通知 | Users / Identity |
| 用户名 | 登录、搜索、公开个人资料页面、在聊天中显示 | Users |
| 密码哈希 | 密码校验；不存储明文密码 | Identity |
| 名、姓、简介 | 用户个人资料及公开的资料数据 | Users |
| 头像与个人资料海报 | 个人资料和公开页面的外观 | Files / Minio + Users 元数据 |
| 隐私与个性化设置 | 个人资料数据的显示和客户端外观 | Users |

### 2.2 设备与会话

| 数据 | 用途 | 存储位置 |
| --- | --- | --- |
| Device ID | 绑定 refresh 令牌、活动会话列表、吊销会话 | Identity.RefreshTokens, Users.UserDevices |
| 设备名称 | 设备的显示与重命名 | Users.UserDevices |
| 操作系统、应用名称 | 客户端兼容性与设备列表 | Users.UserDevices |
| Location | 在会话列表中显示设备 | Users.UserDevices |
| Firebase 令牌 | 通过 Firebase Cloud Messaging 发送推送通知 | Users.UserDevices |
| Refresh 令牌 | 续期会话与从设备退出登录 | Identity.RefreshTokens |

IP 地址在 gRPC 服务元数据标头中传输，用于处理当前请求。

### 2.3 消息、聊天与文件

| 数据 | 用途 | 存储位置 |
| --- | --- | --- |
| 普通消息 | 单人和群组聊天、同步、已读回执、编辑与删除 | Messages / PostgreSQL |
| 私密加密聊天 | 采用客户端加密的一对一聊天 | Messages 存储密文和聊天元数据 |
| 秘密聊天 | 特定设备之间的一对一聊天 | 服务器转发信封并临时缓冲 |
| 附件与预览 | 传输文件、图片、视频、文档、音频和头像 | Files / Minio + PostgreSQL 元数据 |
| 已读状态 | 显示已读消息 | Messages |

### 2.4 在线状态与更新

Onliner 存储并提供在线状态和最后活动时间。Updates 通过 gRPC streaming 向客户端推送实时事件。

### 2.5 网站与支持

WebServer 提供首页、公开个人资料页面、法律页面、安装脚本、客户端版本以及公开个人资料的 REST API。网站支持表单中的消息保存在 WebServer 进程内存中，并通过 Telegram Bot API 转发给管理员。

在公开个人资料页面点击"在浏览器中发消息"时，WebServer 会创建短生命周期的 cookie `bf_open_chat`，以便 Web 客户端打开与所选用户的聊天。

## 3. 数据的使用目的

- 注册、登录、双重验证、密码找回和会话管理；
- 个人资料、用户搜索、公开页面和隐私设置的运行；
- 消息、文件、已读回执、实时更新和推送通知的投递；
- 通过 `Users.ExportData` 导出用户数据；
- 通过网站、电子邮件和 Telegram 机器人提供用户支持；
- 记录错误、指标和安全事件。

## 4. 数据保护

### 4.1 数据传输

- 客户端与微服务在外部边界使用 gRPC/HTTP/2 和 HTTPS/TLS。
- gRPC 请求授权使用 XAuth 元数据：`x-auth-token`、`x-device-id`、`x-device-name`、`x-ip`、`x-os`、`x-app-name`、`x-app-version`。
- 内部异步事件通过 RabbitMQ / MassTransit 传递。

### 4.2 密码、令牌与双重验证

- 密码以哈希形式存储。
- Access 令牌为 `x-auth-token` 标头中的 JWT；其有效期由 Identity 配置决定。
- Refresh 令牌与 Device ID 绑定，存储在 Identity 中。
- 双重验证支持 TOTP 验证器和电子邮件验证码。

### 4.3 加密聊天

- **私密聊天：** 服务器存储 `EncryptedMessage` 密文、nonce 和 AAD；密钥在客户端由口令和聊天盐值派生。
- **秘密聊天：** 服务器处理与设备绑定的不透明信封，将其转发给接收方，并以 24 小时 TTL 进行缓冲。
- **普通聊天：** 消息存储在 Messages 服务中，服务端逻辑可访问，用于提供同步、导出和聊天功能。

## 5. 向第三方提供

我们不出售个人数据。仅为运行当前功能才会向外部供应商提供数据：

- **SMTP：** 电子邮件通知、确认验证码、找回访问权限。
- **Firebase Cloud Messaging：** 向具有 Firebase 令牌的设备发送推送通知。
- **Telegram Bot API：** 处理网站支持聊天中的消息。
- **托管与基础设施：** 承载服务、数据库、文件存储、RabbitMQ、Redis 和 Seq。

## 6. 用户权利

- **访问与导出：** 导出返回 JSON 文件 `profile.json`、`messages.json` 和 `files.json`。
- **更正：** 在客户端功能可用的情况下，可修改个人资料、简介、头像、海报、隐私、设备和密码。
- **删除：** 删除账户及相关个人数据的请求可发送至 privacy@barkfluff.com。
- **吊销会话：** 可通过设备管理功能终止活动会话。

## 7. 消息与数据的删除

- 普通消息支持通过 Messages API 删除。
- 私密加密消息在删除时被标记为已删除，并清除密文。
- 秘密消息在确认投递或 TTL 到期后从临时缓冲区中移除。
- 对于客户端功能未覆盖的账户和数据删除，请通过支持请求处理。

## 8. Cookie 与本地存储

- 原生客户端使用本地存储保存令牌、设置和缓存。
- Windows 客户端保存 `GlobalParam.json`，并可用 PIN 码保护该文件。
- macOS 客户端使用 Keychain 保存令牌。
- WebServer 仅在从公开个人资料页面跳转至 Web 客户端时使用 `bf_open_chat` cookie。

## 9. 儿童

BarkFluff 不面向未满 13 周岁的用户。如果您认为未满 13 周岁的儿童创建了账户，请联系我们：privacy@barkfluff.com。

## 10. 政策变更

本政策变更时会更新"最后更新"日期。重大变更可能通过应用程序或电子邮件通知。

## 11. 联系方式

- 隐私：privacy@barkfluff.com
- 支持：support@barkfluff.com
- 安全：security@barkfluff.com
- 网站：https://barkfluff.com
