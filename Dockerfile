FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app

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

# Обновляем пакеты еще раз и ставим сам браузер и все нужные ему библиотеки
RUN apt-get update && apt-get install -y --no-install-recommends \
    google-chrome-stable \
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

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Копируем только файлы, нужные для восстановления зависимостей
COPY ["FedresursSolution.sln", "."]
COPY ["FedresursScraper/FedresursScraper.csproj", "FedresursScraper/"]
COPY ["Lots.Data/Lots.Data.csproj", "Lots.Data/"]

# Восстанавливаем зависимости всего решения
RUN dotnet restore "FedresursSolution.sln"

# Копируем весь остальной код
COPY . .

# Запускаем публикацию (указывая главный проект)
RUN dotnet publish "FedresursScraper/FedresursScraper.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "FedresursScraper.dll"]
