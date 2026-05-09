WITH target AS (
    -- Введите координаты объекта:
    SELECT 
        55.796390::float AS lat,  -- Широта оцениваемого объекта
        37.516341::float AS lon   -- Долгота оцениваемого объекта  -- , 
)
SELECT 
    l."PublicId",
    --l."Latitude",
    --l."Longitude",
    l."FinalPrice",
    l."Description",
    -- Расчет расстояния в метрах
    (
        SQRT(
            POWER(l."Latitude" - t.lat, 2) + 
            POWER((l."Longitude" - t.lon) * COS(RADIANS(t.lat)), 2)
        ) * 111320
    )::integer AS distance_meters
FROM "Lots" l
CROSS JOIN target t
WHERE l."Latitude" IS NOT NULL 
  AND l."Longitude" IS NOT NULL
  AND l."TradeStatus" = 'Завершенные'
  AND EXISTS (SELECT 1 FROM "LotCategories" lc WHERE lc."LotId"  = l."Id" AND lc."Name" = 'Квартира')
  AND l."IsSharedOwnership" = false
ORDER BY 
    -- Сортировка по кратчайшему расстоянию
    (
        POWER(l."Latitude" - t.lat, 2) + 
        POWER((l."Longitude" - t.lon) * COS(RADIANS(t.lat)), 2)
    ) ASC
LIMIT 5;
