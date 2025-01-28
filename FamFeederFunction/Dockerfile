#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/azure-functions/dotnet:4-dotnet8.0 AS base
WORKDIR /home/site/wwwroot
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["FamFeederFunction/FamFeederFunction.csproj", "FamFeederFunction/"]
COPY Core/*csproj ./Core/
COPY Infrastructure/*csproj ./Infrastructure/

COPY nuget.config .

COPY . .

WORKDIR "/src/FamFeederFunction"

# nuget fix
ENV DOTNET_SYSTEM_NET_HTTP_USESOCKETSHTTPHANDLER=0
ENV NUGET_CREDENTIALPROVIDER_SESSIONTOKENCACHE_ENABLED true
ARG FEED_ACCESSTOKEN
RUN echo $FEED_ACCESSTOKEN
ENV VSS_NUGET_EXTERNAL_FEED_ENDPOINTS="{\"endpointCredentials\": [{\"endpoint\":\"https://statoildeveloper.pkgs.visualstudio.com/_packaging/ProCoSysOfficial/nuget/v3/index.json\", \"username\":\"docker\", \"password\":\"${FEED_ACCESSTOKEN}\"}]}"
RUN echo $VSS_NUGET_EXTERNAL_FEED_ENDPOINTS
RUN wget -qO- https://raw.githubusercontent.com/Microsoft/artifacts-credprovider/master/helpers/installcredprovider.sh | bash

RUN dotnet build "FamFeederFunction.csproj" -c Release

FROM build AS publish
RUN dotnet publish "FamFeederFunction.csproj" -c Release  --no-restore -o /app/publish

FROM base AS final
WORKDIR /home/site/wwwroot
COPY --from=publish /app/publish .
ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    AzureFunctionsJobHost__Logging__Console__IsEnabled=true
