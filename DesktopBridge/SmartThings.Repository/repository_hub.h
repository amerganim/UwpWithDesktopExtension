#pragma once
//
// RepositoryHub owns the SQLite database and all repositories. Its lifecycle methods fan out to
// every repository, so a single onForeground()/onClientReady() from the app refreshes them all.
//
#include <memory>
#include <string>
#include <vector>

#include "database.h"
#include "remote_source.h"
#include "repository.h"

namespace smartthings
{
    class RepositoryHub
    {
    public:
        RepositoryHub(const std::string& dbPath, std::shared_ptr<IRemoteSource> remote);

        // Lifecycle - each call is forwarded to every registered repository.
        void onClientReady();
        void onForeground();
        void onSignIn();
        void onSignOut();

        // Typed repositories the app subscribes to (returned as interfaces).
        ILocationRepository& locations() { return *locationRepository_; }
        IDeviceRepository& devices() { return *deviceRepository_; }

    private:
        Database database_;
        std::shared_ptr<IRemoteSource> remote_;
        std::shared_ptr<LocationRepository> locationRepository_;
        std::shared_ptr<DeviceRepository> deviceRepository_;
        std::vector<IRepository*> repositories_;
    };
}
