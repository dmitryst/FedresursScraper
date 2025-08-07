# --- Этап сборки (Build Stage) ---
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Копируем .csproj и восстанавливаем зависимости
COPY ["Lots.Data.csproj", "."]
RUN dotnet restore "Lots.Data.csproj"

# Устанавливаем инструменты EF Core локально
RUN dotnet tool install --global dotnet-ef

# Добавляем инструменты в PATH
ENV PATH="$PATH:/root/.dotnet/tools"

# Копируем остальной код и собираем приложение
COPY . .
WORKDIR "/src/."
RUN dotnet build "Lots.Data.csproj" -c Release -o /app/build

# --- Этап публикации (Publish Stage) ---
FROM build AS publish
RUN dotnet publish "Lots.Data.csproj" -c Release -o /app/publish

# --- Финальный этап (Final Stage) ---
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Копируем инструменты EF Core из этапа сборки
COPY --from=build /root/.dotnet/tools /root/.dotnet/tools
ENV PATH="$PATH:/root/.dotnet/tools"

ENTRYPOINT ["dotnet", "Lots.Data.dll"]
