SELECT l."PublicId"
FROM "Lots" l
WHERE l."Latitude" IS NULL 
  AND l."Longitude" IS NULL
  AND l."TradeStatus" = 'Завершенные'
  AND EXISTS (SELECT 1 FROM "LotCategories" lc WHERE lc."LotId"  = l."Id" AND lc."Name" = 'Квартира')
  AND NOT EXISTS (SELECT 1 FROM "CadastralInfo" ci WHERE ci."LotId" = l."Id")
ORDER BY l."PublicId";
