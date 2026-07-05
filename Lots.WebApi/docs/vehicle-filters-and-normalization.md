# Марки и модели: фильтры, нормализация, admin API

Документ описывает, как устроена работа с атрибутами `brand` / `model` у лотов категории **«Легковой автомобиль»**: публичное API для фронтенда, фоновые worker'ы WebApi и admin-методы для обслуживания справочника.

## Общая схема

```
Scraper (FedresursScraper)
  └─ VehicleAttributesExtractor (DeepSeek)
       └─ извлекает brand, model, year, mileage → Lot.Attributes (jsonb)

WebApi
  ├─ VehicleAttributesNormalizationWorker — приводит brand/model к canonical по справочнику
  ├─ VehicleFilterOptionsRefreshWorker — строит in-memory кэш для выпадающих списков
  ├─ GET  /api/lots/vehicle-filter-options — отдаёт кэш фронтенду
  ├─ GET  /api/lots/list?attr_brand=...&attr_model=... — поиск лотов
  └─ Admin API — отчёты по неразобранным значениям, сброс нормализации
```

**Справочник марок и моделей:** `Lots.Application/Data/vehicle-catalog.json` (встроен в сборку как embedded resource).  
При необходимости можно переопределить путь через конфиг `VehicleCatalog:CatalogPath`.

---

## Данные в БД (`Lot.Attributes`, jsonb)

| Ключ | Описание |
|------|----------|
| `brand` | Каноническое имя марки (после нормализации) |
| `model` | Каноническое имя модели |
| `year` | Год выпуска (только цифры) |
| `mileage` | Пробег, км (только цифры) |
| `brand_raw` | Исходное значение марки, если оно менялось при нормализации |
| `model_raw` | Исходное значение модели, если менялось |
| `_attributes_parsed` | `true` — лот обработан DeepSeek (Scraper) |
| `_brand_normalized` | `true` — лот прошёл через normalizer |
| `_brand_matched` | `true` / `false` — марка найдена в справочнике |
| `_model_matched` | `true` / `false` — модель найдена в справочнике (при известной марке) |

Фильтрация по марке и модели **без учёта регистра** (`Kia` находит и `KIA`).

---

## Публичное API (без авторизации)

### Справочник для фильтров на сайте

```http
GET /api/lots/vehicle-filter-options
```

**Ответ:**

```json
{
  "brands": ["Chery", "Kia", "Toyota"],
  "modelsByBrand": {
    "Kia": ["Rio", "Spectra", "Sportage"]
  },
  "cachedAtUtc": "2026-06-16T12:00:00Z"
}
```

- Данные берутся из **in-memory кэша**, не из БД на каждый запрос.
- Кэш перестраивается при старте WebApi и далее по интервалу (`VehicleFilterOptions:RefreshIntervalMinutes`, по умолчанию 30 мин).
- После массовой нормализации кэш обновляется автоматически, если worker изменил лоты.

### Поиск лотов с фильтром по марке/модели

```http
GET /api/lots/list?categories=Легковой автомобиль&attr_brand=Kia&attr_model=Spectra&page=1&pageSize=20
```

Любой query-параметр с префиксом `attr_` превращается в динамический фильтр:

| Query | Фильтр в спецификации |
|-------|------------------------|
| `attr_brand=Kia` | `brand = Kia` (case-insensitive) |
| `attr_model=Spectra` | `model = Spectra` (case-insensitive) |
| `attr_year_from=2015` | год ≥ 2015 |
| `attr_year_to=2020` | год ≤ 2020 |
| `attr_mileage_from=50000` | пробег ≥ 50000 |

---

## Admin API

Все методы ниже — контроллер `AdminController`, базовый путь **`/api/admin`**.

### Авторизация

Заголовок обязателен:

```http
X-Admin-Api-Key: <значение из AdminSettings:ApiKey>
```

Ключ задаётся в конфигурации окружения / secrets (не хранить в git).

### Неразобранные марки

```http
GET /api/admin/vehicle-unmatched-brands?limit=100
```

Возвращает марки, которые **есть в лотах**, но **не найдены в справочнике** (`_brand_matched = false`), отсортированные по убыванию частоты.

**Пример ответа:**

```json
{
  "items": [
    { "brand": "ZX Auto", "count": 12 }
  ],
  "total": 1
}
```

> Если список пустой сразу после деплоя — worker ещё не успел проставить флаги. Дождитесь прогона или вызовите reset (см. ниже).

### Неразобранные модели

```http
GET /api/admin/vehicle-unmatched-models?limit=100
```

Марка в справочнике (`_brand_matched = true`), модель — нет (`_model_matched = false`).

**Пример ответа:**

```json
{
  "items": [
    { "brand": "Kia", "model": "Spectra", "count": 37 }
  ],
  "total": 1
}
```

### Сброс нормализации (после обновления справочника)

```http
POST /api/admin/vehicle-reset-normalization
```

Снимает у активных лотов «Легковой автомобиль» флаги:

- `_brand_normalized`
- `_brand_matched`
- `_model_matched`

После этого **VehicleAttributesNormalizationWorker** заново прогонит лоты батчами и пересчитает canonical-имена и флаги. При изменениях перестроит кэш фильтров.

**Пример ответа:**

```json
{
  "message": "Флаги нормализации сброшены. Worker прогонит лоты заново.",
  "resetCount": 1523
}
```

### Извлечение атрибутов DeepSeek (опционально)

```http
POST /api/admin/extract-vehicle-attributes
```

Запускает извлечение атрибутов в фоне. **В проде основной поток — FedresursScraper** (`VehicleAttributesBackgroundWorker`), а не WebApi. Эндпоинт полезен для ручного запуска, если в DI WebApi зарегистрирован `IVehicleAttributesExtractor`.

---

## Фоновые worker'ы WebApi

Настраиваются в `appsettings.json` (или ConfigMap / secrets в k8s):

```json
{
  "VehicleFilterOptions": {
    "RefreshIntervalMinutes": 30
  },
  "VehicleNormalization": {
    "BackfillEnabled": true,
    "BatchSize": 100,
    "IntervalMinutes": 60
  }
}
```

| Worker | Назначение |
|--------|------------|
| `VehicleFilterOptionsRefreshWorker` | Обновляет кэш марок/моделей для `vehicle-filter-options` |
| `VehicleAttributesNormalizationWorker` | Нормализует лоты без `_brand_matched`; включается при `BackfillEnabled: true` |

**Логи при успешном старте:**

```
Справочник марок/моделей загружен: N марок, M алиасов марок.
Фоновое обновление кэша марок/моделей запущено (интервал: 30 мин).
Фоновая нормализация марок/моделей запущена (батч: 100, интервал: 60 мин).
Кэш марок/моделей обновлён: N марок.
Нормализация завершена: обновлено X лотов, кэш фильтров перестроен.
```

Если при старте падает normalizer — проверьте валидность `vehicle-catalog.json` (должен быть корректный JSON).

---

## Типовые сценарии

### 1. Обновили справочник (добавили марки/алиасы)

1. Отредактировать `Lots.Application/Data/vehicle-catalog.json`
2. Собрать и задеплоить **WebApi** (и **Scraper**, если менялась логика extractor)
3. `POST /api/admin/vehicle-reset-normalization`

```bash
kubectl exec deployment/web-api-deployment -- bash -c 'apt-get update -qq && apt-get install -y -qq curl && curl -s -X POST http://localhost:8080/api/admin/vehicle-reset-normalization -H "X-Admin-Api-Key: $AdminSettings__ApiKey"'
```

4. Дождаться worker'а или перезапустить pod
5. Проверить: `GET /api/admin/vehicle-unmatched-brands` и `vehicle-unmatched-models`

### 2. Пополнить справочник по данным из БД

1. `GET /api/admin/vehicle-unmatched-brands?limit=50`
2. `GET /api/admin/vehicle-unmatched-models?limit=50`
3. Добавить canonical + aliases в JSON (см. формат ниже)
4. Deploy + reset + проверка

### 3. Проверить фильтры на фронте

1. `GET /api/lots/vehicle-filter-options` — одна марка в списке, без дублей `Kia` / `KIA`
2. `GET /api/lots/list?categories=Легковой автомобиль&attr_brand=Chery` — все варианты написания Chery

---

## Формат записи в справочнике

```json
{
  "canonical": "Chery",
  "aliases": ["CHERRY", "ЧЕРИ", "ЧЕРРИ"],
  "models": [
    {
      "canonical": "Tiggo 7",
      "aliases": ["Tiggo7", "Тигго 7"]
    }
  ]
}
```

Правила:

- **canonical** — то, что попадёт в `brand` / `model` и в выпадающие списки; при загрузке справочника автоматически попадает в индекс алиасов (дублировать в `aliases` не нужно)
- **aliases** — только **другие** известные варианты из объявлений и ответов DeepSeek (без повтора `canonical`, в том числе с другим регистром — `TIGGO 7` для `Tiggo 7` тоже лишний)
- **OCR-мусор не добавлять в aliases** — артикулы запчастей, внутренние коды площадок, случайные хвосты после модели/двигателя (например `HY5269` в `4G93 HY5269`). Такие фрагменты не кладём в справочник: они раздувают индекс и не обобщаются на другие лоты
- если extraction содержит OCR-мусор и однозначно определить модель можно только по смыслу заголовка/VIN — **в справочник правку не вносим**; в ответе указываем предполагаемую canonical-модель, **модель в лоте правится вручную** в admin UI
- стабильные заводские/маркетинговые коды (VDS-префиксы VIN, индексы ВАЗ, платформы вроде `GMT360`) — нормальные алиасы, если они реально встречаются в объявлениях
- модели нормализуются **в контексте марки** (одинаковая строка у разных марок — разные записи)
- алиасы сравниваются без учёта регистра; пробелы по краям обрезаются

---

## Полезные SQL-запросы (PostgreSQL)

**Неразобранные марки:**

```sql
SELECT attributes->>'brand' AS brand, COUNT(*) AS cnt
FROM "Lots" l
JOIN "LotCategories" lc ON lc."LotId" = l."Id" AND lc."Name" = 'Легковой автомобиль'
WHERE attributes->>'_brand_matched' = 'false'
  AND attributes ? 'brand'
GROUP BY 1
ORDER BY cnt DESC
LIMIT 50;
```

**Неразобранные модели:**

```sql
SELECT
  attributes->>'brand' AS brand,
  attributes->>'model' AS model,
  COUNT(*) AS cnt
FROM "Lots" l
JOIN "LotCategories" lc ON lc."LotId" = l."Id" AND lc."Name" = 'Легковой автомобиль'
WHERE attributes->>'_brand_matched' = 'true'
  AND attributes->>'_model_matched' = 'false'
  AND attributes ? 'model'
GROUP BY 1, 2
ORDER BY cnt DESC
LIMIT 50;
```

---

## Примеры вызовов (curl)

```bash
# Справочник для UI
curl https://<host>/api/lots/vehicle-filter-options

# Неразобранные марки
curl -H "X-Admin-Api-Key: $ADMIN_KEY" \
  "https://<host>/api/admin/vehicle-unmatched-brands?limit=50"

# Сброс после обновления JSON
curl -X POST -H "X-Admin-Api-Key: $ADMIN_KEY" \
  "https://<host>/api/admin/vehicle-reset-normalization"

# Поиск лотов
curl "https://<host>/api/lots/list?categories=Легковой%20автомобиль&attr_brand=Kia&attr_model=Spectra&page=1&pageSize=10"
```

---

## Разделение ответственности сервисов

| Компонент | Где живёт | Задача |
|-----------|-----------|--------|
| DeepSeek extraction | **FedresursScraper** | Первичное заполнение `brand`, `model`, `year`, `mileage` |
| Нормализация по справочнику | **WebApi** (+ Scraper при новых лотах) | Canonical-имена, флаги `_brand_matched` |
| Кэш фильтров + API | **WebApi** | Быстрые выпадающие списки на сайте |
| Справочник JSON | **Lots.Application** | Единый источник canonical и алиасов |

При изменении только `vehicle-catalog.json` достаточно redeploy **WebApi** + `vehicle-reset-normalization`.  
При изменении логики extractor — additionally **FedresursScraper**.
