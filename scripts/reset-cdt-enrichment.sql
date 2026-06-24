-- Сброс IsEnriched для торгов ЦДТ с неполным обогащением (нет фото и/или графика цен).
-- Сначала выполните блок PREVIEW, затем — UPDATE.
--
-- Критерии (как в CdtEnrichmentService):
--   • площадка «Центр дистанционных торгов»
--   • IsEnriched = true
--   • CreatedAt после даты отсечения воркера (15.01.2026 UTC)
--   • у хотя бы одного лота нет фото
--     ИЛИ (для публичного предложения) у хотя бы одного лота нет графика снижения цен

-- =============================================================================
-- PREVIEW: какие торги будут сброшены
-- =============================================================================
WITH cdt_incomplete AS (
    SELECT
        b."Id",
        b."TradeNumber",
        b."Type",
        b."CreatedAt",
        b."EnrichedAt",
        COUNT(DISTINCT l."Id") AS lot_count,
        COUNT(DISTINCT li."Id") AS image_count,
        COUNT(DISTINCT ps."Id") AS schedule_count
    FROM "Biddings" b
    JOIN "Lots" l ON l."BiddingId" = b."Id"
    LEFT JOIN "LotImages" li ON li."LotId" = l."Id"
    LEFT JOIN "LotPriceSchedules" ps ON ps."LotId" = l."Id"
    WHERE b."Platform" LIKE '%Центр дистанционных торгов%'
      AND COALESCE(b."IsEnriched", false) = true
      AND b."HasNoLots" = false
      AND b."CreatedAt" > TIMESTAMPTZ '2026-01-15 00:00:00+00'
    GROUP BY b."Id", b."TradeNumber", b."Type", b."CreatedAt", b."EnrichedAt"
    HAVING
        COUNT(DISTINCT li."Id") = 0
        OR (
            b."Type" ILIKE '%Публичное%'
            AND COUNT(DISTINCT ps."Id") = 0
        )
)
SELECT
    "TradeNumber",
    "Type",
    "CreatedAt",
    "EnrichedAt",
    lot_count,
    image_count,
    schedule_count,
    CASE
        WHEN image_count = 0 THEN 'нет фото'
        WHEN "Type" ILIKE '%Публичное%' AND schedule_count = 0 THEN 'нет графика цен'
        ELSE 'прочее'
    END AS reason
FROM cdt_incomplete
ORDER BY "CreatedAt" DESC;


-- =============================================================================
-- UPDATE: сброс флага и состояния повторных попыток
-- Раскомментируйте блок целиком перед выполнением.
-- =============================================================================
/*
DO $$
DECLARE
    v_reset_count int;
    v_state_count int;
BEGIN
    CREATE TEMP TABLE tmp_cdt_reset ON COMMIT DROP AS
    SELECT b."Id"
    FROM "Biddings" b
    JOIN "Lots" l ON l."BiddingId" = b."Id"
    LEFT JOIN "LotImages" li ON li."LotId" = l."Id"
    LEFT JOIN "LotPriceSchedules" ps ON ps."LotId" = l."Id"
    WHERE b."Platform" LIKE '%Центр дистанционных торгов%'
      AND COALESCE(b."IsEnriched", false) = true
      AND b."HasNoLots" = false
      AND b."CreatedAt" > TIMESTAMPTZ '2026-01-15 00:00:00+00'
    GROUP BY b."Id", b."Type"
    HAVING
        COUNT(DISTINCT li."Id") = 0
        OR (
            b."Type" ILIKE '%Публичное%'
            AND COUNT(DISTINCT ps."Id") = 0
        );

    UPDATE "Biddings" b
    SET
        "IsEnriched" = false,
        "EnrichedAt" = NULL
    FROM tmp_cdt_reset t
    WHERE b."Id" = t."Id";

    GET DIAGNOSTICS v_reset_count = ROW_COUNT;
    RAISE NOTICE 'Сброшен IsEnriched у % торгов ЦДТ', v_reset_count;

    UPDATE "EnrichmentStates" es
    SET
        "RetryCount" = 0,
        "MissingImagesAttemptCount" = 0,
        "LastError" = NULL,
        "LastAttemptAt" = NULL
    FROM tmp_cdt_reset t
    WHERE es."BiddingId" = t."Id";

    GET DIAGNOSTICS v_state_count = ROW_COUNT;
    RAISE NOTICE 'Обновлено состояний EnrichmentStates: %', v_state_count;
END $$;
*/
