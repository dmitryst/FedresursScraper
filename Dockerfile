# Объявляем аргумент сборки с версией по умолчанию
ARG BUILD_VERSION=1.1.0

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
# Повторяем ARG, чтобы сделать его доступным внутри этого этапа
ARG BUILD_VERSION

WORKDIR /app

# ---ЗАМЕНА РЕПОЗИТОРИЕВ НА ЗЕРКАЛО ЯНДЕКСА ---
RUN echo "deb http://mirror.yandex.ru/debian/ bookworm main" > /etc/apt/sources.list && \
    echo "deb http://mirror.yandex.ru/debian/ bookworm-updates main" >> /etc/apt/sources.list && \
    echo "deb http://security.debian.org/debian-security/ bookworm-security main" >> /etc/apt/sources.list

# --- УСТАНОВКА GOOGLE CHROME И ВСЕХ ЗАВИСИМОСТЕЙ ---
# Обновляем пакеты и ставим утилиты для добавления репозитория
RUN apt-get update && apt-get install -y --no-install-recommends \
    wget \
    gnupg \
    ca-certificates

# Добавляем официальный ключ подписи Google
RUN wget -q -O - https://dl.google.com/linux/linux_signing_key.pub | gpg --dearmor -o /usr/share/keyrings/google-chrome-keyring.gpg

# Добавляем официальный репозиторий Google Chrome
RUN echo "deb [arch=amd64 signed-by=/usr/share/keyrings/google-chrome-keyring.gpg] http://dl.google.com/linux/chrome/deb/ stable main" > /etc/apt/sources.list.d/google-chrome.list

# Обновляем пакеты еще раз и ставим браузер google-chrome и все нужные ему библиотеки
RUN apt-get update && apt-get install -y --no-install-recommends \
    google-chrome-stable \
    wget \
    unzip \
    jq \
    libglib2.0-0 \
    libnss3 \
    libgconf-2-4 \
    libfontconfig1 \
    libatk1.0-0 \
    libatk-bridge2.0-0 \
    libcups2 \
    libdbus-1-3 \
    libdrm2 \
    libgbm1 \
    libgtk-3-0 \
    libx11-6 \
    libxcb1 \
    libxcomposite1 \
    libxdamage1 \
    libxext6 \
    libxfixes3 \
    libxrandr2 \
    libxtst6 \
    fonts-liberation \
    lsb-release \
    xdg-utils \
    && rm -rf /var/lib/apt/lists/*

# Скачиваем и устанавливаем chromedriver последней стабильной версии
# RUN wget -q --continue -P /tmp https://storage.googleapis.com/chrome-for-testing-public/138.0.7204.183/linux64/chromedriver-linux64.zip && \
#     unzip -q /tmp/chromedriver-linux64.zip -d /tmp && \
#     mv /tmp/chromedriver-linux64/chromedriver /usr/bin/chromedriver && \
#     chmod +x /usr/bin/chromedriver

# Устанавливаем версию как переменную окружения для приложения
ENV AppInfo__Version=$BUILD_VERSION

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Копируем только файлы, нужные для восстановления зависимостей
COPY ["FedresursSolution.sln", "."]
COPY ["FedresursScraper/FedresursScraper.csproj", "FedresursScraper/"]
COPY ["Lots.Data/Lots.Data.csproj", "Lots.Data/"]
COPY ["FedresursScraper.Tests/FedresursScraper.Tests.csproj", "FedresursScraper.Tests/"]

# Восстанавливаем зависимости всего решения
RUN dotnet restore "FedresursSolution.sln"

# Копируем весь остальной код
COPY . .

# Запускаем публикацию (указывая главный проект)
RUN dotnet publish "FedresursScraper/FedresursScraper.csproj" -c Release -o /app/publish

# Копируем результат сборки в рабочий образ
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "FedresursScraper.dll"]
