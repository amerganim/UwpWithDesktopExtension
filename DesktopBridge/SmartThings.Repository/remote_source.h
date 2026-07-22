#pragma once
//
// The repository's remote data source - i.e. the REST API. Per the design, the actual REST calls
// live in "another project"; the repository only depends on this interface. Inject a real
// implementation (HTTP client) in production; the stub below returns sample data so the library can
// run standalone.
//
#include <functional>

#include "models.h"

namespace smartthings
{
    struct IRemoteSource
    {
        virtual ~IRemoteSource() = default;

        // Async by contract (a real REST client invokes the callback when the response arrives).
        virtual void fetchLocations(std::function<void(LocationList)> onResult) = 0;
        virtual void fetchDevices(std::function<void(DeviceList)> onResult) = 0;
    };

    // Stand-in for the REST project: returns canned data synchronously.
    class StubRemoteSource : public IRemoteSource
    {
    public:
        void fetchLocations(std::function<void(LocationList)> onResult) override
        {
            onResult(LocationList{
                {"loc-1", "Home"},
                {"loc-2", "Office"},
            });
        }

        void fetchDevices(std::function<void(DeviceList)> onResult) override
        {
            onResult(DeviceList{
                {"dev-1", "Living Room Light", "loc-1"},
                {"dev-2", "Front Door Lock", "loc-1"},
                {"dev-3", "Office Thermostat", "loc-2"},
            });
        }
    };
}
