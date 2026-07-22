//
// Console demo that exercises the repository library the way the UWP app would:
//   - subscribe to a repository (observer) and receive the cached value immediately (replay),
//   - drive the lifecycle (onClientReady / onForeground) and receive the refreshed server value.
//
// Run it twice: the first run starts with an empty cache and fills it from the (stub) remote;
// the second run replays the persisted SQLite cache to the subscriber before the remote refresh.
//
#include <cstdio>
#include <memory>

#include "../SmartThings.Repository/models.h"
#include "../SmartThings.Repository/remote_source.h"
#include "../SmartThings.Repository/repository_hub.h"

using namespace smartthings;

int main()
{
    auto remote = std::make_shared<StubRemoteSource>();
    RepositoryHub hub("smartthings-demo.db", remote);

    // The app / UseCase subscribes here (observer). BehaviorSubject replays the current cache now.
    auto locationSub = hub.locations().observe([](const LocationList& locations) {
        std::printf("[locations] %zu item(s)\n", locations.size());
        for (const auto& location : locations)
            std::printf("    - %s (%s)\n", location.name.c_str(), location.id.c_str());
    });
    auto deviceSub = hub.devices().observe([](const DeviceList& devices) {
        std::printf("[devices]   %zu item(s)\n", devices.size());
        for (const auto& device : devices)
            std::printf("    - %s (%s @ %s)\n", device.name.c_str(), device.id.c_str(), device.locationId.c_str());
    });

    std::printf("\n== subscribed (values above came from the SQLite cache) ==\n");
    std::printf("\n== onClientReady() -> fan out to all repositories -> fetch remote ==\n");
    hub.onClientReady();

    std::printf("\n== onForeground() -> refresh again from remote ==\n");
    hub.onForeground();

    std::printf("\n== onSignOut() -> clear ==\n");
    hub.onSignOut();

    return 0;
}
