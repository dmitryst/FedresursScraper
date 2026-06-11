#!/bin/bash

# === КОНФИГУРАЦИЯ ===
WORK_DIR="/b/T/s-lot.ru"
LOCAL_DICTS_PATH="/b/Projects/postgres"

K8S_LABEL="app=postgres"
DB_USER="postgres"
DB_NAME_SOURCE="lot_db"
LOCAL_DOCKER_NAME="local-postgres"
LOCAL_PORT="5433"
DOCKER_IMAGE="postgres:16"

CURRENT_DATE=$(date +%Y-%m-%d)
DB_NAME_TARGET="lot_db_restored_${CURRENT_DATE}"
LOCAL_DUMP_FILE="dump_${CURRENT_DATE}.backup"

GREEN='\033[0;32m'
CYAN='\033[0;36m'
RED='\033[0;31m'
NC='\033[0m'
log() { echo -e "${CYAN}[$(date +'%H:%M:%S')]${NC} $1"; }
set -e

# 0. Проверка словарей
log "Проверка словарей в $LOCAL_DICTS_PATH..."
[ ! -f "$LOCAL_DICTS_PATH/ru_ru.dict" ] && { echo -e "${RED}ru_ru.dict не найден!${NC}"; exit 1; }

if [ -f "$LOCAL_DICTS_PATH/ru_ru.aff" ]; then 
    AFF_NAME="ru_ru.aff"
elif [ -f "$LOCAL_DICTS_PATH/ru_ru.affix" ]; then 
    AFF_NAME="ru_ru.affix"
else 
    echo -e "${RED}.aff/.affix не найден!${NC}"; exit 1
fi
echo "Файл аффиксов: $AFF_NAME"

log "Переход в: $WORK_DIR"
cd "$WORK_DIR"

# 1. Поиск пода
log "Поиск пода..."
POD_NAME=$(kubectl get pods -l $K8S_LABEL -o jsonpath="{.items[0].metadata.name}")
[ -z "$POD_NAME" ] && { echo -e "${RED}Под не найден!${NC}"; exit 1; }
echo -e "${GREEN}Под: $POD_NAME${NC}"

# 2. Асинхронный дамп
REMOTE_DUMP="/tmp/dump.backup"
REMOTE_STATUS="/tmp/dump.status"
REMOTE_LOG="/tmp/dump.log"

log "Очистка K8s..."
MSYS_NO_PATHCONV=1 kubectl exec "$POD_NAME" -- rm -f $REMOTE_DUMP $REMOTE_STATUS $REMOTE_LOG

log "Запуск pg_dump..."
CMD="nohup sh -c '(pg_dump -U $DB_USER -d $DB_NAME_SOURCE -F c -b -f $REMOTE_DUMP > $REMOTE_LOG 2>&1 && echo SUCCESS > $REMOTE_STATUS) || (echo FAIL > $REMOTE_STATUS)' > /dev/null 2>&1 &"
MSYS_NO_PATHCONV=1 kubectl exec "$POD_NAME" -- sh -c "$CMD"

echo -n "Ждем дамп"
while true; do
    STATUS=$(MSYS_NO_PATHCONV=1 kubectl exec "$POD_NAME" -- cat $REMOTE_STATUS 2>/dev/null || true)
    if [[ "$STATUS" == "SUCCESS"* ]]; then echo -e "\n${GREEN}Готово!${NC}"; break; fi
    if [[ "$STATUS" == "FAIL"* ]]; then
        echo -e "\n${RED}Ошибка:${NC}"; MSYS_NO_PATHCONV=1 kubectl exec "$POD_NAME" -- cat $REMOTE_LOG; exit 1
    fi
    echo -n "."; sleep 5
done

# 3. Скачивание (сохраняем с датой)
log "Скачивание в ./$LOCAL_DUMP_FILE ..."
MSYS_NO_PATHCONV=1 kubectl exec "$POD_NAME" -- cat $REMOTE_DUMP > "./$LOCAL_DUMP_FILE"
MSYS_NO_PATHCONV=1 kubectl exec "$POD_NAME" -- rm -f $REMOTE_DUMP $REMOTE_STATUS $REMOTE_LOG

# 4. Docker
log "Подготовка Docker..."
if [ "$(docker ps -a -q -f name=$LOCAL_DOCKER_NAME)" ]; then
    log "Пересоздание контейнера..."
    docker rm -f $LOCAL_DOCKER_NAME > /dev/null
fi

docker run --name $LOCAL_DOCKER_NAME -e POSTGRES_PASSWORD=$DB_USER -d -p "${LOCAL_PORT}:5432" $DOCKER_IMAGE
echo "Ждем старта (5 сек)..."
sleep 5

log "Установка словарей..."
DICT_DIR=$(MSYS_NO_PATHCONV=1 docker exec $LOCAL_DOCKER_NAME find /usr/share/postgresql -type d -name "tsearch_data" | head -n 1 | tr -d '\r')
echo "Путь внутри: '$DICT_DIR'"
WIN_DICTS_PATH=$(cygpath -m "$LOCAL_DICTS_PATH")

if [ -n "$DICT_DIR" ]; then
    MSYS_NO_PATHCONV=1 docker cp "$WIN_DICTS_PATH/ru_ru.dict" "$LOCAL_DOCKER_NAME:$DICT_DIR/ru_ru.dict"
    MSYS_NO_PATHCONV=1 docker cp "$WIN_DICTS_PATH/$AFF_NAME" "$LOCAL_DOCKER_NAME:$DICT_DIR/$AFF_NAME"
    
    # Создаем симлинк для надежности (чтобы было и .aff, и .affix)
    if [ "$AFF_NAME" == "ru_ru.affix" ]; then
        MSYS_NO_PATHCONV=1 docker exec $LOCAL_DOCKER_NAME ln -s "$DICT_DIR/ru_ru.affix" "$DICT_DIR/ru_ru.aff"
    elif [ "$AFF_NAME" == "ru_ru.aff" ]; then
        MSYS_NO_PATHCONV=1 docker exec $LOCAL_DOCKER_NAME ln -s "$DICT_DIR/ru_ru.aff" "$DICT_DIR/ru_ru.affix"
    fi

    MSYS_NO_PATHCONV=1 docker exec -u 0 $LOCAL_DOCKER_NAME chown postgres:postgres "$DICT_DIR/ru_ru.dict" "$DICT_DIR/$AFF_NAME"
    echo "Словари скопированы."
else
    echo -e "${RED}Не найдена папка словарей!${NC}"; exit 1
fi

# 5. Восстановление
log "Загрузка бэкапа в контейнер..."
# Копируем локальный файл с датой во временный файл внутри контейнера
MSYS_NO_PATHCONV=1 docker cp "./$LOCAL_DUMP_FILE" "$LOCAL_DOCKER_NAME:/tmp/dump.backup"

log "Создание базы $DB_NAME_TARGET..."
MSYS_NO_PATHCONV=1 docker exec "$LOCAL_DOCKER_NAME" createdb -U "$DB_USER" "$DB_NAME_TARGET"

log "Восстановление данных..."
set +e
MSYS_NO_PATHCONV=1 docker exec "$LOCAL_DOCKER_NAME" pg_restore -U "$DB_USER" -d "$DB_NAME_TARGET" -v --no-owner --role="$DB_USER" /tmp/dump.backup
RES=$?
set -e

if [ $RES -eq 0 ]; then
    echo -e "${GREEN}=== УСПЕШНО ЗАВЕРШЕНО ===${NC}"
else
    echo -e "${CYAN}=== ГОТОВО (с warnings) ===${NC}"
fi

echo "Файл дампа: $LOCAL_DUMP_FILE"
echo "База: $DB_NAME_TARGET"
