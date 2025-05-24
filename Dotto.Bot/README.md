For development, you probably want to [set a user secret](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets) instead of editing the appsettings  
```sh
dotnet user-secrets set "Discord:Token" "YOURTTOKENHERE"
```