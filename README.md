# TestSystemCoursework (Full) — прохождение тестов + сохранение в Postgres

Состав решения:
- **Server** — TCP сервер (JSON по строкам), работает с PostgreSQL через Npgsql
- **ClientWpf** — клиент прохождения тестов (подключение/логин/получить тесты/начать попытку/ответы/финиш)
- **EditorWpf** — редактор тестов (создание тестов/вопросов/вариантов ответов + картинка как Base64)
- **Shared** — DTO + протокол сообщений

## 1) Postgres (pgAdmin)
Схема/таблицы у тебя уже есть в Postgres Pro.

Проект ожидает, что в базе присутствуют таблицы (users/tests/questions/options/attempts/answers) и что в `users` есть хотя бы два пользователя:
- teacher: `admin / admin`
- student: `student / student`

## 2) Подключение сервера к БД
Открой файл:
- `src/Server/ServerSettings.cs`

Поменяй `ConnectionString` (пароль поставь свой):
```
Host=127.0.0.1;Port=5432;Database=testsystem;Username=postgres;Password=...;Include Error Detail=true
```

## 3) Запуск
Порядок важен:
1. **Server** (Console) — запускаем первым
2. **EditorWpf** — чтобы создавать тесты/вопросы
3. **ClientWpf** — чтобы проходить тесты

## 4) Как проверить, что попытка и ответы пишутся в БД
В pgAdmin:
```sql
select * from attempts order by id desc;
select * from answers order by id desc;
```

## Протокол (для отчёта)
Транспорт: TCP  
Формат: 1 JSON на строку:
```json
{"type":"login","payload":{"login":"student","password":"student"}}
```

Ключевые сообщения:
- `login`
- `tests.get`
- `questions.get`
- `attempt.start`
- `attempt.submit`
- `attempt.finish`

