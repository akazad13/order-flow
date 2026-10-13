# One Dockerfile for every .NET service: docker build --build-arg PROJECT=OrderFlow.Order -f build/service.Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish "src/${PROJECT}/${PROJECT}.csproj" -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG PROJECT
ENV APP_DLL=${PROJECT}.dll
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["sh", "-c", "exec dotnet \"$APP_DLL\""]
