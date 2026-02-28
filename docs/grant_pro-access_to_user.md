```bash
kubectl exec -it <имя-пода> -- psql -U postgres -d lot_db
```

## Поиск пользователя
Прежде чем обновлять, нужно найти Id пользователя по его email, чтобы убедиться, что мы меняем права тому, кому нужно.

Выполните SQL-запрос:

```sql
SELECT "Id", "Email", "IsSubscriptionActive", "SubscriptionEndDate" 
FROM "Users" 
WHERE "Email" = 'user@example.com';
```

Ожидаемый результат:
Вы увидите строку с данными пользователя. Скопируйте его "Id" (хотя мы можем обновить и по email).

## Скрипт выдачи PRO-доступа
Чтобы выдать доступ, нужно установить IsSubscriptionActive = true и задать SubscriptionEndDate в будущем.

Выдача доступа на 1 месяц (по Email):

```sql
UPDATE "Users"
SET 
    "IsSubscriptionActive" = true,
    "SubscriptionEndDate" = NOW() + INTERVAL '1 month'
WHERE "Email" = 'a@a.a';
```

Выдача доступа на 1 год (по Email):

```sql
UPDATE "Users"
SET 
    "IsSubscriptionActive" = true,
    "SubscriptionEndDate" = NOW() + INTERVAL '1 year'
WHERE "Email" = 'user@example.com';
```

Если нужно выдать доступ "навечно" (например, до 2099 года):

```sql
UPDATE "Users"
SET 
    "IsSubscriptionActive" = true,
    "SubscriptionEndDate" = '2099-12-31 23:59:59'
WHERE "Email" = 'user@example.com';
```

## Проверка результата
Убедитесь, что изменения применились:

```sql
SELECT "Email", "IsSubscriptionActive", "SubscriptionEndDate" 
FROM "Users" 
WHERE "Email" = 'user@example.com';
```

Вы должны увидеть:

IsSubscriptionActive: t (true)
SubscriptionEndDate: Дата в будущем (например, 2026-03-13...)

## Выход из psql
Чтобы выйти из консоли psql, введите:

```bash
\q
```

## Полезные команды для сброса доступа
Если нужно отнять подписку:

```sql
UPDATE "Users"
SET 
    "IsSubscriptionActive" = false,
    "SubscriptionEndDate" = NULL -- или текущая дата NOW()
WHERE "Email" = 'user@example.com';
```