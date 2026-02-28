```sql

SELECT 
    --l."Id",
    l."PublicId",
    b."TradeNumber",
    l."Description",
    l."TradeStatus",
    l."FinalPrice",
    l."WinnerInn",
    l."WinnerName",
    b."NextStatusCheckAt" 
FROM "Lots" l
JOIN "Biddings" b ON b."Id" = l."BiddingId"
WHERE b."Platform" = 'Центр дистанционных торгов'
  -- Исключаем уже завершенные торги
  AND l."TradeStatus" NOT IN (
      'Торги завершены (нет данных)',
      'Торги не состоялись',
      'Завершенные',
      'Торги отменены'
  )
  -- Блок проверок дат окончания торгов
  AND (
      -- Условие 1: Есть дата объявления результатов
      (
          b."ResultsAnnouncementDate" IS NOT NULL 
          AND b."ResultsAnnouncementDate" + INTERVAL '1 day' < now()
      )
      OR
      -- Условие 2: Даты результатов нет, но есть период приема заявок (парсим вторую часть)
      (
          b."ResultsAnnouncementDate" IS NULL 
          AND b."BidAcceptancePeriod" IS NOT NULL
          AND b."BidAcceptancePeriod" ~ '\d{2}\.\d{2}\.\d{4} \d{2}:\d{2}$'
          AND to_timestamp(right(trim(b."BidAcceptancePeriod"), 16), 'DD.MM.YYYY HH24:MI') + INTERVAL '1 day' < now()
      )
      OR
      -- Условие 3: Нет ни даты результатов, ни периода приема заявок
      (
          b."ResultsAnnouncementDate" IS NULL 
          AND b."BidAcceptancePeriod" IS NULL
      )
  )
ORDER BY l."PublicId";

```
