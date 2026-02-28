## Обновление статуса лота и присваивание торгам финализированного статуса (если только один лот у торгов)
```sql

-- 1. Сначала обновляем лот
UPDATE "Lots"
SET "TradeStatus" = 'Торги отменены'
WHERE "Id" = '019a9662-86a3-7673-8a76-176747cc10da'; -- ЗАМЕНИТЕ ID

-- 2. Затем обновляем Bidding, проверяя условие "только один лот"
WITH TargetBidding AS (
    SELECT "BiddingId"
    FROM "Lots"
    WHERE "Id" = '019a9662-86a3-7673-8a76-176747cc10da' -- ТОТ ЖЕ ID
),
LotCounts AS (
    SELECT "BiddingId", COUNT(*) as cnt
    FROM "Lots"
    WHERE "BiddingId" = (SELECT "BiddingId" FROM TargetBidding)
    GROUP BY "BiddingId"
)
UPDATE "Biddings" b
SET "IsTradeStatusesFinalized" = true
FROM LotCounts lc
WHERE b."Id" = lc."BiddingId" 
  AND lc.cnt = 1;

```
