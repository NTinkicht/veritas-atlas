# JWT Console Trace Report

Generated: 2026-04-07 22:51:39
BaseUrl: http://localhost:5091

## API Process

```
HasExited=True
Id=21468
```

## Login Result

```
{
    "accessToken":  "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1lIjoiYWRtaW4xIiwiaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS93cy8yMDA4LzA2L2lkZW50aXR5L2NsYWltcy9yb2xlIjoiYWRtaW4iLCJwcmVmZXJyZWRfdXNlcm5hbWUiOiJhZG1pbjEiLCJyb2xlIjoiYWRtaW4iLCJleHAiOjE3NzU2MTY3MDksImlzcyI6IlZlcml0YXNBdGxhcyIsImF1ZCI6IlZlcml0YXNBdGxhc1VzZXJzIn0.tqFticeZtiAndTevq2ClsffWUmx6wmWTQopci5wWtOw",
    "tokenType":  "Bearer",
    "expiresAtUtc":  "2026-04-08T02:51:49.5513259Z",
    "username":  "admin1",
    "role":  "admin"
}
```

## Auth Me Result

```
{
    "user":  "admin1",
    "role":  "admin",
    "isAuthenticated":  true,
    "claims":  [
                   {
                       "type":  "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name",
                       "value":  "admin1"
                   },
                   {
                       "type":  "http://schemas.microsoft.com/ws/2008/06/identity/claims/role",
                       "value":  "admin"
                   },
                   {
                       "type":  "preferred_username",
                       "value":  "admin1"
                   },
                   {
                       "type":  "http://schemas.microsoft.com/ws/2008/06/identity/claims/role",
                       "value":  "admin"
                   },
                   {
                       "type":  "exp",
                       "value":  "1775616709"
                   },
                   {
                       "type":  "iss",
                       "value":  "VeritasAtlas"
                   },
                   {
                       "type":  "aud",
                       "value":  "VeritasAtlasUsers"
                   }
               ]
}
```

## API STDOUT

```
Utilisation des paramÃ¨tres de lancement Ã  partir de C:\Projects\veritas-atlas\apps\api\VeritasAtlas.Api\Properties\launchSettings.json...
GÃ©nÃ©ration...
fail: Microsoft.Extensions.Hosting.Internal.Host[11]
      Hosting failed to start
      System.IO.IOException: Failed to bind to address https://127.0.0.1:7091: address already in use.
       ---> Microsoft.AspNetCore.Connections.AddressInUseException: Une seule utilisation de chaque adresse de socket (protocole/adresse rÃ©seau/port) est habituellement autorisÃ©e.
       ---> System.Net.Sockets.SocketException (10048): Une seule utilisation de chaque adresse de socket (protocole/adresse rÃ©seau/port) est habituellement autorisÃ©e.
         at System.Net.Sockets.Socket.UpdateStatusAfterSocketErrorAndThrowException(SocketError error, Boolean disconnectOnFailure, String callerName)
         at System.Net.Sockets.Socket.DoBind(EndPoint endPointSnapshot, SocketAddress socketAddress)
         at System.Net.Sockets.Socket.Bind(EndPoint localEP)
         at Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.SocketTransportOptions.CreateDefaultBoundListenSocket(EndPoint endpoint)
         at Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.SocketConnectionListener.Bind()
         --- End of inner exception stack trace ---
         at Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.SocketConnectionListener.Bind()
         at Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.SocketTransportFactory.BindAsync(EndPoint endpoint, CancellationToken cancellationToken)
         at Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure.TransportManager.BindAsync(EndPoint endPoint, ConnectionDelegate connectionDelegate, EndpointConfig endpointConfig, CancellationToken cancellationToken)
         at Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerImpl.<>c__DisplayClass28_0`1.<<StartAsync>g__OnBind|0>d.MoveNext()
      --- End of stack trace from previous location ---
         at Microsoft.AspNetCore.Server.Kestrel.Core.Internal.AddressBinder.BindEndpointAsync(ListenOptions endpoint, AddressBindContext context, CancellationToken cancellationToken)
         --- End of inner exception stack trace ---
         at Microsoft.AspNetCore.Server.Kestrel.Core.Internal.AddressBinder.BindEndpointAsync(ListenOptions endpoint, AddressBindContext context, CancellationToken cancellationToken)
         at Microsoft.AspNetCore.Server.Kestrel.Core.LocalhostListenOptions.BindAsync(AddressBindContext context, CancellationToken cancellationToken)
         at Microsoft.AspNetCore.Server.Kestrel.Core.Internal.AddressBinder.AddressesStrategy.BindAsync(AddressBindContext context, CancellationToken cancellationToken)
         at Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerImpl.BindAsync(CancellationToken cancellationToken)
         at Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerImpl.StartAsync[TContext](IHttpApplication`1 application, CancellationToken cancellationToken)
         at Microsoft.AspNetCore.Hosting.GenericWebHostService.StartAsync(CancellationToken cancellationToken)
         at Microsoft.Extensions.Hosting.Internal.Host.<StartAsync>b__14_1(IHostedService service, CancellationToken token)
         at Microsoft.Extensions.Hosting.Internal.Host.ForeachService[T](IEnumerable`1 services, CancellationToken token, Boolean concurrent, Boolean abortOnFirstException, List`1 exceptions, Func`3 operation)

```

## API STDERR

```
Unhandled exception. System.IO.IOException: Failed to bind to address https://127.0.0.1:7091: address already in use.
 ---> Microsoft.AspNetCore.Connections.AddressInUseException: Une seule utilisation de chaque adresse de socket (protocole/adresse rÃ©seau/port) est habituellement autorisÃ©e.
 ---> System.Net.Sockets.SocketException (10048): Une seule utilisation de chaque adresse de socket (protocole/adresse rÃ©seau/port) est habituellement autorisÃ©e.
   at System.Net.Sockets.Socket.UpdateStatusAfterSocketErrorAndThrowException(SocketError error, Boolean disconnectOnFailure, String callerName)
   at System.Net.Sockets.Socket.DoBind(EndPoint endPointSnapshot, SocketAddress socketAddress)
   at System.Net.Sockets.Socket.Bind(EndPoint localEP)
   at Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.SocketTransportOptions.CreateDefaultBoundListenSocket(EndPoint endpoint)
   at Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.SocketConnectionListener.Bind()
   --- End of inner exception stack trace ---
   at Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.SocketConnectionListener.Bind()
   at Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.SocketTransportFactory.BindAsync(EndPoint endpoint, CancellationToken cancellationToken)
   at Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Infrastructure.TransportManager.BindAsync(EndPoint endPoint, ConnectionDelegate connectionDelegate, EndpointConfig endpointConfig, CancellationToken cancellationToken)
   at Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerImpl.<>c__DisplayClass28_0`1.<<StartAsync>g__OnBind|0>d.MoveNext()
--- End of stack trace from previous location ---
   at Microsoft.AspNetCore.Server.Kestrel.Core.Internal.AddressBinder.BindEndpointAsync(ListenOptions endpoint, AddressBindContext context, CancellationToken cancellationToken)
   --- End of inner exception stack trace ---
   at Microsoft.AspNetCore.Server.Kestrel.Core.Internal.AddressBinder.BindEndpointAsync(ListenOptions endpoint, AddressBindContext context, CancellationToken cancellationToken)
   at Microsoft.AspNetCore.Server.Kestrel.Core.LocalhostListenOptions.BindAsync(AddressBindContext context, CancellationToken cancellationToken)
   at Microsoft.AspNetCore.Server.Kestrel.Core.Internal.AddressBinder.AddressesStrategy.BindAsync(AddressBindContext context, CancellationToken cancellationToken)
   at Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerImpl.BindAsync(CancellationToken cancellationToken)
   at Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerImpl.StartAsync[TContext](IHttpApplication`1 application, CancellationToken cancellationToken)
   at Microsoft.AspNetCore.Hosting.GenericWebHostService.StartAsync(CancellationToken cancellationToken)
   at Microsoft.Extensions.Hosting.Internal.Host.<StartAsync>b__14_1(IHostedService service, CancellationToken token)
   at Microsoft.Extensions.Hosting.Internal.Host.ForeachService[T](IEnumerable`1 services, CancellationToken token, Boolean concurrent, Boolean abortOnFirstException, List`1 exceptions, Func`3 operation)
   at Microsoft.Extensions.Hosting.Internal.Host.StartAsync(CancellationToken cancellationToken)
   at Microsoft.Extensions.Hosting.HostingAbstractionsHostExtensions.RunAsync(IHost host, CancellationToken token)
   at Microsoft.Extensions.Hosting.HostingAbstractionsHostExtensions.RunAsync(IHost host, CancellationToken token)
   at Microsoft.Extensions.Hosting.HostingAbstractionsHostExtensions.Run(IHost host)
   at Program.<Main>$(String[] args) in C:\Projects\veritas-atlas\apps\api\VeritasAtlas.Api\Program.cs:line 125

```

