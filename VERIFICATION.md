# Проверка решения

Дата проверки: 05.11.2026. Компьютер: Windows, Visual Studio 2022, SDK .NET 9.0.318.

## Выполнено

- 30 unit-тестов xUnit: все пройдены, 0 ошибок, 0 пропусков. Итоговый запуск — Release.
- MAUI Android: сборка Debug, целевая платформа net9.0-android, эмулятор Pixel 7 API 31 / Android 12.
- MAUI Windows: сборка Debug, целевая платформа net9.0-windows10.0.19041.0.
- Приложение открыто и запущено из Visual Studio 2022 на Android-эмуляторе.
- Реальная загрузка с эмулятора через http://10.0.2.2:5080/api/rates, далее запрос делает собственный сервер.
- Ввод 100 RUB на экране автоматически обновил сумму USD.
- Реальный запрос к серверу за 20.01.2025: получены курсы на 18.01.2025, 44 валюты с добавленным RUB.
- Повторный запрос к серверу использует кеш. Созданы файлы успешного ответа и отсутствующих дат 404.
- Серверный поиск предыдущего дня, память, дисковый кеш после перезапуска provider и конкурентные запросы проверены unit-тестами.
- JSON-клиент и полное сохранение/восстановление состояния через сгенерированный контекст проверены unit-тестами.
- Подготовлен workflow GitHub Actions: тесты → сервер + Android, Windows, iOS Simulator, Mac Catalyst.

## Ограничения проверки

- iOS и Mac Catalyst локально не собирались: здесь нет Mac/Xcode. Целевые платформы и соответствующие файлы включены.
- Пустой проект опубликован пользователем. Перенос и новый дизайн пока локальные; workflow этих изменений на GitHub ещё не запускался.
- HTTPS-хостинг сервера не развёрнут: готов сервер для локального запуска/последующего размещения.
- Debug APK предназначен для учебной установки; подписи и пакеты магазинов не настраивались.

## Команды для повторения

```powershell
dotnet test tests/CurrencyConverter.Tests/CurrencyConverter.Tests.csproj -c Release
dotnet build CurrencyConverter2/CurrencyConverter2.csproj -f net9.0-android -p:BuildTarget=net9.0-android
dotnet build CurrencyConverter2/CurrencyConverter2.csproj -f net9.0-windows10.0.19041.0 -p:BuildTarget=net9.0-windows10.0.19041.0
dotnet run --project src/CurrencyConverter.Server/CurrencyConverter.Server.csproj
```

## Перенос в CurrencyConverter2 и дизайн ТЗ
05.11.2026: исходное решение CurrencyConverter2.sln и его MAUI-проект сохранены. Origin=https://github.com/M0zeav/CurrencyConverter2.git сохранён. После переноса пройдены 30/30 unit-тестов Release. Android и Windows собраны без ошибок и предупреждений. Добавлен чёрно-белый экран по макету с двумя горизонтальными рядами ввода, датой справа и меню ⋮. Новый сервер запущен из src/CurrencyConverter.Server этой папки.

На эмуляторе дополнительно проверены календарь (кнопки OK/Отмена видны), стрелка списка валют и пересчёт после выбора RUB. Стрелка списка валют открывает штатный Android-диалог; основной экран повторяет расположение элементов макета.
