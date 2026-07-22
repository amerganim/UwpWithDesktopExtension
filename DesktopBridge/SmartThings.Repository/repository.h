#pragma once
//
// Repository layer.
//
//   IRepository            - the "base repository" interface: the lifecycle hooks that the hub
//                            fans out to every repository (onClientReady / onForeground /
//                            onSignIn / onSignOut).
//   BaseRepository<T>      - common implementation: owns a BehaviorSubject<vector<T>>, primes it
//                            from the SQLite cache on construction, and refreshes from the remote
//                            source on the lifecycle hooks.
//   ILocationRepository /
//   IDeviceRepository      - typed repository interfaces the app subscribes to.
//   LocationRepository /
//   DeviceRepository       - concrete implementations (cache <-> remote).
//
#include <functional>

#include "database.h"
#include "models.h"
#include "remote_source.h"
#include "rx.h"

namespace smartthings
{
    // Base repository interface - the lifecycle surface driven by the hub.
    struct IRepository
    {
        virtual ~IRepository() = default;
        virtual void onClientReady() = 0;
        virtual void onForeground() = 0;
        virtual void onSignIn() = 0;
        virtual void onSignOut() = 0;
    };

    // Common repository behaviour over a collection of T.
    template <class T>
    class BaseRepository : public IRepository
    {
    public:
        BaseRepository(Database& db, IRemoteSource& remote) : db_(db), remote_(remote) {}

        // Rx-like stream the UseCase / UWP layer subscribes to.
        [[nodiscard]] rx::Subscription observe(std::function<void(const std::vector<T>&)> observer)
        {
            return subject_.subscribe(std::move(observer));
        }

        // Lifecycle (invoked by RepositoryHub). Cache is already primed; refresh from remote.
        void onClientReady() override { refreshFromRemote(); }
        void onForeground() override { refreshFromRemote(); }
        void onSignIn() override { refreshFromRemote(); }
        void onSignOut() override { writeCache({}); subject_.next({}); }

    protected:
        virtual std::vector<T> readCache() = 0;
        virtual void writeCache(const std::vector<T>& items) = 0;
        virtual void fetchRemote(std::function<void(std::vector<T>)> onResult) = 0;

        // "Return data from cache on constructor": call this from the *derived* constructor (when
        // virtual dispatch is live) so any subscriber immediately receives the last-known data.
        void primeFromCache() { subject_.next(readCache()); }

        Database& db_;
        IRemoteSource& remote_;

    private:
        void refreshFromRemote()
        {
            fetchRemote([this](std::vector<T> items) {
                writeCache(items);      // update the cache...
                subject_.next(items);   // ...and push the fresh server data to subscribers.
            });
        }

        rx::BehaviorSubject<std::vector<T>> subject_;
    };

    // Typed repository interfaces ("all repository classes are interfaces").
    struct ILocationRepository
    {
        virtual ~ILocationRepository() = default;
        virtual rx::Subscription observe(std::function<void(const LocationList&)> observer) = 0;
    };

    struct IDeviceRepository
    {
        virtual ~IDeviceRepository() = default;
        virtual rx::Subscription observe(std::function<void(const DeviceList&)> observer) = 0;
    };

    class LocationRepository : public BaseRepository<Location>, public ILocationRepository
    {
    public:
        LocationRepository(Database& db, IRemoteSource& remote) : BaseRepository(db, remote) { primeFromCache(); }

        rx::Subscription observe(std::function<void(const LocationList&)> observer) override
        {
            return BaseRepository::observe(std::move(observer));
        }

    protected:
        LocationList readCache() override { return db_.readLocations(); }
        void writeCache(const LocationList& items) override { db_.replaceLocations(items); }
        void fetchRemote(std::function<void(LocationList)> onResult) override { remote_.fetchLocations(std::move(onResult)); }
    };

    class DeviceRepository : public BaseRepository<Device>, public IDeviceRepository
    {
    public:
        DeviceRepository(Database& db, IRemoteSource& remote) : BaseRepository(db, remote) { primeFromCache(); }

        rx::Subscription observe(std::function<void(const DeviceList&)> observer) override
        {
            return BaseRepository::observe(std::move(observer));
        }

    protected:
        DeviceList readCache() override { return db_.readDevices(); }
        void writeCache(const DeviceList& items) override { db_.replaceDevices(items); }
        void fetchRemote(std::function<void(DeviceList)> onResult) override { remote_.fetchDevices(std::move(onResult)); }
    };
}
