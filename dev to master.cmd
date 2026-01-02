@echo off
chcp 65001 > nul
echo ========================================================
echo ВНИМАНИЕ! 
echo Сейчас ветка MASTER будет полностью заменена веткой DEV.
echo Все изменения в MASTER, которых нет в DEV, будут удалены.
echo ========================================================
echo.
echo Нажмите любую клавишу, чтобы начать, или закройте окно для отмены...
pause > nul

echo.
echo [1/6] Получение данных с сервера...
git fetch origin

echo.
echo [2/6] Переключение на dev и обновление...
git checkout dev
if %errorlevel% neq 0 goto error
git pull origin dev
if %errorlevel% neq 0 goto error

echo.
echo [3/6] Переключение на master...
git checkout master
if %errorlevel% neq 0 goto error

echo.
echo [4/6] Полная замена master на dev (Reset)...
git reset --hard dev
if %errorlevel% neq 0 goto error

echo.
echo [5/6] Отправка изменений на сервер (Force Push)...
git push origin master --force
if %errorlevel% neq 0 goto error

echo.
echo [6/6] Возвращаемся обратно на dev...
git checkout dev

echo.
echo ========================================================
echo УСПЕШНО! Master теперь копия Dev.
echo ========================================================
pause
exit

:error
echo.
echo ========================================================
echo ПРОИЗОШЛА ОШИБКА! Проверьте сообщения выше.
echo ========================================================
pause