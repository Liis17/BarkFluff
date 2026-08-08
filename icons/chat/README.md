# Иконки чата

Иконки для композера сообщения и компактного превью последнего сообщения в списке чатов.

## Контракт

- SVG: `24×24`, `viewBox="0 0 24 24"`.
- Монохром: только `currentColor`, без встроенных цветов и контейнеров.
- Rounded outline: `stroke-width="2"`, круглые caps и joins.
- Кнопки композера отображаются в размере около `24dp`; превью списка — около `15dp`.

## Кнопки композера

| Файл | Назначение |
|---|---|
| `send.svg` | Отправить текст или вложения |
| `attach-file.svg` | Открыть выбор файла, фото или видео |
| `stickers.svg` | Открыть панель стикеров |

## Превью типов вложений

| Тип сообщения | Файл |
|---|---|
| `IMAGE` | `image.svg` |
| `VIDEO` | `video.svg` |
| `GIF` | `gif.svg` |
| `DOCUMENT` | `document.svg` |
| `AUDIO` | `audio.svg` |
| `VOICE` | `voice.svg` |
| `STICKER` | `sticker.svg` |
| `FORWARDED_MESSAGE` | `forwarded-message.svg` |
| неизвестный тип | `unknown-attachment.svg` |
