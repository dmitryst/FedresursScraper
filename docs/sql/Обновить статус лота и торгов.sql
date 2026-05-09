DO $$
DECLARE
    -- Входные параметры
    v_lot_id uuid := '019a96ff-e774-7781-b075-57dfd62cc999';    -- Указать данные
    v_new_lot_status text := 'Завершенные';                     -- Указать данные
    
    -- Внутренние переменные
    v_bidding_id uuid;
    v_biddings_updated_count int;
BEGIN
    -- 1. Обновляем статус лота и сохраняем BiddingId
    UPDATE "Lots"
    SET 
        "TradeStatus" = v_new_lot_status,
        "FinalPrice" = 10555.99,                         -- Указать данные
        "WinnerInn" = '503401762118',                   -- Указать данные
        "WinnerName" = 'Савинова Ольга Владимировна'   -- Указать данные

    WHERE "Id" = v_lot_id
    RETURNING "BiddingId" INTO v_bidding_id;

    -- Если лот найден и обновлен
    IF v_bidding_id IS NOT NULL THEN
        -- Выводим уведомление об успешном обновлении лота
        RAISE NOTICE 'Лот % успешно обновлен. Новый статус: %', v_lot_id, v_new_lot_status;

        -- 2. Обновляем Biddings при условии, что все лоты в финальных статусах
        UPDATE "Biddings"
        SET "IsTradeStatusesFinalized" = true, "NextStatusCheckAt" = NULL
        WHERE "Id" = v_bidding_id
          AND NOT EXISTS (
              SELECT 1
              FROM "Lots"
              WHERE "BiddingId" = v_bidding_id
                AND "TradeStatus" NOT IN (
                        'Завершенные',
                        'Торги отменены',
                        'Торги не состоялись',
                        'Торги завершены (нет данных)'
                    )
          );
          
        -- Получаем количество затронутых строк последним UPDATE
        GET DIAGNOSTICS v_biddings_updated_count = ROW_COUNT;

        -- Выводим результат обновления Biddings
        IF v_biddings_updated_count > 0 THEN
            RAISE NOTICE 'Торги (Bidding) % УСПЕШНО обновлены. IsTradeStatusesFinalized = true.', v_bidding_id;
        ELSE
            RAISE NOTICE 'Торги (Bidding) % НЕ обновлены (найдены лоты в активных статусах).', v_bidding_id;
        END IF;

    ELSE
        -- Если лот с указанным ID не найден
        RAISE NOTICE 'Ошибка: Лот с ID % не найден в базе данных.', v_lot_id;
    END IF;
END $$;