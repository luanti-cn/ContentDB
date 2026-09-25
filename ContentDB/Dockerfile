# ContentDB C# Mirror —— 多项目分层构建
# 结构:ContentDB(Api,根目录) + ContentDB.Core + ContentDB.Infrastructure(子文件夹)

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# 先拷贝 csproj 以利用还原缓存
COPY ["ContentDB.csproj", "./"]
COPY ["ContentDB.Core/ContentDB.Core.csproj", "ContentDB.Core/"]
COPY ["ContentDB.Infrastructure/ContentDB.Infrastructure.csproj", "ContentDB.Infrastructure/"]
RUN dotnet restore "./ContentDB.csproj"

# 拷贝其余源码并发布
COPY . .
RUN dotnet publish "./ContentDB.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ContentDB.dll"]
