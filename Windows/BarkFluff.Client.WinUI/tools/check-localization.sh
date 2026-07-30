#!/usr/bin/env bash
# Паритет ключей ru/en и наличие каждого строкового ключа, использованного в XAML.
# В WinUI нет DynamicResource: отсутствующий StaticResource — исключение в рантайме,
# а не пустая строка, поэтому проверка обязательна перед каждым коммитом.
set -u

cd "$(dirname "$0")/.." || exit 2
en=Resources/Localization/Strings.en.xaml
ru=Resources/Localization/Strings.ru.xaml
status=0

if ! diff <(grep -o 'x:Key="[^"]*"' "$en" | sort) <(grep -o 'x:Key="[^"]*"' "$ru" | sort); then
    echo "ОШИБКА: наборы ключей ru и en различаются"
    status=1
fi

# Строковые ключи узнаются по префиксу экрана; кисти и стили сюда не попадают.
prefixes='App_|Settings_|Tray_|Welcome_|SelectNode_|ConnectedNode_|Login_|FastAuth_|Registration_|Recovery_|Messenger_|Common_|Error_'
missing=$(grep -rhoE "\{StaticResource ($prefixes)[A-Za-z0-9_]*\}" --include='*.xaml' . \
    | sed -E 's/\{StaticResource (.*)\}/\1/' | sort -u \
    | while read -r key; do
        grep -q "x:Key=\"$key\"" "$en" || echo "$key"
    done)

if [ -n "$missing" ]; then
    echo "ОШИБКА: ключи используются в XAML, но отсутствуют в словарях:"
    echo "$missing"
    status=1
fi

[ "$status" -eq 0 ] && echo "Локализация: OK ($(grep -c 'x:String' "$en") ключей)"
exit "$status"
