-- Топ самых частых предложений категорий от DeepSeek для лотов,
-- для которых он НЕ нашел подходящей категории из текущего дерева категорий
SELECT 
    "SuggestedCategory", 
    COUNT(*) as Count
FROM "LotClassificationAnalysis"
WHERE "SuggestedCategory" IS NOT NULL
GROUP BY "SuggestedCategory"
ORDER BY Count DESC;


-- узнаем какой именно процесс (Recovery или Scraper) создал аналитику
SELECT * 
FROM "LotClassificationAnalysis" a
JOIN "LotAuditEvents" e ON a."LotId" = e."LotId"
where e."Status" = 'Success'
  -- Вычисляем модуль разницы во времени в секундах
  AND ABS(EXTRACT(EPOCH FROM (a."CreatedAt" - e."Timestamp"))) < 5; -- примерно одно время


-- получаем все LotClassificationAnalysis для лота
SELECT * 
FROM "LotClassificationAnalysis" a
JOIN "LotAuditEvents" e ON a."LotId" = e."LotId"
where a."LotId" = '019b31b0-da6f-797f-982f-f5a122488c01'
    AND ABS(EXTRACT(EPOCH FROM (a."CreatedAt" - e."Timestamp"))) < 5;


-- находим все лоты с категориями, отсутствующими в дереве категорий
WITH ValidCategories ("Name") AS (
    VALUES
    -- Недвижимость
    ('Квартира'), ('Жилой дом'), ('Прочие постройки'), ('Нежилое помещение'), 
    ('Нежилое здание'), ('Имущественный комплекс'), ('Иные сооружения'), 
    ('Земельный участок'), ('Объекты с/х недвижимости'),

    -- Готовый бизнес
    ('Готовый бизнес'),

    -- Транспортные средства
    ('Легковой автомобиль'), ('Коммерческий транспорт и спецтехника'), ('Мототехника'),
    ('Водный транспорт'), ('Авиатранспорт'), ('С/х техника'), 
    ('Иной транспорт и техника'),

    -- Оборудование
    ('Промышленное оборудование'), ('Строительное оборудование'), ('Складское оборудование'),
    ('Торговое оборудование'), ('Металлообрабатывающее оборудование'), ('Медицинское оборудование'),
    ('Пищевое оборудование'), ('Деревообрабатывающее оборудование'), ('Производственные линии'),
    ('Сварочное оборудование'), ('Другое оборудование'),

    -- Компьютерное оборудование
    ('Компьютеры и комплектующие'), ('Оргтехника'), ('Сетевое оборудование'),

    -- Финансовые активы
    ('Дебиторская задолженность'), ('Ценные бумаги'), ('Доли в уставном капитале'),
    ('Другие финансовые активы'),

    -- Товарно-материальные ценности
    ('Одежда'), ('Мебель'), ('Строительные материалы'), ('Оружие'),
    ('Предметы искусства'), ('Драгоценности'), ('Другие ТМЦ'),

    -- Нематериальные активы
    ('Программное обеспечение'), ('Торговые знаки'), ('Авторские права'),
    ('Патенты на изобретение'), ('Другие нематериальные активы'),

    -- Прочее
    ('Прочее')
)
SELECT 
    l."Id" as "LotId",
    l."Title" as "LotTitle",
    lc."Name" as "InvalidCategory",
    lc."Id" as "CategoryId"
FROM "LotCategories" lc
JOIN "Lots" l ON lc."LotId" = l."Id"
LEFT JOIN ValidCategories vc ON lc."Name" = vc."Name"
WHERE vc."Name" IS NULL
ORDER BY lc."Name";


-- находим лоты, удовлетворяющие двум условиям:
-- 1) в таблице "LotCategories" для них нет записей.
-- 2) в таблице "LotAuditEvents" для них есть хотя бы одна запись с типом 'Classification'
SELECT 
    l."Id" AS "LotId",
    l."Title",
    substring(l."Description", 1, 100) as "DescriptionPreview", -- Первые 100 символов описания
    
    -- Получаем статус последней попытки
    (
        SELECT e."Status"
        FROM "LotAuditEvents" e
        WHERE e."LotId" = l."Id" AND e."EventType" = 'Classification'
        ORDER BY e."Timestamp" DESC
        LIMIT 1
    ) AS "LastStatus",
    
    -- Время последней попытки
    (
        SELECT e."Timestamp"
        FROM "LotAuditEvents" e
        WHERE e."LotId" = l."Id" AND e."EventType" = 'Classification'
        ORDER BY e."Timestamp" DESC
        LIMIT 1
    ) AS "LastAttemptTime",

    -- Сколько всего было попыток
    (
        SELECT COUNT(*)
        FROM "LotAuditEvents" e
        WHERE e."LotId" = l."Id" AND e."EventType" = 'Classification'
    ) AS "AttemptCount"

FROM "Lots" l
WHERE 
    -- 1. У лота нет категорий (NOT EXISTS в LotCategories)
    NOT EXISTS (
        SELECT 1 
        FROM "LotCategories" lc 
        WHERE lc."LotId" = l."Id"
    )
    -- 2. Была хотя бы одна попытка классификации (EXISTS в LotAuditEvents)
    AND EXISTS (
        SELECT 1 
        FROM "LotAuditEvents" lae 
        WHERE lae."LotId" = l."Id" 
          AND lae."EventType" = 'Classification'
    )
ORDER BY "LastAttemptTime" DESC;


-- скрипт ручного  разбора категорий лотов
SELECT l."Id", l."Title", COUNT(e."Id") as "Attempts"
FROM "Lots" l
JOIN "LotAuditEvents" e ON l."Id" = e."LotId"
WHERE NOT EXISTS (SELECT 1 FROM "LotCategories" c WHERE c."LotId" = l."Id") -- Нет категорий
  AND e."EventType" = 'Classification'
GROUP BY l."Id", l."Title"
HAVING COUNT(e."Id") >= 5 -- Лимит MaxAttempts
ORDER BY "Attempts" DESC;


