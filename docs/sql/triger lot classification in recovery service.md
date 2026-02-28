## Тригер запуска (батчевой) классификации лота через LotRecoveryService

```sql

DO $$
DECLARE 
    target_public_id INT := 90091;
    target_lot_id UUID;
BEGIN
    -- 1. Получаем внутренний Id (Guid/UUID) лота один раз, чтобы не делать подзапросы
    SELECT "Id" INTO target_lot_id FROM "Lots" WHERE "PublicId" = target_public_id;

    -- Если лот найден, выполняем обновление и удаление
    IF target_lot_id IS NOT NULL THEN
        UPDATE "Lots" SET "Title" = NULL WHERE "Id" = target_lot_id;
        DELETE FROM "LotCategories" WHERE "LotId" = target_lot_id;
        DELETE FROM "LotAuditEvents" WHERE "LotId" = target_lot_id;
        DELETE FROM "LotClassificationAnalysis" WHERE "LotId" = target_lot_id;
        
        RAISE NOTICE 'Лот % успешно очищен', target_public_id;
    ELSE
        RAISE WARNING 'Лот с PublicId = % не найден', target_public_id;
    END IF;
END $$;

```


## Тригер запуска (батчевой) классификации лотов через LotRecoveryService с выборкой Id лотов по условию

```sql

DO $$
DECLARE 
    target_record RECORD;
    processed_count INT := 0;
BEGIN
    -- 1. Выбираем все нужные лоты (сразу берем внутренний Id и PublicId для логирования)
    -- и запускаем цикл по каждой найденной строке
    FOR target_record IN 
        SELECT l."Id", l."PublicId" 
        FROM "Lots" l
        JOIN "Biddings" b ON b."Id" = l."BiddingId"
        WHERE b."TradeNumber" LIKE '196305-МЭТС%' 
          AND l."StartPrice" < l."MarketValueMin" / 3
    LOOP
        -- 2. Внутри цикла у нас есть доступ к текущему target_record."Id"
        
        -- Обнуляем Title у лота
        UPDATE "Lots" 
        SET "Title" = NULL 
        WHERE "Id" = target_record."Id";
        
        -- Удаляем категории этого лота
        DELETE FROM "LotCategories" 
        WHERE "LotId" = target_record."Id";
        
        -- Удаляем аудит-события классификации этого лота
        DELETE FROM "LotAuditEvents" 
        WHERE "LotId" = target_record."Id";

        -- Увеличиваем счетчик обработанных лотов
        processed_count := processed_count + 1;
        
        -- Выводим сообщение в консоль БД (опционально)
        RAISE NOTICE 'Лот % (Id: %) успешно очищен', target_record."PublicId", target_record."Id";
    END LOOP;

    -- 3. Итоговое сообщение
    IF processed_count > 0 THEN
        RAISE NOTICE 'Успешно обработано лотов: %', processed_count;
    ELSE
        RAISE NOTICE 'Подходящие лоты не найдены.';
    END IF;
END $$;

```
