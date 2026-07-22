# SmartThings.Repository (C++ repository library)

A native C++ library that caches SmartThings data in **SQLite** and exposes it to the app as an
**Rx-like, subject/observer** stream. The C# UWP app subscribes through **UseCase** classes and
receives data first from the local cache, then refreshed from the server.

## Design

```
        UWP (C#)                    C++ library (this project)                REST project
  ┌──────────────────┐       ┌───────────────────────────────────┐      ┌──────────────────┐
  │ LocationUseCase   │ sub → │ ILocationRepository (BehaviorSubj) │      │  IRemoteSource    │
  │ DeviceUseCase     │ ────→ │ IDeviceRepository                  │ ───→ │  (HTTP/REST)      │
  └──────────────────┘       │ RepositoryHub  ── SQLite cache      │      └──────────────────┘
        ▲   subscribe        │   onClientReady/onForeground/...    │
        └── onNext(cache) ───┘   fan out to every repository       │
            onNext(server) ─────────────────────────────────────── ┘
```

- **`IRepository`** (base repository interface) — lifecycle hooks `onClientReady`, `onForeground`,
  `onSignIn`, `onSignOut`. `RepositoryHub` fans each call out to **every** repository.
- **`BaseRepository<T>`** — common implementation: owns a `BehaviorSubject<vector<T>>`, primes it
  from the SQLite cache on construction, and refreshes from the remote source on the lifecycle hooks.
- **`ILocationRepository` / `IDeviceRepository`** — typed repository interfaces the app subscribes
  to; **`LocationRepository` / `DeviceRepository`** are the concrete implementations.
- **`BehaviorSubject<T>`** (`rx.h`) — the "Rx like C#" piece: it replays the latest value to every
  new subscriber, so a subscriber gets the cache immediately and then the server value via `next()`.
- **`IRemoteSource`** — the REST API. The real HTTP calls live in *another project*; the repository
  depends only on this interface (a `StubRemoteSource` returns sample data so the lib runs alone).
- **SQLite** — via the OS-provided `winsqlite3` (no third-party SQLite to bundle).

### Data flow (matches the requirement)
1. **Constructor** → read SQLite cache → `subject.next(cache)`. A subscriber (UseCase) gets cached
   data immediately.
2. **`onForeground()` / `onClientReady()`** → `IRemoteSource` fetch → write cache → `subject.next(server)`.
   Subscribers get the fresh server data.
3. **`onSignOut()`** → clear cache and push empty.

## Try it (native, no UWP needed)

`SmartThings.Repository.Demo` is a console app that subscribes and drives the lifecycle:

```sh
msbuild SmartThings.Repository.Demo/SmartThings.Repository.Demo.vcxproj -p:Configuration=Release -p:Platform=x64
DesktopBridge/SmartThings.Repository.Demo/x64/Release/SmartThings.Repository.Demo.exe
```

It prints the cached value on subscribe (empty on first run), then the server values after
`onClientReady()` / `onForeground()`, proving the subject/observer + cache-then-remote flow with
real SQLite.

## Consuming from the C# UWP app (the bridge)

UWP (C#) cannot reference a native C++ library directly with events. The bridge is a **C++/WinRT
Windows Runtime Component** that wraps these classes and projects them to C#:

- Author an `.idl` exposing runtime classes `RepositoryHub`, `LocationRepository`, `DeviceRepository`
  and model classes `Location`, `Device`.
- Expose the subject as a WinRT **event** (e.g. `event ...Changed`) or an `IObservableVector<T>`
  (both project to C# naturally). `next()` raises the event; C# `+=` is the observer.
- The UWP app then uses **`LocationUseCase` / `DeviceUseCase`** (see [`bridge-csharp/`](bridge-csharp/))
  which subscribe to the projected repository and re-expose it to the app as an `IObservable` /
  event.

The WinRT component + UWP wiring build only with the full Visual Studio UWP/C++ workload (CI), so
it is provided as the documented next step; the native library and its behavior above are complete
and verified.
