-- init-search-config.sql

-- Создаем словарь Hunspell
-- Указываем имена файлов БЕЗ расширений (Postgres сам добавит .affix и .dict)
CREATE TEXT SEARCH DICTIONARY public.russian_hunspell (
    TEMPLATE = ispell,
    DictFile = ru_ru,
    AffFile = ru_ru,
    StopWords = russian
);

-- Создаем конфигурацию поиска на основе стандартной
CREATE TEXT SEARCH CONFIGURATION public.russian_h (COPY = russian);

-- Изменяем маппинг: сначала проверяем Hunspell, если не нашли - то стандартный Stemmer
ALTER TEXT SEARCH CONFIGURATION public.russian_h
    ALTER MAPPING FOR hword, hword_part, word
    WITH russian_hunspell, russian_stem;
    
-- Опционально: Тест, чтобы убедиться в логах, что все ок
-- SELECT ts_lexize('public.russian_h', 'Татарстане'); 
-- Должно вернуть 'татарстан'
