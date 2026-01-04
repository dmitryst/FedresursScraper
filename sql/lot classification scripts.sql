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