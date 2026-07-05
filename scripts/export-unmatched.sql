SELECT attributes->>'brand' AS brand, attributes->>'model' AS model, COUNT(*) AS cnt
FROM "Lots" l
JOIN "LotCategories" lc ON lc."LotId" = l."Id" AND lc."Name" = 'Легковой автомобиль'
WHERE l."IsActive" = true
  AND l."Attributes" ? 'brand'
  AND l."Attributes" ? 'model'
  AND l."Attributes"->>'_brand_matched' = 'true'
  AND l."Attributes"->>'_model_matched' = 'false'
  AND COALESCE(attributes->>'brand', '') <> ''
  AND COALESCE(attributes->>'model', '') <> ''
GROUP BY 1, 2
ORDER BY cnt DESC;
